namespace SmartTripPlanner.Api.Tools;

using SmartTripPlanner.Api.Models;

public static class ToolDefinitions
{
    public static List<LlmTool> GetAllTools() =>
    [
        new LlmTool
        {
            Function = new LlmFunction
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
        new LlmTool
        {
            Function = new LlmFunction
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
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "add_trip_event",
                Description = "Creates a trip event and saves it to the calendar and database",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        summary = new { type = "string", description = "Event title" },
                        location = new { type = "string", description = "Event location name or address" },
                        start = new { type = "string", description = "ISO 8601 start time" },
                        end = new { type = "string", description = "ISO 8601 end time" },
                        description = new { type = "string", description = "Event description with travel notes (optional)" },
                        latitude = new { type = "number", description = "Location latitude" },
                        longitude = new { type = "number", description = "Location longitude" }
                    },
                    required = new[] { "summary", "location", "start", "end" }
                }
            }
        },
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "search_area",
                Description = "Searches for cached locations in the database for a given area and category",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        area = new { type = "string", description = "City, neighborhood, or region to search" },
                        category = new { type = "string", description = "Category: attractions, restaurants, activities, hotels" },
                        limit = new { type = "integer", description = "Max results (default 5)" }
                    },
                    required = new[] { "area" }
                }
            }
        },
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "get_weather",
                Description = "Gets a 7-day weather forecast for a location. Use to check conditions for trip dates.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        latitude = new { type = "number", description = "Location latitude" },
                        longitude = new { type = "number", description = "Location longitude" },
                        date = new { type = "string", description = "Start date for forecast (ISO 8601, e.g. 2026-04-15)" }
                    },
                    required = new[] { "latitude", "longitude", "date" }
                }
            }
        },
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "delete_trip_event",
                Description = "Removes a trip event by its ID from the itinerary",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        event_id = new { type = "integer", description = "The event ID to delete" }
                    },
                    required = new[] { "event_id" }
                }
            }
        },
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "get_trip",
                Description = "Retrieves a saved trip and all its events by trip ID. Use to review or modify existing itineraries.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        trip_id = new { type = "integer", description = "The trip ID to retrieve" }
                    },
                    required = new[] { "trip_id" }
                }
            }
        },
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "search_restaurants",
                Description = "Searches OpenStreetMap for real restaurants and cafes near a location. Returns names, coordinates, cuisine types, and addresses.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        latitude = new { type = "number", description = "Search center latitude" },
                        longitude = new { type = "number", description = "Search center longitude" },
                        radius_meters = new { type = "integer", description = "Search radius in meters (default 2000)" },
                        limit = new { type = "integer", description = "Max results (default 10)" }
                    },
                    required = new[] { "latitude", "longitude" }
                }
            }
        },
        new LlmTool
        {
            Function = new LlmFunction
            {
                Name = "search_hotels",
                Description = "Searches OpenStreetMap for real hotels and accommodations near a location. Returns names, coordinates, star ratings, and addresses.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        latitude = new { type = "number", description = "Search center latitude" },
                        longitude = new { type = "number", description = "Search center longitude" },
                        radius_meters = new { type = "integer", description = "Search radius in meters (default 5000)" },
                        limit = new { type = "integer", description = "Max results (default 10)" }
                    },
                    required = new[] { "latitude", "longitude" }
                }
            }
        }
    ];
}
