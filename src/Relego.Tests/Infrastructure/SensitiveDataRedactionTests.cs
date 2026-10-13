using System.Globalization;
using Relego.Server.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace Relego.Tests.Infrastructure;

public class SensitiveDataRedactionTests
{
    [Theory]
    [InlineData("token=AbCdEf123456", "[redacted]")]
    [InlineData("token:AbCdEf123456", "[redacted]")]
    [InlineData("pairingToken=AbCdEf123456", "[redacted]")]
    [InlineData("pairing_code=AbCdEf123456", "[redacted]")]
    [InlineData("session-id=123-4567890-1234567", "[redacted]")]
    [InlineData("ubid-acbuk=123-4567890-1234567", "[redacted]")]
    [InlineData("at-acbuk=AbCdEf123456", "[redacted]")]
    [InlineData("Cookie: session-id=123-4567890-1234567", "Cookie: [redacted]")]
    [InlineData("Authorization: Bearer AbCdEf123456", "Authorization: [redacted]")]
    public void Scrub_WhenTheLineCarriesASecret_RedactsIt(string line, string expected)
    {
        Assert.Equal(expected, SensitiveDataRedaction.Scrub(line));
    }

    [Fact]
    public void Scrub_WhenAQueryStringCarriesAPairingToken_KeepsTheRouteAndRedactsTheValue()
    {
        var scrubbed = SensitiveDataRedaction.Scrub("GET /sync/kindle-cloud/pair?token=AbCdEf123456 HTTP/1.1");

        Assert.Equal("GET /sync/kindle-cloud/pair?[redacted] HTTP/1.1", scrubbed);
    }

    [Fact]
    public void Scrub_WhenALineCarriesAPageDump_DropsEverythingFromTheTagOnwards()
    {
        var scrubbed = SensitiveDataRedaction.Scrub(
            "Notebook payload: <!DOCTYPE html><html><body>private highlight text</body></html>");

        Assert.Equal("Notebook payload: [redacted page content]", scrubbed);
        Assert.DoesNotContain("private highlight text", scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sync of 37/120 books for kindle-cloud finished in 1234 ms")]
    [InlineData("Reminder job skipped: no connection configured")]
    [InlineData("Job 4f2a completed with status completed_no_changes")]
    public void Scrub_WhenTheLineIsAnOrdinaryDiagnostic_LeavesItUntouched(string line)
    {
        Assert.Equal(line, SensitiveDataRedaction.Scrub(line));
    }

    [Theory]
    [InlineData("pairingToken", true)]
    [InlineData("PairingCode", true)]
    [InlineData("sessionId", true)]
    [InlineData("Cookie", true)]
    [InlineData("Authorization", true)]
    [InlineData("Password", true)]
    [InlineData("ApiKey", true)]
    [InlineData("FailureCode", false)]
    [InlineData("ProviderId", false)]
    [InlineData("BooksDone", false)]
    [InlineData("RegionId", false)]
    public void IsSensitiveName_ClassifiesPropertyNames(string name, bool expected)
    {
        Assert.Equal(expected, SensitiveDataRedaction.IsSensitiveName(name));
    }

    [Fact]
    public void ContainsSensitiveValue_WhenTheValueIsOrdinary_IsFalse()
    {
        Assert.False(SensitiveDataRedaction.ContainsSensitiveValue("kindle-cloud"));
        Assert.False(SensitiveDataRedaction.ContainsSensitiveValue("120"));
    }

    [Fact]
    public void Enricher_WhenAPropertyIsNamedLikeASecret_ReplacesTheWholeValue()
    {
        var logEvent = CreateLogEvent("Sync progress", ("pairingToken", "AbCdEf123456"), ("booksDone", 37));

        new SensitiveDataRedactionEnricher().Enrich(logEvent, null!);

        Assert.Equal("[redacted]", Scalar(logEvent, "pairingToken"));
        Assert.Equal(37, ((ScalarValue)logEvent.Properties["booksDone"]).Value);
    }

    [Fact]
    public void Enricher_WhenAnOrdinaryPropertyCarriesASecret_RedactsOnlyTheSecret()
    {
        var logEvent = CreateLogEvent("Sync progress", ("requestLine", "GET /pair?token=AbCdEf123456"));

        new SensitiveDataRedactionEnricher().Enrich(logEvent, null!);

        Assert.Equal("GET /pair?[redacted]", Scalar(logEvent, "requestLine"));
    }

    [Fact]
    public void RedactingTextFormatter_WhenTheTemplateBakesInASecret_RedactsTheRenderedLine()
    {
        var formatter = new RedactingTextFormatter(new MessageTemplateTextFormatter(SerilogConfiguration.OutputTemplate));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        formatter.Format(CreateLogEvent("Paired with token=AbCdEf123456"), writer);

        var rendered = writer.ToString();
        Assert.Contains("[INF]", rendered, StringComparison.Ordinal);
        Assert.Contains("Paired with [redacted]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("AbCdEf123456", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactingTextFormatter_WhenTheLineIsOrdinary_WritesItInTheFileSinkShape()
    {
        var formatter = new RedactingTextFormatter(new MessageTemplateTextFormatter(SerilogConfiguration.OutputTemplate));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        formatter.Format(CreateLogEvent("Sync progress"), writer);

        var rendered = writer.ToString();
        Assert.Contains("[INF]", rendered, StringComparison.Ordinal);
        Assert.Contains("Sync progress", rendered, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, rendered, StringComparison.Ordinal);
    }

    private static LogEvent CreateLogEvent(
        string template,
        params (string Name, object Value)[] properties) =>
        new(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse(template),
            properties.Select(property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));

    private static object? Scalar(LogEvent logEvent, string name) =>
        ((ScalarValue)logEvent.Properties[name]).Value;
}
