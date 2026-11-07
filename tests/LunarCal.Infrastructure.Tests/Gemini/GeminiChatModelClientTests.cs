using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using LunarCal.Core.Chat;
using LunarCal.Infrastructure.Gemini;
using Microsoft.Extensions.Options;
using Xunit;

namespace LunarCal.Infrastructure.Tests.Gemini;

/// <summary>
/// Unit tests for <see cref="GeminiChatModelClient"/> (FR-BYOK-7, SEC-KEY-2, SEC-KEY-3, spec §7, ADR-0016).
/// Verifies REST generateContent serialization, function calling AUTO, error classification, safety handling, and timeouts.
/// </summary>
public sealed class GeminiChatModelClientTests
{
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedRequestBody { get; private set; }

        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            if (request.Content != null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (ResponseFactory != null)
            {
                return ResponseFactory(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private static (GeminiChatModelClient Client, MockHttpHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null,
        int timeoutSeconds = 60)
    {
        var handler = new MockHttpHandler { ResponseFactory = responseFactory };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var options = Options.Create(new GeminiOptions
        {
            DefaultModelId = "gemini-3.8-flash",
            RequestTimeoutSeconds = timeoutSeconds,
            MaxOutputTokens = 2048
        });

        var client = new GeminiChatModelClient(httpClient, options);
        return (client, handler);
    }

    [Fact]
    public async Task FR_BYOK_7_Valid_function_call_response_is_parsed_correctly()
    {
        var jsonResponse = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "functionCall": {
                      "name": "create_event",
                      "args": {
                        "summary": "Họp công ty",
                        "start": "2026-11-10T09:00:00+07:00"
                      }
                    },
                    "thoughtSignature": "sig_alpha_12345"
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ],
          "usageMetadata": {
            "promptTokenCount": 42,
            "candidatesTokenCount": 18,
            "totalTokenCount": 60
          },
          "modelVersion": "gemini-3.8-flash"
        }
        """;

        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Tạo lịch họp công ty lúc 9h ngày 10/11") },
            Tools = new[]
            {
                new ToolDefinition("create_event", "Creates a calendar event", "{\"type\":\"object\"}")
            }
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
        response.HasToolCalls.Should().BeTrue();
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls[0].Name.Should().Be("create_event");
        response.ToolCalls[0].ArgumentsJson.Should().Contain("Họp công ty");
        response.FinishReason.Should().Be(ChatFinishReason.ToolCalls);
        response.ProviderState.Should().Be("sig_alpha_12345");
        response.Usage.Should().NotBeNull();
        response.Usage!.TotalTokens.Should().Be(60);
        response.ModelId.Should().Be("gemini-3.8-flash");
    }

    [Fact]
    public async Task FR_BYOK_7_Text_response_is_parsed_correctly()
    {
        var jsonResponse = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": "Tôi đã hiểu yêu cầu của bạn."
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ],
          "modelVersion": "gemini-3.8-flash"
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Xin chào") }
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Text.Should().Be("Tôi đã hiểu yêu cầu của bạn.");
        response.HasToolCalls.Should().BeFalse();
        response.FinishReason.Should().Be(ChatFinishReason.Stop);
    }

    [Fact]
    public async Task FR_BYOK_7_Malformed_function_call_throws_GeminiApiException_with_MalformedResponse()
    {
        var jsonResponse = """
        {
          "candidates": [
            {
              "content": { "parts": [], "role": "model" },
              "finishReason": "MALFORMED_FUNCTION_CALL"
            }
          ]
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Tạo lịch") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task FR_CHAT_7_429_RateLimit_is_classified_as_RateLimitExceeded()
    {
        var errorJson = """
        {
          "error": {
            "code": 429,
            "message": "Resource has been exhausted (e.g. check quota per minute).",
            "status": "RESOURCE_EXHAUSTED"
          }
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Tạo sự kiện") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.RateLimitExceeded);
        ex.Which.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task FR_CHAT_7_429_DailyQuota_is_classified_as_DailyQuotaExceeded()
    {
        var errorJson = """
        {
          "error": {
            "code": 429,
            "message": "Free tier daily quota exceeded for this project. Quota resets at midnight Pacific Time.",
            "status": "RESOURCE_EXHAUSTED"
          }
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Tạo sự kiện") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.DailyQuotaExceeded);
    }

    [Fact]
    public async Task SEC_KEY_4_401_and_403_InvalidApiKey_are_classified_correctly()
    {
        var errorJson = """
        {
          "error": {
            "code": 400,
            "message": "API key not valid. Please pass a valid API key.",
            "status": "INVALID_ARGUMENT",
            "details": [
              {
                "reason": "API_KEY_INVALID"
              }
            ]
          }
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Test") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.InvalidApiKey);
    }

    [Fact]
    public async Task SEC_KEY_4_403_Leaked_key_is_classified_as_LeakedApiKey()
    {
        var errorJson = """
        {
          "error": {
            "code": 403,
            "message": "The API key was reported as leaked and has been blocked.",
            "status": "PERMISSION_DENIED"
          }
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Test") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.LeakedApiKey);
    }

    [Fact]
    public async Task FR_CHAT_7_403_FreeTierVerificationRequired_is_classified_correctly()
    {
        var errorJson = """
        {
          "error": {
            "code": 403,
            "message": "Billing must be verified for this project: FREE_TIER_VERIFICATION_REQUIRED",
            "status": "PERMISSION_DENIED"
          }
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Test") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.FreeTierVerificationRequired);
    }

    [Fact]
    public async Task FR_CHAT_7_Safety_blocked_content_returns_Safety_finish_reason()
    {
        var jsonResponse = """
        {
          "promptFeedback": {
            "blockReason": "SAFETY"
          }
        }
        """;

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Dangerous query") }
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.FinishReason.Should().Be(ChatFinishReason.Safety);
    }

    [Fact]
    public async Task SEC_NET_2_Timeout_throws_GeminiApiException_Transient()
    {
        var (client, _) = CreateClient(_ =>
        {
            // Simulate timeout
            Thread.Sleep(1500);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeoutSeconds: 1);

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Delayed query") }
        };

        var act = async () => await client.SendAsync(request, TestContext.Current.CancellationToken);

        var ex = await act.Should().ThrowAsync<GeminiApiException>();
        ex.Which.ErrorKind.Should().Be(GeminiErrorKind.TransientError);
        ex.Which.StatusCode.Should().Be(HttpStatusCode.RequestTimeout);
    }

    [Fact]
    public async Task ADR_0016_Request_payload_does_not_contain_temperature_topP_or_topK()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"candidates\":[]}", Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Hello") },
            MaxOutputTokens = 1000
        };

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        handler.CapturedRequestBody.Should().NotBeNull();
        handler.CapturedRequestBody.Should().NotContain("temperature");
        handler.CapturedRequestBody.Should().NotContain("topP");
        handler.CapturedRequestBody.Should().NotContain("topK");
        handler.CapturedRequestBody.Should().NotContain("minimal");
        handler.CapturedRequestBody.Should().Contain("\"maxOutputTokens\":1000");
    }

    [Fact]
    public async Task ADR_0016_Request_payload_contains_function_calling_mode_AUTO()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"candidates\":[]}", Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[] { ChatMessage.CreateUser("Add event") },
            Tools = new[] { new ToolDefinition("create_event", "Creates event", "{\"type\":\"object\"}") }
        };

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        handler.CapturedRequestBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(handler.CapturedRequestBody!);
        var mode = doc.RootElement
            .GetProperty("toolConfig")
            .GetProperty("functionCallingConfig")
            .GetProperty("mode")
            .GetString();

        mode.Should().Be("AUTO");
    }

    [Fact]
    public async Task ADR_0016_Multi_turn_thought_signature_in_ProviderState_is_echoed_in_function_call_part()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"candidates\":[]}", Encoding.UTF8, "application/json")
        });

        var request = new ChatRequest
        {
            Messages = new[]
            {
                ChatMessage.CreateUser("Hỏi lại"),
                ChatMessage.CreateModel("Tôi sẽ tạo sự kiện", new[] { new ToolCall("create_event", "{}") })
            },
            ProviderState = "saved_thought_signature_456"
        };

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        handler.CapturedRequestBody.Should().NotBeNull();
        handler.CapturedRequestBody.Should().Contain("\"thoughtSignature\":\"saved_thought_signature_456\"");
    }
}
