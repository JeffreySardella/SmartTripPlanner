namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;
using AetherPlan.Api.Tools;

public class AgentService(
    ILlmClient llmClient,
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
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = SystemPrompt },
            new() { Role = "user", Content = userRequest }
        };

        var tools = ToolDefinitions.GetAllTools();

        for (var i = 0; i < maxIterations; i++)
        {
            logger.LogInformation("Agent iteration {Iteration}", i + 1);

            LlmChatResponse response;
            try
            {
                response = await llmClient.ChatAsync(messages, tools);
            }
            catch (OllamaUnavailableException ex)
            {
                logger.LogError(ex, "LLM service is unavailable");
                return $"LLM service is unavailable: {ex.Message}";
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

                messages.Add(new LlmMessage
                {
                    Role = "tool",
                    Content = JsonSerializer.Serialize(result)
                });
            }
        }

        return "Agent reached max iterations without completing. Please try a more specific request.";
    }

    private async Task<object> ExecuteToolAsync(LlmToolCall toolCall)
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

            "search_area" => await SearchAreaAsync(args),

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
        var latitude = args.TryGetValue("latitude", out var lat) ? Convert.ToDouble(lat) : 0.0;
        var longitude = args.TryGetValue("longitude", out var lng) ? Convert.ToDouble(lng) : 0.0;

        var calendarEventId = await calendarService.CreateEventAsync(summary, location, start, end, description);

        var trips = await persistenceService.GetTripsAsync();
        var trip = trips.FirstOrDefault(t => t.Status == "draft")
            ?? await persistenceService.CreateTripAsync(location, start, end);

        await persistenceService.AddTripEventAsync(trip.Id, summary, location, latitude, longitude, start, end, calendarEventId);

        if (latitude != 0.0 || longitude != 0.0)
        {
            await persistenceService.CacheLocationsAsync([
                new CachedLocation
                {
                    Name = location,
                    Latitude = latitude,
                    Longitude = longitude,
                    Category = "general"
                }
            ]);
        }

        return new { calendarEventId, summary, location, start, end, latitude, longitude };
    }

    private async Task<object> SearchAreaAsync(Dictionary<string, object> args)
    {
        var area = args["area"].ToString()!;
        var category = args.TryGetValue("category", out var cat) ? cat.ToString() : null;
        var limit = args.TryGetValue("limit", out var lim) ? Convert.ToInt32(lim) : 5;

        var cached = await persistenceService.SearchCachedLocationsAsync(area, category);

        if (cached.Count > 0)
        {
            var results = cached.Take(limit).Select(l => new
            {
                l.Name, l.Latitude, l.Longitude, l.Category
            });
            return new { cached = true, locations = results };
        }

        return new
        {
            cached = false, area, category,
            hint = "No cached locations found. Please suggest places with their coordinates."
        };
    }
}
