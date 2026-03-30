namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public interface IPersistenceService
{
    Task<Trip> CreateTripAsync(string destination, DateTime startDate, DateTime endDate);
    Task<TripEvent> AddTripEventAsync(int tripId, string summary, string location,
        double latitude, double longitude, DateTime start, DateTime end, string? calendarEventId);
    Task<List<Trip>> GetTripsAsync();
    Task<Trip?> GetTripByIdAsync(int id);
    Task<List<CachedLocation>> SearchCachedLocationsAsync(string area, string? category = null);
    Task CacheLocationsAsync(List<CachedLocation> locations);
    Task<CachedLocation?> GetCachedLocationByNameAsync(string name);
    Task<bool> DeleteTripEventAsync(int eventId);
    Task SavePreferenceAsync(string key, string value, string source);
    Task<List<UserPreference>> GetPreferencesAsync();
    Task<bool> DeletePreferenceAsync(string key);
    Task<Dictionary<string, Dictionary<string, int>>> GetUserChoiceHistoryAsync();
}
