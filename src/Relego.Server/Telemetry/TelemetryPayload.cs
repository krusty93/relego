using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Relego.Server.Telemetry;

/// <summary>
/// The complete set of data the server reports once a day. This type is the authoritative list of
/// what leaves the container, referenced by <c>PRIVACY.md</c> and <c>README.md</c>: adding a value is
/// a privacy-policy change (ADR-010), not a feature addition.
/// </summary>
/// <param name="InstallationId">Random identifier stored in the data volume; it names one install and nothing else.</param>
/// <param name="ServerVersion">The Relego server version currently running.</param>
/// <param name="Architecture">The CPU architecture the server runs on, for example <c>x64</c>.</param>
public sealed record TelemetryPayload(string InstallationId, string ServerVersion, string Architecture)
{
    /// <summary>The event name the dashboards group on.</summary>
    public const string EventName = "server_heartbeat";

    /// <summary>
    /// Builds the payload for the running server. The version comes from the assembly the server was
    /// built as, so it reports what is actually running rather than a value kept in sync by hand.
    /// </summary>
    /// <param name="installationId">Random identifier of this install.</param>
    /// <returns>The payload describing this server.</returns>
    public static TelemetryPayload ForThisServer(string installationId) =>
        new(installationId, CurrentServerVersion, CurrentArchitecture);

    /// <summary>The server version reported in the heartbeat, for example <c>0.20.3</c>.</summary>
    public static string CurrentServerVersion =>
        typeof(TelemetryPayload).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>The CPU architecture reported in the heartbeat, for example <c>x64</c> or <c>arm64</c>.</summary>
    public static string CurrentArchitecture =>
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>
    /// Serialises the capture request body: the three values above plus only the envelope the vendor
    /// requires (project token, event name, distinct id, timestamp) and the flag that keeps the event
    /// anonymous.
    /// </summary>
    /// <param name="apiKey">The PostHog project token that receives the event.</param>
    /// <param name="timestamp">When the heartbeat was produced.</param>
    /// <returns>The JSON body to POST to the capture endpoint.</returns>
    public string ToCaptureJson(string apiKey, DateTimeOffset timestamp)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["distinct_id"] = InstallationId,
            ["server_version"] = ServerVersion,
            ["architecture"] = Architecture,
            // No person profile is created, so there is nothing to enrich, export, or delete later.
            ["$process_person_profile"] = false,
        };

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["api_key"] = apiKey,
            ["event"] = EventName,
            ["timestamp"] = timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["properties"] = properties,
        };

        return JsonSerializer.Serialize(body);
    }
}
