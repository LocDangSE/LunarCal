using System.Net;
using LunarCal.Infrastructure.Security;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Exception thrown by <see cref="GeminiChatModelClient"/> on API, authentication, safety, or protocol errors (ADR-0016 §4).
/// Ensures credentials and secrets are always redacted from exception messages (SEC-KEY-3).
/// </summary>
public sealed class GeminiApiException : Exception
{
    public GeminiErrorKind ErrorKind { get; }
    public HttpStatusCode? StatusCode { get; }
    public string? GeminiStatus { get; }
    public string? GeminiRawMessage { get; }

    public GeminiApiException(
        GeminiErrorKind errorKind,
        string message,
        HttpStatusCode? statusCode = null,
        string? geminiStatus = null,
        string? geminiRawMessage = null,
        Exception? innerException = null)
        : base(LogRedactor.Redact(message), innerException)
    {
        ErrorKind = errorKind;
        StatusCode = statusCode;
        GeminiStatus = geminiStatus;
        GeminiRawMessage = LogRedactor.Redact(geminiRawMessage);
    }
}
