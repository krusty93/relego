using System.Runtime.InteropServices;
using System.Text.Json;
using Relego.Server.Telemetry;

namespace Relego.Tests.Telemetry;

/// <summary>
/// Guards the privacy contract: <c>TelemetryPayload</c> is the file <c>PRIVACY.md</c> and
/// <c>README.md</c> point at, so these tests assert the exact set of fields on the wire rather than a
/// subset. Anything added here has to be added to the policy first (ADR-010).
/// </summary>
public sealed class TelemetryPayloadTests
{
    private const string Token = "phc_test_token";

    private static readonly string[] ExpectedEnvelope = ["api_key", "event", "timestamp", "properties"];
    private static readonly string[] ExpectedProperties =
    [
        "distinct_id",
        "server_version",
        "architecture",
        "$process_person_profile",
    ];

    [Fact]
    public void SendsExactlyTheDisclosedFields()
    {
        var payload = new TelemetryPayload("abc123", "0.20.3", "x64");
        var timestamp = new DateTimeOffset(2026, 11, 23, 9, 30, 0, TimeSpan.FromHours(2));

        using var document = JsonDocument.Parse(payload.ToCaptureJson(Token, timestamp));
        var root = document.RootElement;

        Assert.Equal(ExpectedEnvelope, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Token, root.GetProperty("api_key").GetString());
        Assert.Equal("server_heartbeat", root.GetProperty("event").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 11, 23, 7, 30, 0, TimeSpan.Zero),
            root.GetProperty("timestamp").GetDateTimeOffset());

        var properties = root.GetProperty("properties");

        Assert.Equal(ExpectedProperties, properties.EnumerateObject().Select(property => property.Name));
        Assert.Equal("abc123", properties.GetProperty("distinct_id").GetString());
        Assert.Equal("0.20.3", properties.GetProperty("server_version").GetString());
        Assert.Equal("x64", properties.GetProperty("architecture").GetString());
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
    }

    [Fact]
    public void ReportsTheRunningServerVersion()
    {
        var version = TelemetryPayload.CurrentServerVersion;

        // The reported value has to be usable on a dashboard, not the "unknown" fallback.
        Assert.NotEqual("unknown", version);
        Assert.Equal(3, version.Split('.').Length);
    }

    [Fact]
    public void ReportsTheProcessArchitectureInLowerCase()
    {
        var expected = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        Assert.Equal(expected, TelemetryPayload.CurrentArchitecture);
    }

    [Fact]
    public void BuildsPayloadForThisServer()
    {
        var payload = TelemetryPayload.ForThisServer("install-id");

        Assert.Equal("install-id", payload.InstallationId);
        Assert.Equal(TelemetryPayload.CurrentServerVersion, payload.ServerVersion);
        Assert.Equal(TelemetryPayload.CurrentArchitecture, payload.Architecture);
    }
}
