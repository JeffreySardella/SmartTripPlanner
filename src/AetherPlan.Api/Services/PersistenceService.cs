namespace AetherPlan.Api.Services;

using AetherPlan.Api.Data;
using AetherPlan.Api.Models;
using Microsoft.EntityFrameworkCore;

public class PersistenceService(AetherPlanDbContext db) : IPersistenceService
{
    public async Task<Trip> CreateTripAsync(string destination, DateTime startDate, DateTime endDate)
    {
        var trip = new Trip
        {
            Destination = destination,
            StartDate = startDate,
            EndDate = endDate
        };

        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip;
    }

    public async Task<TripEvent> AddTripEventAsync(int tripId, string summary, string location,
        double latitude, double longitude, DateTime start, DateTime end, string? calendarEventId)
    {
        var evt = new TripEvent
        {
            TripId = tripId,
            Summary = summary,
            Location = location,
            Latitude = latitude,
            Longitude = longitude,
            Start = start,
            End = end,
            CalendarEventId = calendarEventId
        };

        db.TripEvents.Add(evt);
        await db.SaveChangesAsync();
        return evt;
    }

    public async Task<List<Trip>> GetTripsAsync()
    {
        return await db.Trips
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Trip?> GetTripByIdAsync(int id)
    {
        return await db.Trips
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == id);
    }
}
