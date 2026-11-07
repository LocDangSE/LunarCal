using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace LunarCal.Infrastructure.Security;

/// <summary>
/// Serilog text formatter that runs the entire formatted text through <see cref="LogRedactor"/> before writing (SEC-LOG-1).
/// </summary>
public sealed class RedactingTextFormatter : ITextFormatter
{
    private readonly ITextFormatter _innerFormatter;
    private readonly bool _redactEmail;

    /// <summary>Initializes a new instance of the <see cref="RedactingTextFormatter"/> class wrapping an existing formatter.</summary>
    public RedactingTextFormatter(ITextFormatter innerFormatter, bool redactEmail = false)
    {
        _innerFormatter = innerFormatter ?? throw new ArgumentNullException(nameof(innerFormatter));
        _redactEmail = redactEmail;
    }

    /// <summary>Initializes a new instance of the <see cref="RedactingTextFormatter"/> class with a message template.</summary>
    public RedactingTextFormatter(string outputTemplate, IFormatProvider? formatProvider = null, bool redactEmail = false)
        : this(new MessageTemplateTextFormatter(outputTemplate, formatProvider), redactEmail)
    {
    }

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = new StringWriter();
        _innerFormatter.Format(logEvent, buffer);
        var formatted = buffer.ToString();
        var redacted = LogRedactor.Redact(formatted, _redactEmail);
        output.Write(redacted);
    }
}
