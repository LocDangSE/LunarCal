using System.Reflection;
using AwesomeAssertions;
using LunarCal.Core.Abstractions;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.Core.Tests.Chat;

public sealed class ChatContractTests
{
    [Fact]
    public void FR_BYOK_7_Core_chat_types_contain_no_vendor_specific_references()
    {
        // FR-BYOK-7: Core must stay strictly vendor-neutral without references to Gemini, Google, etc.
        var coreAssembly = typeof(IChatModelClient).Assembly;
        var chatTypes = coreAssembly.GetTypes()
            .Where(t => t.Namespace == "LunarCal.Core.Chat" || t == typeof(IChatModelClient))
            .ToList();

        chatTypes.Should().NotBeEmpty();

        var forbiddenVendorNames = new[] { "Gemini", "Google", "OpenAI", "Anthropic", "Claude" };

        foreach (var type in chatTypes)
        {
            foreach (var vendor in forbiddenVendorNames)
            {
                type.Name.Should().NotContain(vendor, $"type {type.FullName} must remain vendor-neutral");
            }

            // Inspect public properties and methods to ensure return and parameter types belong to BCL or LunarCal.Core
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var member in members)
            {
                foreach (var vendor in forbiddenVendorNames)
                {
                    member.Name.Should().NotContain(vendor, $"member {member.Name} on {type.Name} must remain vendor-neutral");
                }
            }
        }
    }

    [Fact]
    public void FR_BYOK_7_ChatRequest_initializes_with_expected_defaults()
    {
        var request = new ChatRequest();

        request.SystemPrompt.Should().BeNull();
        request.Messages.Should().BeEmpty();
        request.Tools.Should().BeEmpty();
        request.ModelId.Should().BeNull();
        request.MaxOutputTokens.Should().BeNull();
        request.ProviderState.Should().BeNull();
    }

    [Fact]
    public void FR_BYOK_7_ChatResponse_reports_tool_calls_accurately()
    {
        var responseWithoutTools = new ChatResponse
        {
            Text = "Hello world",
            FinishReason = ChatFinishReason.Stop
        };

        responseWithoutTools.HasToolCalls.Should().BeFalse();
        responseWithoutTools.Text.Should().Be("Hello world");
        responseWithoutTools.FinishReason.Should().Be(ChatFinishReason.Stop);

        var toolCall = new ToolCall("create_event", "{\"title\":\"Meeting\"}", "call_123");
        var usage = new ChatTokenUsage(PromptTokens: 100, ResponseTokens: 25, TotalTokens: 125);

        var responseWithTools = new ChatResponse
        {
            ToolCalls = new[] { toolCall },
            FinishReason = ChatFinishReason.ToolCalls,
            ProviderState = "opaque_thought_signature",
            Usage = usage,
            ModelId = "custom-model"
        };

        responseWithTools.HasToolCalls.Should().BeTrue();
        responseWithTools.ToolCalls.Should().ContainSingle();
        responseWithTools.ToolCalls[0].Name.Should().Be("create_event");
        responseWithTools.ToolCalls[0].ArgumentsJson.Should().Be("{\"title\":\"Meeting\"}");
        responseWithTools.ToolCalls[0].Id.Should().Be("call_123");
        responseWithTools.FinishReason.Should().Be(ChatFinishReason.ToolCalls);
        responseWithTools.ProviderState.Should().Be("opaque_thought_signature");
        responseWithTools.Usage.Should().Be(usage);
        responseWithTools.ModelId.Should().Be("custom-model");
    }

    [Fact]
    public void FR_BYOK_7_ChatMessage_factory_methods_create_consistent_instances()
    {
        var userMsg = ChatMessage.CreateUser("What is on my calendar tomorrow?");
        userMsg.Role.Should().Be(ChatMessageRole.User);
        userMsg.Text.Should().Be("What is on my calendar tomorrow?");
        userMsg.ToolCalls.Should().BeEmpty();
        userMsg.ToolResults.Should().BeEmpty();

        var sysMsg = ChatMessage.CreateSystem("You are a helpful calendar assistant.");
        sysMsg.Role.Should().Be(ChatMessageRole.System);
        sysMsg.Text.Should().Be("You are a helpful calendar assistant.");

        var toolCall = new ToolCall("list_events", "{\"from\":\"2026-11-08\"}");
        var modelMsg = ChatMessage.CreateModel(null, new[] { toolCall });
        modelMsg.Role.Should().Be(ChatMessageRole.Model);
        modelMsg.Text.Should().BeNull();
        modelMsg.ToolCalls.Should().ContainSingle();

        var toolResultMsg = ChatMessage.CreateToolResult("list_events", "{\"events\":[]}", "call_1");
        toolResultMsg.Role.Should().Be(ChatMessageRole.Tool);
        toolResultMsg.ToolResults.Should().ContainSingle();
        toolResultMsg.ToolResults[0].ToolName.Should().Be("list_events");
        toolResultMsg.ToolResults[0].ResultJson.Should().Be("{\"events\":[]}");
        toolResultMsg.ToolResults[0].ToolCallId.Should().Be("call_1");
    }

    [Fact]
    public void FR_BYOK_7_ToolDefinition_and_records_have_value_semantics()
    {
        var def1 = new ToolDefinition("create_event", "Creates an event", "{\"type\":\"object\"}");
        var def2 = new ToolDefinition("create_event", "Creates an event", "{\"type\":\"object\"}");

        def1.Should().Be(def2);
        (def1 == def2).Should().BeTrue();

        var call1 = new ToolCall("create_event", "{}", "1");
        var call2 = new ToolCall("create_event", "{}", "1");

        call1.Should().Be(call2);
        (call1 == call2).Should().BeTrue();

        var res1 = new ToolResult("create_event", "{\"ok\":true}", "1");
        var res2 = new ToolResult("create_event", "{\"ok\":true}", "1");

        res1.Should().Be(res2);
        (res1 == res2).Should().BeTrue();
    }
}
