namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface IPersistenceService
{
    Task<Trip> CreateTripAsync(string destination, DateTime startDate, DateTime endDate);
    Task<TripEvent> AddTripEventAsync(int tripId, string summary, string location,
        double latitude, double longitude, DateTime start, DateTime end, string? calendarEventId);
    Task<List<Trip>> GetTripsAsync();
    Task<Trip?> GetTripByIdAsync(int id);
}
