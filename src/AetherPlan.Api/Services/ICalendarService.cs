namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface ICalendarService
{
    Task<List<FreeBusyBlock>> GetCalendarViewAsync(DateTime start, DateTime end);
    Task<string> CreateEventAsync(string summary, string location, DateTime start, DateTime end, string? description = null);
    Task<List<string>> PushItineraryAsync(List<TripEvent> events);
}
