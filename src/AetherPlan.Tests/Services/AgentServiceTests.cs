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
    private readonly IPersistenceService _persistenceService = Substitute.For<IPersistenceService>();
    private readonly AgentService _sut;

    public AgentServiceTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        _sut = new AgentService(_ollamaClient, _calendarService, _travelService, _persistenceService, logger);
    }

    [Fact]
    public async Task RunAsync_DirectTextResponse_ReturnsContent()
    {
        _ollamaClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(new LlmChatResponse
            {
                Message = new LlmMessage { Role = "assistant", Content = "Here is your plan." },
                Done = true
            });

        var result = await _sut.RunAsync("Plan a trip");

        Assert.Equal("Here is your plan.", result);
    }

    [Fact]
    public async Task RunAsync_ToolCallThenTextResponse_ExecutesToolAndReturns()
    {
        // First call: Ollama wants to call get_calendar_view
        _ollamaClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(
                new LlmChatResponse
                {
                    Message = new LlmMessage
                    {
                        Role = "assistant",
                        ToolCalls = [new LlmToolCall
                        {
                            Function = new LlmFunctionCall
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
                new LlmChatResponse
                {
                    Message = new LlmMessage { Role = "assistant", Content = "You're free all day!" },
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
        _ollamaClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(new LlmChatResponse
            {
                Message = new LlmMessage
                {
                    Role = "assistant",
                    ToolCalls = [new LlmToolCall
                    {
                        Function = new LlmFunctionCall
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

    [Fact]
    public async Task RunAsync_SearchAreaWithCachedResults_ReturnsCachedLocations()
    {
        var cachedLocations = new List<CachedLocation>
        {
            new() { Id = 1, Name = "Tokyo Tower", Latitude = 35.6586, Longitude = 139.7454, Category = "attractions" }
        };

        _persistenceService.SearchCachedLocationsAsync("Tokyo", null)
            .Returns(cachedLocations);

        // First call: Ollama calls search_area
        // Second call: Ollama returns text with the results
        _ollamaClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(
                new LlmChatResponse
                {
                    Message = new LlmMessage
                    {
                        Role = "assistant",
                        ToolCalls = [new LlmToolCall
                        {
                            Function = new LlmFunctionCall
                            {
                                Name = "search_area",
                                Arguments = new Dictionary<string, object> { ["area"] = "Tokyo" }
                            }
                        }]
                    },
                    Done = true
                },
                new LlmChatResponse
                {
                    Message = new LlmMessage { Role = "assistant", Content = "Found Tokyo Tower!" },
                    Done = true
                });

        var result = await _sut.RunAsync("What's in Tokyo?");

        Assert.Equal("Found Tokyo Tower!", result);
        await _persistenceService.Received(1).SearchCachedLocationsAsync("Tokyo", null);
    }
}
