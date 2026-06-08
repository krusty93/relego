using MimeKit;
using Polly.Retry;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Resilience;
using Relego.Server.Infrastructure.Smtp;
using Microsoft.Extensions.Options;

namespace Relego.Server.Services;

public sealed class RecapService : IRecapService
{
    private readonly HighlightSelectionService _selectionService;
    private readonly IMailDeliveryService _mailDeliveryService;
    private readonly RecapRepository _recapRepository;
    private readonly UserRepository _userRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger<RecapService> _logger;
    private readonly string _fromAddress;

    public RecapService(
        HighlightSelectionService selectionService,
        IMailDeliveryService mailDeliveryService,
        RecapRepository recapRepository,
        UserRepository userRepository,
        SettingsRepository settingsRepository,
        IOptions<SmtpSettings> smtpSettings,
        ILogger<RecapService> logger)
    {
        _selectionService = selectionService;
        _mailDeliveryService = mailDeliveryService;
        _recapRepository = recapRepository;
        _userRepository = userRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
        _retryPolicy = RecapDeliveryPolicy.Create(logger);
        _fromAddress = smtpSettings.Value.FromAddress;
    }

    public async Task ExecuteAsync(int userId, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default)
    {
        var jobId = await _recapRepository.CreateJobAsync(userId, scheduledFor);

        var settings = await _settingsRepository.GetByUserIdAsync(userId);
        var candidates = await _selectionService.SelectAsync(userId, settings, scheduledFor, cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No eligible highlights for user {UserId} at slot {ScheduledFor}. Skipping delivery", userId, scheduledFor);
            await _recapRepository.UpdateJobFailedAsync(jobId, "No eligible highlights available.", attemptCount: 0);
            return;
        }

        var user = await _userRepository.GetByIdAsync(userId);

        var hasKindle = !string.IsNullOrWhiteSpace(user.KindleEmail);
        var hasEmail = !string.IsNullOrWhiteSpace(user.DeliveryEmail);

        if (!hasKindle && !hasEmail)
        {
            _logger.LogWarning("No delivery channel configured for user {UserId}. Recaps cannot be delivered", userId);
            await _recapRepository.UpdateJobFailedAsync(jobId, "No delivery channel configured.", attemptCount: 0);
            return;
        }

        // Compose EPUB for Kindle channel (if needed)
        byte[]? epubContent = null;
        string? fileName = null;
        if (hasKindle)
        {
            epubContent = EpubComposer.Compose(candidates, scheduledFor, settings.Schedule);
            fileName = $"Relego Recap - {scheduledFor:yyy-MM-dd HH:mm}.epub";
        }

        // Compose HTML for email channel (if needed)
        MimeMessage? htmlMessage = null;
        if (hasEmail)
        {
            try
            {
#pragma warning disable CA2000 // Ownership is transferred to SendHtmlRecapAsync via retry policy
                htmlMessage = HtmlEmailComposer.Compose(
                    candidates, scheduledFor, settings.Schedule,
                    user.DeliveryEmail!, _fromAddress);
#pragma warning restore CA2000
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTML email composition failed for user {UserId}. Skipping email channel", userId);
                hasEmail = false;
            }
        }

        var kindleOk = false;
        var emailOk = false;
        var kindleAttempts = 0;
        var emailAttempts = 0;

        // Deliver via Kindle channel
        if (hasKindle)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(async ct =>
                {
                    kindleAttempts++;
                    await _mailDeliveryService.SendRecapAsync(user.KindleEmail, epubContent!, fileName!, ct);
                }, cancellationToken);
                kindleOk = true;
                _logger.LogInformation("Kindle delivery: {Result}", "Success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kindle delivery failed after {Attempts} attempts for user {UserId}", kindleAttempts, userId);
                kindleOk = false;
            }
        }

        // Deliver via Email channel
        if (hasEmail && htmlMessage is not null)
        {
            var emailRetryPolicy = RecapDeliveryPolicy.Create(_logger);
            try
            {
                await emailRetryPolicy.ExecuteAsync(async ct =>
                {
                    emailAttempts++;
                    await _mailDeliveryService.SendHtmlRecapAsync(htmlMessage, ct);
                }, cancellationToken);
                emailOk = true;
                _logger.LogInformation("Email delivery: {Result}", "Success");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email delivery failed after {Attempts} attempts for user {UserId}", emailAttempts, userId);
                emailOk = false;
            }
        }

        // Outcome determination
        var anySuccess = kindleOk || emailOk;
        var deliveredAt = DateTimeOffset.UtcNow;

        if (anySuccess)
        {
            await _recapRepository.UpdateJobDeliveredAsync(jobId, deliveredAt, kindleAttempts + emailAttempts);

            foreach (var candidate in candidates)
            {
                await _recapRepository.UpdateHighlightSeenAsync(candidate.Id, deliveredAt);
            }

            _logger.LogInformation(
                "Recap delivered to user {UserId}. Kindle: {KindleOk}, Email: {EmailOk}. {Count} highlights updated",
                userId, kindleOk, emailOk, candidates.Count);
        }
        else
        {
            var errorMsg = hasKindle && hasEmail
                ? "Both delivery channels failed."
                : hasKindle
                    ? "Kindle delivery failed."
                    : "Email delivery failed.";
            await _recapRepository.UpdateJobFailedAsync(jobId, errorMsg, kindleAttempts + emailAttempts);
        }
    }
}
