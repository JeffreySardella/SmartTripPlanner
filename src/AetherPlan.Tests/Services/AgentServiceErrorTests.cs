namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Microsoft.Extensions.Logging;

public class AgentServiceErrorTests
{
    private readonly IOllamaClient _ollamaClient = Substitute.For<IOllamaClient>();
    private readonly ICalendarService _calendarService = Substitute.For<ICalendarService>();
    private readonly ITravelService _travelService = Substitute.For<ITravelService>();
    private readonly IPersistenceService _persistenceService = Substitute.For<IPersistenceService>();
    private readonly AgentService _sut;

    public AgentServiceErrorTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        _sut = new AgentService(_ollamaClient, _calendarService, _travelService, _persistenceService, logger);
    }

    [Fact]
    public async Task RunAsync_ToolThrows_FeedsErrorBackToOllama()
    {
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
                    Message = new OllamaMessage { Role = "assistant", Content = "Sorry, calendar is unavailable." },
                    Done = true
                });

        _calendarService.GetCalendarViewAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .ThrowsAsync(new InvalidOperationException("Google Calendar API not configured"));

        var result = await _sut.RunAsync("Check my calendar");

        Assert.Equal("Sorry, calendar is unavailable.", result);
    }

    [Fact]
    public async Task RunAsync_OllamaUnavailable_ReturnsUserFriendlyMessage()
    {
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .ThrowsAsync(new OllamaUnavailableException("Cannot connect to Ollama"));

        var result = await _sut.RunAsync("Plan a trip");

        Assert.Contains("Ollama", result);
        Assert.Contains("unavailable", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_BadToolArguments_FeedsParseErrorToOllama()
    {
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
                                    ["start"] = "not-a-date",
                                    ["end"] = "also-not-a-date"
                                }
                            }
                        }]
                    },
                    Done = true
                },
                new OllamaChatResponse
                {
                    Message = new OllamaMessage { Role = "assistant", Content = "Let me try valid dates." },
                    Done = true
                });

        var result = await _sut.RunAsync("Check calendar");

        Assert.Equal("Let me try valid dates.", result);
    }
}
