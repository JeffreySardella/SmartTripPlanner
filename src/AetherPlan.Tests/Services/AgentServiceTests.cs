namespace AetherPlan.Tests.Services;

using System.Text.Json;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using NSubstitute;
using Microsoft.Extensions.Logging;

public class AgentServiceTests
{
    private readonly IOllamaClient _ollamaClient = Substitute.For<IOllamaClient>();
    private readonly ICalendarService _calendarService = Substitute.For<ICalendarService>();
    private readonly ITravelService _travelService = Substitute.For<ITravelService>();
    private readonly AgentService _sut;

    public AgentServiceTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        _sut = new AgentService(_ollamaClient, _calendarService, _travelService, logger);
    }

    [Fact]
    public async Task RunAsync_DirectTextResponse_ReturnsContent()
    {
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .Returns(new OllamaChatResponse
            {
                Message = new OllamaMessage { Role = "assistant", Content = "Here is your plan." },
                Done = true
            });

        var result = await _sut.RunAsync("Plan a trip");

        Assert.Equal("Here is your plan.", result);
    }

    [Fact]
    public async Task RunAsync_ToolCallThenTextResponse_ExecutesToolAndReturns()
    {
        // First call: Ollama wants to call get_calendar_view
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .Returns(
                new OllamaChatResponse
                {
                    Message = new OllamaMessage
                    {
                        Role = "assistant",
                        ToolCalls = [new OllamaToolCall
                        {
                            Function = new OllamaFunctionCall
                            {
                                Name = "get_calendar_view",
                                Arguments = new Dictionary<string, object>
                                {
                                    ["start"] = "2026-03-10T00:00:00",
                                    ["end"] = "2026-03-12T23:59:59"
                                }
                            }
                        }]
                    },
                    Done = true
                },
                new OllamaChatResponse
                {
                    Message = new OllamaMessage { Role = "assistant", Content = "You're free all day!" },
                    Done = true
                });

        _calendarService.GetCalendarViewAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<FreeBusyBlock>
            {
                new() { Start = DateTime.Today, End = DateTime.Today.AddHours(24), IsBusy = false }
            });

        var result = await _sut.RunAsync("Am I free March 10-12?");

        Assert.Equal("You're free all day!", result);
        await _calendarService.Received(1).GetCalendarViewAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task RunAsync_MaxIterationsReached_ReturnsWarning()
    {
        // Always return tool calls, never a text response
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .Returns(new OllamaChatResponse
            {
                Message = new OllamaMessage
                {
                    Role = "assistant",
                    ToolCalls = [new OllamaToolCall
                    {
                        Function = new OllamaFunctionCall
                        {
                            Name = "search_area",
                            Arguments = new Dictionary<string, object> { ["area"] = "Tokyo" }
                        }
                    }]
                },
                Done = true
            });

        var result = await _sut.RunAsync("Plan Tokyo trip", maxIterations: 3);

        Assert.Contains("max iterations", result, StringComparison.OrdinalIgnoreCase);
    }
}
