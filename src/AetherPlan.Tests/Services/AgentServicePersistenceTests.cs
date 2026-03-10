namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using NSubstitute;
using Microsoft.Extensions.Logging;

public class AgentServicePersistenceTests
{
    [Fact]
    public async Task RunAsync_AddTripEventToolCall_PersistsToDatabase()
    {
        var ollamaClient = Substitute.For<IOllamaClient>();
        var calendarService = Substitute.For<ICalendarService>();
        var travelService = Substitute.For<ITravelService>();
        var persistenceService = Substitute.For<IPersistenceService>();
        var logger = Substitute.For<ILogger<AgentService>>();

        var sut = new AgentService(ollamaClient, calendarService, travelService, persistenceService, logger);

        ollamaClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
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
                                Name = "add_trip_event",
                                Arguments = new Dictionary<string, object>
                                {
                                    ["summary"] = "Visit Eiffel Tower",
                                    ["location"] = "Champ de Mars, Paris",
                                    ["start"] = "2026-03-15T10:00:00",
                                    ["end"] = "2026-03-15T12:00:00"
                                }
                            }
                        }]
                    },
                    Done = true
                },
                new LlmChatResponse
                {
                    Message = new LlmMessage { Role = "assistant", Content = "Event added!" },
                    Done = true
                });

        calendarService.CreateEventAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string?>())
            .Returns("cal-event-123");

        persistenceService.CreateTripAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Trip { Id = 1, Destination = "Paris" });

        persistenceService.GetTripsAsync().Returns(new List<Trip>());

        persistenceService.AddTripEventAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string?>())
            .Returns(new TripEvent { Id = 1, Summary = "Visit Eiffel Tower", Location = "Champ de Mars, Paris" });

        var result = await sut.RunAsync("Plan a day in Paris");

        Assert.Equal("Event added!", result);
        await persistenceService.Received(1).AddTripEventAsync(
            Arg.Any<int>(), Arg.Is("Visit Eiffel Tower"), Arg.Is("Champ de Mars, Paris"),
            Arg.Any<double>(), Arg.Any<double>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Is("cal-event-123"));
    }
}
