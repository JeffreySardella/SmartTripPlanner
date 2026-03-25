namespace SmartTripPlanner.Tests.Services;

using System.Net;
using System.Text.Json;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;

public class OllamaClientTests
{
    [Fact]
    public async Task ChatAsync_WithToolResponse_ReturnsToolCalls()
    {
        var expectedResponse = new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls = [new LlmToolCall
                {
                    Function = new LlmFunctionCall
                    {
                        Name = "get_calendar_view",
                        Arguments = new Dictionary<string, object>
                        {
                            ["start"] = "2026-03-10",
                            ["end"] = "2026-03-12"
                        }
                    }
                }]
            },
            Done = true
        };

        var handler = new FakeHttpHandler(JsonSerializer.Serialize(expectedResponse));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "qwen3.5:35b-a3b-q4_K_M");

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Plan a trip to Tokyo March 10-12" }
        };

        var response = await client.ChatAsync(messages, tools: null);

        Assert.NotNull(response.Message.ToolCalls);
        Assert.Single(response.Message.ToolCalls);
        Assert.Equal("get_calendar_view", response.Message.ToolCalls[0].Function.Name);
    }

    [Fact]
    public async Task ChatAsync_WithTextResponse_ReturnsContent()
    {
        var expectedResponse = new LlmChatResponse
        {
            Message = new LlmMessage { Role = "assistant", Content = "Here is your itinerary." },
            Done = true
        };

        var handler = new FakeHttpHandler(JsonSerializer.Serialize(expectedResponse));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "qwen3.5:35b-a3b-q4_K_M");

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Summarize the plan" }
        };

        var response = await client.ChatAsync(messages, tools: null);

        Assert.NotNull(response.Message.Content);
        Assert.Null(response.Message.ToolCalls);
    }
}

internal class FakeHttpHandler(string responseJson) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
