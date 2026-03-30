namespace SmartTripPlanner.Tests.Services;

using NSubstitute;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using SmartTripPlanner.Api.Tools;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public class AgentServiceConfirmTests
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly ICalendarService _calendar = Substitute.For<ICalendarService>();
    private readonly ITravelService _travel = Substitute.For<ITravelService>();
    private readonly IPersistenceService _persistence = Substitute.For<IPersistenceService>();
    private readonly AgentService _sut;

    public AgentServiceConfirmTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        var env = Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        env.ContentRootPath.Returns(Path.GetTempPath());
        var weather = new WeatherService(Substitute.For<IHttpClientFactory>(), Substitute.For<ILogger<WeatherService>>());
        var poi = new PoiService(Substitute.For<IHttpClientFactory>(), Substitute.For<ILogger<PoiService>>());
        _sut = new AgentService(_llm, _calendar, _travel, _persistence, weather, poi, env, logger);
    }

    [Fact]
    public async Task RunAsync_ConfirmTripTool_ReturnsConfirmationResult()
    {
        var toolCallResponse = new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls =
                [
                    new LlmToolCall
                    {
                        Id = "call_1",
                        Function = new LlmFunctionCall
                        {
                            Name = "confirm_trip",
                            Arguments = new Dictionary<string, object>
                            {
                                ["destination"] = JsonSerializer.SerializeToElement("Tokyo"),
                                ["pace"] = JsonSerializer.SerializeToElement("packed"),
                                ["travelers"] = JsonSerializer.SerializeToElement(2)
                            }
                        }
                    }
                ]
            }
        };

        _llm.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(toolCallResponse);

        var result = await _sut.RunAsync("plan me a trip to Tokyo");

        Assert.Equal(AgentResultType.TripConfirmation, result.Type);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("Tokyo", result.Confirmation!.Destination);
        Assert.Equal("packed", result.Confirmation.Pace);
        Assert.Equal(2, result.Confirmation.Travelers);
    }

    [Fact]
    public async Task RunAsync_SavePreferenceTool_CallsPersistenceService()
    {
        var toolCallResponse = new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls =
                [
                    new LlmToolCall
                    {
                        Id = "call_1",
                        Function = new LlmFunctionCall
                        {
                            Name = "save_preference",
                            Arguments = new Dictionary<string, object>
                            {
                                ["key"] = JsonSerializer.SerializeToElement("pace"),
                                ["value"] = JsonSerializer.SerializeToElement("packed"),
                                ["source"] = JsonSerializer.SerializeToElement("learned")
                            }
                        }
                    }
                ]
            }
        };

        var finalResponse = new LlmChatResponse
        {
            Message = new LlmMessage { Role = "assistant", Content = "Preference saved." }
        };

        _llm.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(toolCallResponse, finalResponse);

        await _sut.RunAsync("I always prefer packed trips");

        await _persistence.Received(1).SavePreferenceAsync("pace", "packed", "learned");
    }
}
