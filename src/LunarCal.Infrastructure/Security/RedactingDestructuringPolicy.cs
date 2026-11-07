using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Serilog destructuring policy that sanitizes sensitive properties and strings when objects are destructured with '@' (SEC-LOG-1).
/// </summary>
public sealed class RedactingDestructuringPolicy : IDestructuringPolicy
{
    private readonly bool _redactEmail;

    /// <summary>Initializes a new instance of the <see cref="RedactingDestructuringPolicy"/> class.</summary>
    /// <param name="redactEmail">Whether to redact email addresses.</param>
    public RedactingDestructuringPolicy(bool redactEmail = false)
    {
        _redactEmail = redactEmail;
    }

    /// <inheritdoc />
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        if (value is null)
        {
            result = null;
            return false;
        }

        var type = value.GetType();

        // Only destructure user objects, not primitives, enums, strings, decimals or System.* types
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
        {
            result = null;
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        var logProperties = new List<LogEventProperty>();
        foreach (var prop in properties)
        {
            if (LogRedactor.IsSensitivePropertyName(prop.Name))
            {
                logProperties.Add(new LogEventProperty(prop.Name, new ScalarValue(LogRedactor.RedactedEventField)));
                continue;
            }

            object? propVal;
            try
            {
                propVal = prop.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (propVal is string s)
            {
                logProperties.Add(new LogEventProperty(prop.Name, new ScalarValue(LogRedactor.Redact(s, _redactEmail))));
            }
            else
            {
                logProperties.Add(new LogEventProperty(prop.Name, propertyValueFactory.CreatePropertyValue(propVal, destructureObjects: true)));
            }
        }

        result = new StructureValue(logProperties, type.Name);
        return true;
    }
}
