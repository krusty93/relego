using Relego.Server.Telemetry;

namespace Relego.Tests.Telemetry;

/// <summary>
/// Covers the opt-out rules from ADR-010. Telemetry is on by default, which only stays acceptable
/// while every way of turning it off does turn it off: <c>DISABLE_TELEMETRY</c> and the cross-vendor
/// <c>DO_NOT_TRACK</c>, in the spellings operators actually use, and no heartbeat at all when the
/// image carries no project token.
/// </summary>
public sealed class TelemetrySettingsTests
{
    private const string Token = "phc_test_token";

    [Fact]
    public void EnabledByDefaultWhenTokenIsPresent()
    {
        var settings = TelemetrySettings.FromVariables(null, null, Token);

        Assert.True(settings.IsEnabled);
        Assert.Equal(Token, settings.ApiKey);
        Assert.Equal(new Uri("https://eu.i.posthog.com/capture/"), settings.Endpoint);
        Assert.Null(settings.DisabledReason);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData(" on ")]
    public void DisableTelemetryTurnsHeartbeatOff(string value)
    {
        var settings = TelemetrySettings.FromVariables(value, null, Token);

        Assert.False(settings.IsEnabled);
        Assert.NotNull(settings.DisabledReason);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("banana")]
    public void ValuesThatAreNotAffirmativeLeaveTelemetryOn(string value)
    {
        var settings = TelemetrySettings.FromVariables(value, null, Token);

        Assert.True(settings.IsEnabled);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    public void DoNotTrackIsHonouredAsWell(string value)
    {
        var settings = TelemetrySettings.FromVariables(null, value, Token);

        Assert.False(settings.IsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoProjectTokenMeansNoHeartbeat(string? apiKey)
    {
        var settings = TelemetrySettings.FromVariables(null, null, apiKey);

        Assert.False(settings.IsEnabled);
        Assert.Null(settings.ApiKey);
    }

    [Fact]
    public void TokenIsTrimmed()
    {
        var settings = TelemetrySettings.FromVariables(null, null, $"  {Token}\n");

        Assert.Equal(Token, settings.ApiKey);
    }
}
