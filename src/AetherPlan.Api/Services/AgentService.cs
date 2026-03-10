namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;
using AetherPlan.Api.Tools;

public class AgentService(
    IOllamaClient ollamaClient,
    ICalendarService calendarService,
    ITravelService travelService,
    IPersistenceService persistenceService,
    ILogger<AgentService> logger) : IAgentService
{
    private const string SystemPrompt =
        "You are a professional travel logistician. Maximize sightseeing while " +
        "minimizing travel fatigue. You have access to the user's Google Calendar. " +
        "When a trip is requested, check for free slots, research the area, and " +
        "only commit events once you've verified travel times between locations.";

    public async Task<string> RunAsync(string userRequest, int maxIterations = 10)
    {
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = SystemPrompt },
            new() { Role = "user", Content = userRequest }
        };

        var tools = ToolDefinitions.GetAllTools();

        for (var i = 0; i < maxIterations; i++)
        {
            logger.LogInformation("Agent iteration {Iteration}", i + 1);

            OllamaChatResponse response;
            try
            {
                response = await ollamaClient.ChatAsync(messages, tools);
            }
            catch (OllamaUnavailableException ex)
            {
                logger.LogError(ex, "Ollama is unavailable");
                return $"Ollama is unavailable: {ex.Message}";
            }

            var message = response.Message;

            // If no tool calls, we have a final text response
            if (message.ToolCalls is null || message.ToolCalls.Count == 0)
            {
                return message.Content ?? string.Empty;
            }

            // Add assistant message with tool calls to history
            messages.Add(message);

            // Execute each tool call
            foreach (var toolCall in message.ToolCalls)
            {
                logger.LogInformation("Executing tool: {ToolName}", toolCall.Function.Name);

                object result;
                try
                {
                    result = await ExecuteToolAsync(toolCall);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Tool {ToolName} failed", toolCall.Function.Name);
                    result = new { error = $"{toolCall.Function.Name} failed: {ex.Message}" };
                }

                messages.Add(new OllamaMessage
                {
                    Role = "tool",
                    Content = JsonSerializer.Serialize(result)
                });
            }
        }

        return "Agent reached max iterations without completing. Please try a more specific request.";
    }

    private async Task<object> ExecuteToolAsync(OllamaToolCall toolCall)
    {
        var args = toolCall.Function.Arguments;

        return toolCall.Function.Name switch
        {
            "get_calendar_view" => await calendarService.GetCalendarViewAsync(
                DateTime.Parse(args["start"].ToString()!),
                DateTime.Parse(args["end"].ToString()!)),

            "validate_travel" => travelService.ValidateTravel(
                Convert.ToDouble(args["from_lat"]),
                Convert.ToDouble(args["from_lon"]),
                Convert.ToDouble(args["to_lat"]),
                Convert.ToDouble(args["to_lon"]),
                DateTime.Parse(args["departure_time"].ToString()!),
                DateTime.Parse(args["arrival_deadline"].ToString()!)),

            "add_trip_event" => await AddTripEventWithPersistence(args),

            "search_area" => new { note = "search_area uses LLM internal knowledge, no external call needed",
                                   area = args["area"].ToString() },

            _ => new { error = $"Unknown tool: {toolCall.Function.Name}" }
        };
    }

    private async Task<object> AddTripEventWithPersistence(Dictionary<string, object> args)
    {
        var summary = args["summary"].ToString()!;
        var location = args["location"].ToString()!;
        var start = DateTime.Parse(args["start"].ToString()!);
        var end = DateTime.Parse(args["end"].ToString()!);
        var description = args.TryGetValue("description", out var desc) ? desc.ToString() : null;

        var calendarEventId = await calendarService.CreateEventAsync(summary, location, start, end, description);

        var trips = await persistenceService.GetTripsAsync();
        var trip = trips.FirstOrDefault(t => t.Status == "draft")
            ?? await persistenceService.CreateTripAsync(location, start, end);

        await persistenceService.AddTripEventAsync(trip.Id, summary, location, 0, 0, start, end, calendarEventId);

        return new { calendarEventId, summary, location, start, end };
    }
}
