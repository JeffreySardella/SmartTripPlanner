namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public interface ICalendarService
{
    /// <summary>The calendar ID events are created in. Defaults to "primary".</summary>
    string TargetCalendarId { get; set; }
    bool IsConfigured { get; }
    Task<List<CalendarInfo>> ListCalendarsAsync();
    Task<CalendarInfo> CreateCalendarAsync(string name);
    Task DeleteCalendarAsync(string calendarId);
    Task<CalendarUserProfile?> GetUserProfileAsync();
    Task<List<FreeBusyBlock>> GetCalendarViewAsync(DateTime start, DateTime end);
    Task<string> CreateEventAsync(string summary, string location, DateTime start, DateTime end, string? description = null);
    Task<List<string>> PushItineraryAsync(List<TripEvent> events);
}

public class CalendarInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool IsPrimary { get; set; }
}

public class CalendarUserProfile
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
