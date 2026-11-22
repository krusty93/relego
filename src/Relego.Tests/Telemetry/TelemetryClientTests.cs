using System.Net;
using System.Text.Json;
using Relego.Server.Telemetry;
using RichardSzalay.MockHttp;

namespace Relego.Tests.Telemetry;

/// <summary>
/// Covers the transport side of ADR-010: one POST to the EU capture endpoint, no request at all when
/// telemetry is off, and silence instead of an exception when the endpoint is unreachable. A failed
/// heartbeat must never surface to the user.
/// </summary>
public sealed class TelemetryClientTests
{
    private const string Token = "phc_test_token";

    private readonly MockHttpMessageHandler _mockHttp = new();

    private static readonly TelemetrySettings Enabled = new(true, Token, TelemetrySettings.DefaultEndpoint);
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostsTheHeartbeatToTheEuEndpoint()
    {
        string? body = null;
        _mockHttp
            .Expect(HttpMethod.Post, "https://eu.i.posthog.com/capture/")
            .With(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK);

        var sent = await new TelemetryClient(_mockHttp.ToHttpClient()).SendAsync(Enabled, "install-1", new FixedTimeProvider(Now));

        Assert.True(sent);
        _mockHttp.VerifyNoOutstandingExpectation();

        using var document = JsonDocument.Parse(body!);
        var properties = document.RootElement.GetProperty("properties");

        Assert.Equal("install-1", properties.GetProperty("distinct_id").GetString());
        Assert.Equal(TelemetryPayload.CurrentServerVersion, properties.GetProperty("server_version").GetString());
        Assert.Equal(TelemetryPayload.CurrentArchitecture, properties.GetProperty("architecture").GetString());
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
    }

    [Fact]
    public async Task SendsNothingWhenTelemetryIsDisabled()
    {
        var disabled = new TelemetrySettings(false, Token, TelemetrySettings.DefaultEndpoint, "test");
        var calls = 0;
        _mockHttp
            .When(HttpMethod.Post, "https://eu.i.posthog.com/capture/")
            .With(_ =>
            {
                calls++;
                return true;
            })
            .Respond(HttpStatusCode.OK);

        var sent = await new TelemetryClient(_mockHttp.ToHttpClient()).SendAsync(disabled, "install-1", new FixedTimeProvider(Now));

        Assert.False(sent);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ReportsAFailedSendWithoutThrowing(HttpStatusCode statusCode)
    {
        _mockHttp
            .When(HttpMethod.Post, "https://eu.i.posthog.com/capture/")
            .Respond(statusCode);

        var sent = await new TelemetryClient(_mockHttp.ToHttpClient()).SendAsync(Enabled, "install-1", new FixedTimeProvider(Now));

        Assert.False(sent);
    }

    [Fact]
    public async Task SwallowsAnUnreachableEndpoint()
    {
        _mockHttp
            .When(HttpMethod.Post, "https://eu.i.posthog.com/capture/")
            .Throw(new HttpRequestException("offline"));

        var sent = await new TelemetryClient(_mockHttp.ToHttpClient()).SendAsync(Enabled, "install-1", new FixedTimeProvider(Now));

        Assert.False(sent);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
