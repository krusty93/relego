using Polly.Retry;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Resilience;

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

    public RecapService(
        HighlightSelectionService selectionService,
        IMailDeliveryService mailDeliveryService,
        RecapRepository recapRepository,
        UserRepository userRepository,
        SettingsRepository settingsRepository,
        ILogger<RecapService> logger)
    {
        _selectionService = selectionService;
        _mailDeliveryService = mailDeliveryService;
        _recapRepository = recapRepository;
        _userRepository = userRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
        _retryPolicy = RecapDeliveryPolicy.Create(logger);
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
        var hasDeliveryEmail = !string.IsNullOrWhiteSpace(user.DeliveryEmail);

        if (!hasKindle && !hasDeliveryEmail)
        {
            _logger.LogWarning("No delivery channel configured for user {UserId}. Recaps cannot be delivered", userId);
            await _recapRepository.UpdateJobFailedAsync(jobId, "No delivery channel configured.", attemptCount: 0);
            return;
        }

        var emailOk = false;
        var kindleOk = false;
        var emailAttempts = 0;
        var kindleAttempts = 0;
        var cadenceLabel = settings.Schedule.Equals("weekly", StringComparison.OrdinalIgnoreCase) ? "Weekly" : "Daily";

        if (hasKindle)
        {
            byte[] epubContent = EpubComposer.Compose(candidates, scheduledFor, cadenceLabel);
            string fileName = $"Relego Recap - {scheduledFor:yyy-MM-dd HH:mm}.epub";

            try
            {
                await _retryPolicy.ExecuteAsync(async ct =>
                {
                    kindleAttempts++;
                    await _mailDeliveryService.SendRecapAsync(user.KindleEmail, epubContent!, fileName, ct);
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

        if (hasDeliveryEmail)
        {
            (string htmlBody, string plainTextBody) = HtmlEmailComposer.Compose(candidates, scheduledFor);

            try
            {
                await _retryPolicy.ExecuteAsync(async ct =>
                {
                    emailAttempts++;
                    await _mailDeliveryService.SendHtmlRecapAsync(user.DeliveryEmail!, htmlBody, plainTextBody, $"Your Relego {cadenceLabel} Recap", ct);
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
            var errorMsg = hasKindle && hasDeliveryEmail
                ? "Both delivery channels failed."
                : hasKindle
                    ? "Kindle delivery failed."
                    : "Email delivery failed.";
            await _recapRepository.UpdateJobFailedAsync(jobId, errorMsg, kindleAttempts + emailAttempts);

            _logger.LogError(
                "Recap delivery failed for user {UserId}. Kindle: {KindleOk}, Email: {EmailOk}. Error: {ErrorMessage}",
                userId, kindleOk, emailOk, errorMsg);
        }
    }
}
