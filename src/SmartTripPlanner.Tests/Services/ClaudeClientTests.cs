namespace SmartTripPlanner.Tests.Services;

using System.Net;
using System.Text.Json;
using SmartTripPlanner.Api.Exceptions;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;

public class ClaudeClientTests
{
    // --- Static helper tests ---

    [Fact]
    public void ExtractSystemPrompt_WithSystemMessage_ReturnsContent()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "Hello" }
        };

        var result = ClaudeClient.ExtractSystemPrompt(messages);

        Assert.Equal("You are helpful.", result);
    }

    [Fact]
    public void ExtractSystemPrompt_NoSystemMessage_ReturnsNull()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Hello" }
        };

        var result = ClaudeClient.ExtractSystemPrompt(messages);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertTools_MapsToClaudeFormat()
    {
        var tools = new List<LlmTool>
        {
            new()
            {
                Function = new LlmFunction
                {
                    Name = "get_weather",
                    Description = "Get weather for a location",
                    Parameters = new
                    {
                        type = "object",
                        properties = new { location = new { type = "string" } },
                        required = new[] { "location" }
                    }
                }
            }
        };

        var result = ClaudeClient.ConvertTools(tools);

        Assert.Single(result);
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("get_weather", root.GetProperty("name").GetString());
        Assert.Equal("Get weather for a location", root.GetProperty("description").GetString());
        Assert.True(root.TryGetProperty("input_schema", out var schema));
        Assert.Equal("object", schema.GetProperty("type").GetString());
    }

    [Fact]
    public void ConvertMessages_UserOnly_MapsDirectly()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = "System prompt" },
            new() { Role = "user", Content = "Hello" }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result); // system message excluded
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("Hello", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void ConvertMessages_AssistantWithToolCalls_CreatesToolUseBlocks()
    {
        var messages = new List<LlmMessage>
        {
            new()
            {
                Role = "assistant",
                Content = "Let me check.",
                ToolCalls = [new LlmToolCall
                {
                    Id = "toolu_abc",
                    Function = new LlmFunctionCall
                    {
                        Name = "get_weather",
                        Arguments = new Dictionary<string, object> { ["location"] = "Paris" }
                    }
                }]
            }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result);
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("assistant", root.GetProperty("role").GetString());
        var content = root.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_abc", content[1].GetProperty("id").GetString());
        Assert.Equal("get_weather", content[1].GetProperty("name").GetString());
    }

    [Fact]
    public void ConvertMessages_AssistantWithNullContentAndToolCalls_CreatesOnlyToolUseBlock()
    {
        var messages = new List<LlmMessage>
        {
            new()
            {
                Role = "assistant",
                Content = null,
                ToolCalls = [new LlmToolCall
                {
                    Id = "toolu_xyz",
                    Function = new LlmFunctionCall
                    {
                        Name = "search_area",
                        Arguments = new Dictionary<string, object> { ["area"] = "Tokyo" }
                    }
                }]
            }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result);
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength()); // No text block, only tool_use
        Assert.Equal("tool_use", content[0].GetProperty("type").GetString());
    }

    [Fact]
    public void ConvertMessages_ToolResults_MergedIntoUserMessage()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "tool", Content = "{\"temp\":72}", ToolCallId = "toolu_1" },
            new() { Role = "tool", Content = "{\"temp\":65}", ToolCallId = "toolu_2" }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result); // Two tool messages merged into one user message
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("user", root.GetProperty("role").GetString());
        var content = root.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("tool_result", content[0].GetProperty("type").GetString());
        Assert.Equal("toolu_1", content[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_result", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_2", content[1].GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public void ParseResponse_TextOnly_ReturnsChatResponse()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [{ "type": "text", "text": "Hello!" }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        Assert.Equal("assistant", response.Message.Role);
        Assert.Equal("Hello!", response.Message.Content);
        Assert.Null(response.Message.ToolCalls);
        Assert.True(response.Done);
    }

    [Fact]
    public void ParseResponse_ToolUse_ReturnsToolCalls()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "text", "text": "Let me check." },
                { "type": "tool_use", "id": "toolu_abc", "name": "get_weather", "input": { "location": "Paris" } }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 20 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        Assert.Equal("Let me check.", response.Message.Content);
        Assert.NotNull(response.Message.ToolCalls);
        Assert.Single(response.Message.ToolCalls);
        Assert.Equal("toolu_abc", response.Message.ToolCalls[0].Id);
        Assert.Equal("get_weather", response.Message.ToolCalls[0].Function.Name);
        Assert.Equal("Paris", response.Message.ToolCalls[0].Function.Arguments["location"].ToString());
        Assert.True(response.Done); // Claude tool_use responses are still "done" (AgentService checks ToolCalls, not Done)
    }

    [Fact]
    public void ParseResponse_MultipleToolUse_ReturnsAllToolCalls()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "tool_use", "id": "toolu_1", "name": "get_calendar_view", "input": { "start": "2026-03-10", "end": "2026-03-12" } },
                { "type": "tool_use", "id": "toolu_2", "name": "search_area", "input": { "area": "Tokyo" } }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 30 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        Assert.NotNull(response.Message.ToolCalls);
        Assert.Equal(2, response.Message.ToolCalls.Count);
        Assert.Equal("get_calendar_view", response.Message.ToolCalls[0].Function.Name);
        Assert.Equal("search_area", response.Message.ToolCalls[1].Function.Name);
    }

    [Fact]
    public void ParseResponse_NumericInput_ParsesCorrectly()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "tool_use", "id": "toolu_1", "name": "validate_travel", "input": { "from_lat": 35.6762, "from_lon": 139.6503 } }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 20 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        var args = response.Message.ToolCalls![0].Function.Arguments;
        Assert.Equal(35.6762, Convert.ToDouble(args["from_lat"]));
        Assert.Equal(139.6503, Convert.ToDouble(args["from_lon"]));
    }

    // --- HTTP integration tests ---

    [Fact]
    public async Task ChatAsync_ValidResponse_ReturnsParsedResponse()
    {
        var claudeResponse = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [{ "type": "text", "text": "Here is your plan." }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;

        var handler = new FakeHttpHandler(claudeResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-api-key");

        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "Plan a trip" }
        };

        var response = await client.ChatAsync(messages, tools: null);

        Assert.Equal("Here is your plan.", response.Message.Content);
        Assert.True(response.Done);
    }

    [Fact]
    public async Task ChatAsync_SendsRequiredHeaders()
    {
        var claudeResponse = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [{ "type": "text", "text": "OK" }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;

        var handler = new HeaderCapturingHandler(claudeResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "sk-test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };
        await client.ChatAsync(messages, tools: null);

        Assert.Equal("sk-test-key", handler.CapturedHeaders!["x-api-key"]);
        Assert.Equal("2023-06-01", handler.CapturedHeaders["anthropic-version"]);
    }

    [Fact]
    public async Task ChatAsync_ConnectionError_ThrowsLlmUnavailableException()
    {
        var handler = new FaultyHttpHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "test" } };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_Timeout_ThrowsLlmUnavailableException()
    {
        var handler = new FaultyHttpHandler(new TaskCanceledException("Timed out"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "test" } };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_ServerError_ThrowsLlmUnavailableException()
    {
        var handler = new StatusCodeHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "test" } };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }
}

internal class HeaderCapturingHandler(string responseJson) : HttpMessageHandler
{
    public Dictionary<string, string>? CapturedHeaders { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedHeaders = new Dictionary<string, string>();
        foreach (var header in request.Headers)
        {
            CapturedHeaders[header.Key] = string.Join(",", header.Value);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
