namespace SmartTripPlanner.Api.Services;

using System.Globalization;
using System.Text.Json;
using SmartTripPlanner.Api.Exceptions;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Tools;

// Ollama/Claude return tool arguments as JsonElement inside Dictionary<string, object>.
// These helpers safely extract typed values without InvalidCastException.
file static class ArgExtensions
{
    public static double GetDouble(this Dictionary<string, object> args, string key)
    {
        var val = args[key];
        return val is JsonElement je ? je.GetDouble() : Convert.ToDouble(val);
    }

    public static double GetDoubleOrDefault(this Dictionary<string, object> args, string key, double fallback = 0.0)
    {
        if (!args.TryGetValue(key, out var val)) return fallback;
        return val is JsonElement je ? je.GetDouble() : Convert.ToDouble(val);
    }

    public static int GetIntOrDefault(this Dictionary<string, object> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var val)) return fallback;
        return val is JsonElement je ? je.GetInt32() : Convert.ToInt32(val);
    }

    public static string? GetStringOrDefault(this Dictionary<string, object> args, string key)
    {
        return args.TryGetValue(key, out var val) ? val.ToString() : null;
    }

    public static DateTime GetDateTime(this Dictionary<string, object> args, string key)
    {
        return DateTime.Parse(args[key].ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}

public class AgentService(
    ILlmClient llmClient,
    ICalendarService calendarService,
    ITravelService travelService,
    IPersistenceService persistenceService,
    WeatherService weatherService,
    PoiService poiService,
    IWebHostEnvironment env,
    ILogger<AgentService> logger) : IAgentService
{
    private string? _cachedSystemPrompt;

    private string SystemPrompt
    {
        get
        {
            if (_cachedSystemPrompt is not null) return _cachedSystemPrompt;

            var promptPath = Path.Combine(env.ContentRootPath, "Prompts", "system-prompt.md");
            if (File.Exists(promptPath))
            {
                _cachedSystemPrompt = File.ReadAllText(promptPath);
                logger.LogInformation("Loaded system prompt from {Path} ({Length} chars)", promptPath, _cachedSystemPrompt.Length);
            }
            else
            {
                logger.LogWarning("System prompt file not found at {Path}, using fallback", promptPath);
                _cachedSystemPrompt = "You are a professional travel logistician. Plan trips using the available tools.";
            }

            return _cachedSystemPrompt;
        }
    }

    public Task<string> RunAsync(string userRequest, int maxIterations = 10)
        => RunAsync(userRequest, _ => { }, maxIterations);

    public async Task<string> RunAsync(string userRequest, Action<AgentProgress> onProgress, int maxIterations = 10)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = SystemPrompt },
            new() { Role = "user", Content = userRequest }
        };

        var tools = ToolDefinitions.GetAllTools();
        int? currentRunTripId = null;

        for (var i = 0; i < maxIterations; i++)
        {
            logger.LogInformation("Agent iteration {Iteration}", i + 1);
            onProgress(new AgentProgress
            {
                Iteration = i + 1, MaxIterations = maxIterations,
                Status = i == 0 ? "Thinking..." : "Processing results...",
                ElapsedSec = sw.Elapsed.TotalSeconds
            });

            LlmChatResponse response;
            try
            {
                response = await llmClient.ChatAsync(messages, tools);
            }
            catch (LlmUnavailableException ex)
            {
                logger.LogError(ex, "LLM service is unavailable");
                return $"LLM service is unavailable: {ex.Message}";
            }

            var message = response.Message;

            // If no tool calls, we have a final text response
            if (message.ToolCalls is null || message.ToolCalls.Count == 0)
            {
                onProgress(new AgentProgress
                {
                    Iteration = i + 1, MaxIterations = maxIterations,
                    Status = "Done", ElapsedSec = sw.Elapsed.TotalSeconds
                });
                return message.Content ?? string.Empty;
            }

            // Add assistant message with tool calls to history
            messages.Add(message);

            // Execute each tool call
            foreach (var toolCall in message.ToolCalls)
            {
                var friendlyName = toolCall.Function.Name switch
                {
                    "get_calendar_view" => "Checking calendar",
                    "search_area" => "Researching destination",
                    "validate_travel" => "Validating travel routes",
                    "add_trip_event" => "Saving event",
                    "get_weather" => "Checking weather forecast",
                    "delete_trip_event" => "Removing event",
                    "get_trip" => "Loading trip details",
                    "search_restaurants" => "Finding restaurants nearby",
                    "search_hotels" => "Finding hotels nearby",
                    _ => toolCall.Function.Name
                };

                onProgress(new AgentProgress
                {
                    Iteration = i + 1, MaxIterations = maxIterations,
                    Status = friendlyName, ToolName = toolCall.Function.Name,
                    ElapsedSec = sw.Elapsed.TotalSeconds
                });

                logger.LogInformation("Executing tool: {ToolName}", toolCall.Function.Name);

                object result;
                try
                {
                    result = await ExecuteToolAsync(toolCall, currentRunTripId, id => currentRunTripId = id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Tool {ToolName} failed", toolCall.Function.Name);
                    result = new { error = $"{toolCall.Function.Name} failed: {ex.Message}" };
                }

                messages.Add(new LlmMessage
                {
                    Role = "tool",
                    Content = JsonSerializer.Serialize(result),
                    ToolCallId = toolCall.Id
                });
            }
        }

        return "Agent reached max iterations without completing. Please try a more specific request.";
    }

    private async Task<object> ExecuteToolAsync(LlmToolCall toolCall, int? currentRunTripId, Action<int> setTripId)
    {
        var args = toolCall.Function.Arguments;

        return toolCall.Function.Name switch
        {
            "get_calendar_view" => await calendarService.GetCalendarViewAsync(
                args.GetDateTime("start"),
                args.GetDateTime("end")),

            "validate_travel" => travelService.ValidateTravel(
                args.GetDouble("from_lat"),
                args.GetDouble("from_lon"),
                args.GetDouble("to_lat"),
                args.GetDouble("to_lon"),
                args.GetDateTime("departure_time"),
                args.GetDateTime("arrival_deadline")),

            "add_trip_event" => await AddTripEventWithPersistence(args, currentRunTripId, setTripId),

            "search_area" => await SearchAreaAsync(args),

            "get_weather" => await weatherService.GetWeatherAsync(
                args.GetDouble("latitude"),
                args.GetDouble("longitude"),
                args.GetDateTime("date")),

            "delete_trip_event" => await DeleteTripEventAsync(args),

            "get_trip" => await GetTripAsync(args),

            "search_restaurants" => await poiService.SearchRestaurantsAsync(
                args.GetDouble("latitude"),
                args.GetDouble("longitude"),
                args.GetIntOrDefault("radius_meters", 2000),
                args.GetIntOrDefault("limit", 10)),

            "search_hotels" => await poiService.SearchHotelsAsync(
                args.GetDouble("latitude"),
                args.GetDouble("longitude"),
                args.GetIntOrDefault("radius_meters", 5000),
                args.GetIntOrDefault("limit", 10)),

            _ => new { error = $"Unknown tool: {toolCall.Function.Name}" }
        };
    }

    private async Task<object> AddTripEventWithPersistence(Dictionary<string, object> args, int? currentRunTripId, Action<int> setTripId)
    {
        var summary = args["summary"].ToString()!;
        var location = args["location"].ToString()!;
        var start = args.GetDateTime("start");
        var end = args.GetDateTime("end");
        var description = args.GetStringOrDefault("description");
        var latitude = args.GetDoubleOrDefault("latitude");
        var longitude = args.GetDoubleOrDefault("longitude");

        // Try to push to Google Calendar; degrade gracefully if unavailable
        string? calendarEventId = null;
        try
        {
            calendarEventId = await calendarService.CreateEventAsync(summary, location, start, end, description);
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Calendar unavailable — saving event to database only (degraded mode)");
        }

        Trip trip;
        if (currentRunTripId.HasValue)
        {
            trip = (await persistenceService.GetTripByIdAsync(currentRunTripId.Value))!;
        }
        else
        {
            trip = await persistenceService.CreateTripAsync(location, start, end);
            setTripId(trip.Id);
        }

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

        var mode = calendarEventId is not null ? "calendar+database" : "database-only (degraded)";
        return new { calendarEventId, summary, location, start, end, latitude, longitude, mode };
    }

    private async Task<object> SearchAreaAsync(Dictionary<string, object> args)
    {
        var area = args["area"].ToString()!;
        var category = args.GetStringOrDefault("category");
        var limit = args.GetIntOrDefault("limit", 5);

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
            hint = "No cached locations for this area. Use your own knowledge to suggest real places with accurate coordinates (lat/lng). You can also call search_restaurants or search_hotels with coordinates to find real nearby places. Proceed to build the itinerary with validate_travel and add_trip_event."
        };
    }

    private async Task<object> DeleteTripEventAsync(Dictionary<string, object> args)
    {
        var eventId = args.GetIntOrDefault("event_id", 0);
        if (eventId == 0) return new { error = "event_id is required" };

        var deleted = await persistenceService.DeleteTripEventAsync(eventId);
        return deleted
            ? new { success = true, message = $"Event {eventId} deleted" }
            : (object)new { success = false, message = $"Event {eventId} not found" };
    }

    private async Task<object> GetTripAsync(Dictionary<string, object> args)
    {
        var tripId = args.GetIntOrDefault("trip_id", 0);
        if (tripId == 0) return new { error = "trip_id is required" };

        var trip = await persistenceService.GetTripByIdAsync(tripId);
        if (trip is null) return new { error = $"Trip {tripId} not found" };

        return new
        {
            trip.Id, trip.Destination, trip.Status,
            startDate = trip.StartDate.ToString("o"),
            endDate = trip.EndDate.ToString("o"),
            events = trip.Events.Select(e => new
            {
                e.Id, e.Summary, e.Location,
                e.Latitude, e.Longitude,
                start = e.Start.ToString("o"),
                end = e.End.ToString("o"),
                e.CalendarEventId
            })
        };
    }
}
