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
        if (string.IsNullOrWhiteSpace(user.KindleEmail))
        {
            _logger.LogWarning("No Kindle email configured for user {UserId}. Cannot deliver recap", userId);
            await _recapRepository.UpdateJobFailedAsync(jobId, "No Kindle email configured.", attemptCount: 0);
            return;
        }

        var epubContent = EpubComposer.Compose(candidates, scheduledFor, settings.Schedule);
        var fileName = $"Relego Recap - {scheduledFor:yyy-MM-dd HH:mm}.epub";

        var attemptCount = 0;

        try
        {
            await _retryPolicy.ExecuteAsync(async ct =>
            {
                attemptCount++;
                await _mailDeliveryService.SendRecapAsync(user.KindleEmail, epubContent, fileName, ct);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recap delivery failed after {Attempts} attempts for user {UserId}", attemptCount, userId);
            await _recapRepository.UpdateJobFailedAsync(jobId, $"Delivery failed after {attemptCount} attempts: {ex.Message}", attemptCount);
            return;
        }

        // Delivery confirmed — update recap history
        var deliveredAt = DateTimeOffset.UtcNow;
        await _recapRepository.UpdateJobDeliveredAsync(jobId, deliveredAt, attemptCount);

        foreach (var candidate in candidates)
        {
            await _recapRepository.UpdateHighlightSeenAsync(candidate.Id, deliveredAt);
        }

        _logger.LogInformation(
            "Recap delivered to user {UserId} after {Attempts} attempt(s). {Count} highlights updated",
            userId, attemptCount, candidates.Count);
    }
}
