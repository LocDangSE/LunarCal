using Serilog.Core;
using Serilog.Events;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Serilog enricher that redacts sensitive properties and string values on every LogEvent (SEC-LOG-1, SEC-LOG-2).
/// </summary>
public sealed class RedactingEnricher : ILogEventEnricher
{
    private readonly bool _redactEmail;

    /// <summary>Initializes a new instance of the <see cref="RedactingEnricher"/> class.</summary>
    /// <param name="redactEmail">Whether to redact email addresses.</param>
    public RedactingEnricher(bool redactEmail = false)
    {
        _redactEmail = redactEmail;
    }

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var propertyKeys = logEvent.Properties.Keys.ToList();
        foreach (var key in propertyKeys)
        {
            var value = logEvent.Properties[key];
            if (LogRedactor.IsSensitivePropertyName(key))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue(LogRedactor.RedactedEventField)));
            }
            else
            {
                var redacted = RedactPropertyValue(value);
                if (!ReferenceEquals(redacted, value))
                {
                    logEvent.AddOrUpdateProperty(new LogEventProperty(key, redacted));
                }
            }
        }
    }

    private LogEventPropertyValue RedactPropertyValue(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar when scalar.Value is string str =>
                new ScalarValue(LogRedactor.Redact(str, _redactEmail)),

            StructureValue structure =>
                new StructureValue(
                    structure.Properties.Select(p =>
                        LogRedactor.IsSensitivePropertyName(p.Name)
                            ? new LogEventProperty(p.Name, new ScalarValue(LogRedactor.RedactedEventField))
                            : new LogEventProperty(p.Name, RedactPropertyValue(p.Value))),
                    structure.TypeTag),

            SequenceValue sequence =>
                new SequenceValue(sequence.Elements.Select(RedactPropertyValue)),

            DictionaryValue dictionary =>
                new DictionaryValue(dictionary.Elements.Select(kvp =>
                    new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        kvp.Key,
                        kvp.Key.Value is string k && LogRedactor.IsSensitivePropertyName(k)
                            ? new ScalarValue(LogRedactor.RedactedEventField)
                            : RedactPropertyValue(kvp.Value)))),

            _ => value
        };
    }
}
