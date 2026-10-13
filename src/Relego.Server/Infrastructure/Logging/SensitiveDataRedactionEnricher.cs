using Serilog.Core;
using Serilog.Events;

namespace Relego.Server.Infrastructure.Logging;

/// <summary>
/// Replaces log property values that name or look like a secret before any sink sees them, so a
/// structured sink cannot leak them either. Non-scalar values are left alone; the file sink's
/// <see cref="RedactingTextFormatter"/> is the backstop that covers them.
/// </summary>
internal sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            if (value is not ScalarValue { Value: string text })
                continue;

            if (SensitiveDataRedaction.IsSensitiveName(name))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SensitiveDataRedaction.Redacted)));
            }
            else if (SensitiveDataRedaction.ContainsSensitiveValue(text))
            {
                // Only the sensitive fragment goes, so the surrounding diagnostic stays readable.
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SensitiveDataRedaction.Scrub(text))));
            }
        }
    }
}
