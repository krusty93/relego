namespace Relego.Server.Telemetry;

/// <summary>
/// Resolved telemetry configuration. Telemetry is on by default (ADR-010) and stops as soon as the
/// user opts out with an environment variable.
/// </summary>
/// <param name="IsEnabled">Whether heartbeats are sent at all.</param>
/// <param name="ApiKey">The PostHog project token that receives the events.</param>
/// <param name="Endpoint">The capture endpoint the token belongs to.</param>
/// <param name="DisabledReason">Why telemetry is off, for a single start-up log line.</param>
public sealed record TelemetrySettings(bool IsEnabled, string? ApiKey, Uri Endpoint, string? DisabledReason = null)
{
    /// <summary>Set to <c>true</c> (or <c>1</c>) to stop the daily heartbeat.</summary>
    public const string DisableVariable = "DISABLE_TELEMETRY";

    /// <summary>The cross-vendor opt-out convention, honoured alongside <see cref="DisableVariable"/>.</summary>
    public const string DoNotTrackVariable = "DO_NOT_TRACK";

    /// <summary>
    /// The PostHog project token that receives the events. It is committed rather than injected because
    /// a project token can only write events, never read them, so PostHog documents it as safe to
    /// publish alongside a client. Replace the placeholder with the project's token before release.
    /// </summary>
    // codeql[cs/hard-coded-credentials] A project token is public by design; it can only ingest events.
    public const string ProjectToken = "phc_sxk5fSdeo2uHdPk45x7mbkZnctQREtYZQYhUGFU29E6U";

    /// <summary>The EU capture endpoint; the US instance is never contacted.</summary>
    public static Uri DefaultEndpoint { get; } = new("https://eu.i.posthog.com/capture/");

    /// <summary>
    /// Reads the settings from the environment. Absent or unrecognised values leave telemetry on, which
    /// keeps the default-on model from being disabled by accident.
    /// </summary>
    /// <returns>The resolved settings.</returns>
    public static TelemetrySettings FromEnvironment() => FromVariables(
        Environment.GetEnvironmentVariable(DisableVariable),
        Environment.GetEnvironmentVariable(DoNotTrackVariable),
        ProjectToken);

    /// <summary>
    /// Resolves the settings from raw values, so the opt-out rules can be exercised without touching the
    /// process environment.
    /// </summary>
    /// <param name="disableTelemetry">The <see cref="DisableVariable"/> value.</param>
    /// <param name="doNotTrack">The <see cref="DoNotTrackVariable"/> value.</param>
    /// <param name="apiKey">The project token to send with.</param>
    /// <returns>The resolved settings.</returns>
    public static TelemetrySettings FromVariables(string? disableTelemetry, string? doNotTrack, string? apiKey)
    {
        var optedOut = IsTruthy(disableTelemetry) || IsTruthy(doNotTrack);
        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        if (optedOut)
            return new TelemetrySettings(false, key, DefaultEndpoint, $"{DisableVariable} or {DoNotTrackVariable} is set");

        // A token is compiled in, so this only guards a build where the placeholder was blanked out.
        return key is null
            ? new TelemetrySettings(false, null, DefaultEndpoint, "no project token is compiled in")
            : new TelemetrySettings(true, key, DefaultEndpoint);
    }

    private static bool IsTruthy(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
