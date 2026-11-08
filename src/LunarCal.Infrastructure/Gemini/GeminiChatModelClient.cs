using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using LunarCal.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LunarCal.Infrastructure.Gemini;

/// <summary>
/// Implements <see cref="IChatModelClient"/> using the Google Gemini REST generateContent API with AUTO function calling (spec §7, ADR-0016).
/// Enforces key security, 60-second timeouts, error categorization, and vendor-neutral domain isolation.
/// </summary>
public sealed class GeminiChatModelClient : IChatModelClient
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore? _settingsStore;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiChatModelClient>? _logger;

    public GeminiChatModelClient(
        HttpClient httpClient,
        IOptions<GeminiOptions>? options = null,
        ILogger<GeminiChatModelClient>? logger = null,
        ISettingsStore? settingsStore = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? new GeminiOptions();
        _logger = logger;
        _settingsStore = settingsStore;
    }

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? configuredModel = null;
        if (string.IsNullOrWhiteSpace(request.ModelId) && _settingsStore != null)
        {
            configuredModel = await _settingsStore.GetStringAsync(SettingKeys.GeminiModelId, cancellationToken).ConfigureAwait(false);
        }

        var modelId = !string.IsNullOrWhiteSpace(request.ModelId)
            ? request.ModelId
            : !string.IsNullOrWhiteSpace(configuredModel)
                ? configuredModel
                : _options.DefaultModelId;

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("No Gemini model specified and no DefaultModelId configured (FR-BYOK-5, ADR-0016).");
        }

        var endpoint = BuildEndpointUri(modelId);
        var payloadBytes = BuildRequestPayload(request);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.RequestTimeoutSeconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(payloadBytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            }
        };

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new GeminiApiException(
                GeminiErrorKind.TransientError,
                $"Gemini API request timed out after {_options.RequestTimeoutSeconds} seconds.",
                HttpStatusCode.RequestTimeout);
        }

        using (httpResponse)
        {
            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var (kind, desc, status, msg) = GeminiErrorClassifier.Classify(httpResponse.StatusCode, responseBody);
                throw new GeminiApiException(
                    kind,
                    desc,
                    httpResponse.StatusCode,
                    status,
                    msg);
            }

            return ParseResponse(responseBody, modelId);
        }
    }

    private Uri BuildEndpointUri(string modelId)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/') + "/";
        return new Uri($"{baseUrl}v1beta/models/{Uri.EscapeDataString(modelId)}:generateContent");
    }

    private byte[] BuildRequestPayload(ChatRequest request)
    {
        var root = new JsonObject();

        // 1. contents
        var contentsArray = new JsonArray();
        foreach (var message in request.Messages)
        {
            var contentObj = new JsonObject();
            var partsArray = new JsonArray();

            switch (message.Role)
            {
                case ChatMessageRole.User:
                    contentObj["role"] = "user";
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        partsArray.Add(new JsonObject { ["text"] = message.Text });
                    }
                    foreach (var tr in message.ToolResults)
                    {
                        var responseNode = JsonNode.Parse(tr.ResultJson) ?? new JsonObject();
                        partsArray.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = tr.ToolName,
                                ["response"] = responseNode
                            }
                        });
                    }
                    break;

                case ChatMessageRole.Model:
                    contentObj["role"] = "model";
                    if (!string.IsNullOrEmpty(message.Text))
                    {
                        partsArray.Add(new JsonObject { ["text"] = message.Text });
                    }
                    foreach (var tc in message.ToolCalls)
                    {
                        var argsNode = JsonNode.Parse(tc.ArgumentsJson) ?? new JsonObject();
                        var fcPart = new JsonObject
                        {
                            ["functionCall"] = new JsonObject
                            {
                                ["name"] = tc.Name,
                                ["args"] = argsNode
                            }
                        };

                        if (!string.IsNullOrWhiteSpace(request.ProviderState))
                        {
                            fcPart["thoughtSignature"] = request.ProviderState;
                        }

                        partsArray.Add(fcPart);
                    }
                    break;

                case ChatMessageRole.Tool:
                    // Gemini v1beta represents tool responses as role "user" with functionResponse part
                    contentObj["role"] = "user";
                    foreach (var tr in message.ToolResults)
                    {
                        var responseNode = JsonNode.Parse(tr.ResultJson) ?? new JsonObject();
                        partsArray.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = tr.ToolName,
                                ["response"] = responseNode
                            }
                        });
                    }
                    break;

                case ChatMessageRole.System:
                    // Handled under systemInstruction
                    break;
            }

            if (partsArray.Count > 0)
            {
                contentObj["parts"] = partsArray;
                contentsArray.Add(contentObj);
            }
        }

        root["contents"] = contentsArray;

        // 2. systemInstruction
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            root["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = request.SystemPrompt }
                }
            };
        }

        // 3. tools & toolConfig
        if (request.Tools.Count > 0)
        {
            var declarations = new JsonArray();
            foreach (var tool in request.Tools)
            {
                var paramsNode = JsonNode.Parse(tool.ParametersSchemaJson) ?? new JsonObject();
                declarations.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = paramsNode
                });
            }

            root["tools"] = new JsonArray
            {
                new JsonObject { ["functionDeclarations"] = declarations }
            };

            root["toolConfig"] = new JsonObject
            {
                ["functionCallingConfig"] = new JsonObject
                {
                    ["mode"] = "AUTO"
                }
            };
        }

        // 4. generationConfig (maxOutputTokens only; do NOT send temperature, topP, topK per ADR-0016)
        var maxTokens = request.MaxOutputTokens ?? _options.MaxOutputTokens;
        if (maxTokens > 0)
        {
            root["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = maxTokens
            };
        }

        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static ChatResponse ParseResponse(string responseBody, string fallbackModelId)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // 1. Prompt feedback / Safety block
        if (root.TryGetProperty("promptFeedback", out var feedbackElement))
        {
            if (feedbackElement.TryGetProperty("blockReason", out var blockReasonProp))
            {
                var blockReason = blockReasonProp.GetString();
                if (!string.IsNullOrEmpty(blockReason))
                {
                    return new ChatResponse
                    {
                        FinishReason = ChatFinishReason.Safety,
                        ModelId = fallbackModelId
                    };
                }
            }
        }

        // 2. Candidates
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return new ChatResponse
            {
                FinishReason = ChatFinishReason.Stop,
                ModelId = fallbackModelId
            };
        }

        var candidate = candidates[0];

        // 3. FinishReason
        var finishReason = ChatFinishReason.Stop;
        if (candidate.TryGetProperty("finishReason", out var finishProp))
        {
            var finishStr = finishProp.GetString()?.ToUpperInvariant();
            finishReason = finishStr switch
            {
                "STOP" => ChatFinishReason.Stop,
                "MAX_TOKENS" => ChatFinishReason.Length,
                "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" => ChatFinishReason.Safety,
                "MALFORMED_FUNCTION_CALL" => throw new GeminiApiException(
                    GeminiErrorKind.MalformedResponse,
                    "Gemini API returned finishReason: MALFORMED_FUNCTION_CALL."),
                _ => ChatFinishReason.Other
            };
        }

        // 4. Parts (text, functionCall, thoughtSignature)
        var textBuilder = new StringBuilder();
        var toolCalls = new List<ToolCall>();
        string? providerState = null;

        if (candidate.TryGetProperty("content", out var contentElement) &&
            contentElement.TryGetProperty("parts", out var partsElement))
        {
            foreach (var part in partsElement.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        textBuilder.Append(text);
                    }
                }

                if (part.TryGetProperty("functionCall", out var fcElement))
                {
                    var name = fcElement.GetProperty("name").GetString() ?? string.Empty;
                    var args = fcElement.TryGetProperty("args", out var argsProp)
                        ? argsProp.GetRawText()
                        : "{}";

                    toolCalls.Add(new ToolCall(name, args));
                }

                if (part.TryGetProperty("thoughtSignature", out var sigProp))
                {
                    providerState = sigProp.GetString();
                }
            }
        }

        if (toolCalls.Count > 0 && finishReason == ChatFinishReason.Stop)
        {
            finishReason = ChatFinishReason.ToolCalls;
        }

        // 5. Usage metadata
        ChatTokenUsage? usage = null;
        if (root.TryGetProperty("usageMetadata", out var usageProp))
        {
            int prompt = usageProp.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
            int candidatesTokens = usageProp.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
            int total = usageProp.TryGetProperty("totalTokenCount", out var t) ? t.GetInt32() : prompt + candidatesTokens;
            usage = new ChatTokenUsage(prompt, candidatesTokens, total);
        }

        // 6. Model version
        var modelId = root.TryGetProperty("modelVersion", out var mv)
            ? mv.GetString() ?? fallbackModelId
            : fallbackModelId;

        return new ChatResponse
        {
            Text = textBuilder.Length > 0 ? textBuilder.ToString() : null,
            ToolCalls = toolCalls,
            FinishReason = finishReason,
            ProviderState = providerState,
            Usage = usage,
            ModelId = modelId
        };
    }
}
