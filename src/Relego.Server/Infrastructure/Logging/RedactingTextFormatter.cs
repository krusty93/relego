using Serilog.Events;
using Serilog.Formatting;

namespace Relego.Server.Infrastructure.Logging;

/// <summary>
/// Scrubs the formatted log line before it reaches the sink. This is the backstop for values baked
/// into a message template rather than passed as a property, which no enricher can reach.
/// </summary>
/// <param name="inner">The formatter that renders the line.</param>
internal sealed class RedactingTextFormatter(ITextFormatter inner) : ITextFormatter
{
    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        inner.Format(logEvent, buffer);
        output.Write(SensitiveDataRedaction.Scrub(buffer.ToString()));
    }
}
