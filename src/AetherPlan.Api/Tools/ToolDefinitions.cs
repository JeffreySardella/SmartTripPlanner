namespace AetherPlan.Api.Tools;

using AetherPlan.Api.Models;

public static class ToolDefinitions
{
    public static List<OllamaTool> GetAllTools() =>
    [
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "get_calendar_view",
                Description = "Returns free/busy time blocks for a date range from Google Calendar",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        start = new { type = "string", description = "ISO 8601 start date (e.g. 2026-03-10T00:00:00)" },
                        end = new { type = "string", description = "ISO 8601 end date (e.g. 2026-03-12T23:59:59)" }
                    },
                    required = new[] { "start", "end" }
                }
            }
        },
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "validate_travel",
                Description = "Checks if travel between two locations is feasible given departure time and arrival deadline",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        from_lat = new { type = "number", description = "Origin latitude" },
                        from_lon = new { type = "number", description = "Origin longitude" },
                        to_lat = new { type = "number", description = "Destination latitude" },
                        to_lon = new { type = "number", description = "Destination longitude" },
                        departure_time = new { type = "string", description = "ISO 8601 departure time" },
                        arrival_deadline = new { type = "string", description = "ISO 8601 arrival deadline" }
                    },
                    required = new[] { "from_lat", "from_lon", "to_lat", "to_lon", "departure_time", "arrival_deadline" }
                }
            }
        },
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "add_trip_event",
                Description = "Creates a Google Calendar event with location and description",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        summary = new { type = "string", description = "Event title" },
                        location = new { type = "string", description = "Event location name or address" },
                        start = new { type = "string", description = "ISO 8601 start time" },
                        end = new { type = "string", description = "ISO 8601 end time" },
                        description = new { type = "string", description = "Event description (optional)" },
                        latitude = new { type = "number", description = "Location latitude (optional)" },
                        longitude = new { type = "number", description = "Location longitude (optional)" }
                    },
                    required = new[] { "summary", "location", "start", "end" }
                }
            }
        },
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "search_area",
                Description = "Uses internal knowledge to suggest attractions, restaurants, and activities in a given area",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        area = new { type = "string", description = "City, neighborhood, or region to search" },
                        category = new { type = "string", description = "Category: attractions, restaurants, activities, hotels" },
                        limit = new { type = "integer", description = "Max number of suggestions (default 5)" }
                    },
                    required = new[] { "area" }
                }
            }
        }
    ];
}
