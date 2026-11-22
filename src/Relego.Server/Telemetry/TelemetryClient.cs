using System.Text;

namespace Relego.Server.Telemetry;

/// <summary>
/// Sends a single heartbeat to the capture endpoint. Failures are swallowed on purpose: telemetry
/// must never fail a boot, block a schedule, or produce log noise on an offline host (ADR-010).
/// </summary>
public sealed class TelemetryClient(HttpClient httpClient)
{
    /// <summary>
    /// Posts one heartbeat.
    /// </summary>
    /// <param name="settings">The resolved configuration, including the project token.</param>
    /// <param name="installationId">The identifier of this install.</param>
    /// <param name="timeProvider">Clock used for the event timestamp.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when the endpoint accepted the event.</returns>
    public async Task<bool> SendAsync(
        TelemetrySettings settings,
        string installationId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsEnabled || settings.ApiKey is null)
            return false;

        var payload = TelemetryPayload.ForThisServer(installationId);
        var body = payload.ToCaptureJson(settings.ApiKey, timeProvider.GetUtcNow());

        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await httpClient
                .PostAsync(settings.Endpoint, content, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
