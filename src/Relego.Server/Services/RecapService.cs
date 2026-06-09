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
        var emailOk = false;
        var kindleOk = false;
        var emailAttempts = 0;
        var kindleAttempts = 0;

        if (hasKindle)
        {
            string? fileName = null;
            byte[]? epubContent = null;
            epubContent = EpubComposer.Compose(candidates, scheduledFor, settings.Schedule);
            fileName = $"Relego Recap - {scheduledFor:yyy-MM-dd HH:mm}.epub";

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

        if (hasEmail)
        {
            try
            {
#pragma warning disable CA2000 // Ownership is transferred to SendHtmlRecapAsync via retry policy
                var htmlMessage = HtmlEmailComposer.Compose(
                    candidates, scheduledFor, settings.Schedule,
                    user.DeliveryEmail!, _fromAddress);
#pragma warning restore CA2000

                try
                {
                    await _retryPolicy.ExecuteAsync(async ct =>
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTML email composition failed for user {UserId}. Skipping email channel", userId);
                hasEmail = false;
            }
        }

        // Outcome determination
        var anySuccess = kindleOk || emailOk;
        var deliveredAt = DateTimeOffset.UtcNow;

        if (anySuccess)
        {
            await _recapRepository.UpdateJobDeliveredAsync(jobId, deliveredAt, kindleAttempts + emailAttempts);

            foreach (SelectionCandidate candidate in candidates)
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
