using Serilog.Core;
using Serilog.Events;

namespace Relego.Server.Infrastructure.Logging;

/// <summary>
/// Replaces log property values that name or look like a secret before any sink sees them, so a
/// structured sink cannot leak them either. A property whose name marks it as a secret is replaced
/// whatever its Serilog type; only the value-shaped rule needs a string to inspect.
/// </summary>
internal sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            // The name is checked first and on its own: a sequence or structure called `Credentials`
            // can carry the secret in an element that no label-based rule would recognise.
            if (SensitiveDataRedaction.IsSensitiveName(name))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SensitiveDataRedaction.Redacted)));
            }
            else if (value is ScalarValue { Value: string text } && SensitiveDataRedaction.ContainsSensitiveValue(text))
            {
                // Only the sensitive fragment goes, so the surrounding diagnostic stays readable.
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SensitiveDataRedaction.Scrub(text))));
            }
        }
    }
}
