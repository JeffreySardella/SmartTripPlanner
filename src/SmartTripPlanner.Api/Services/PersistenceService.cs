namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Data;
using SmartTripPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;

public class PersistenceService(SmartTripPlannerDbContext db) : IPersistenceService
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

    public async Task<List<CachedLocation>> SearchCachedLocationsAsync(string area, string? category = null)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var query = db.CachedLocations
            .Where(l => l.LastUpdated > cutoff);

        if (!string.IsNullOrWhiteSpace(area))
            query = query.Where(l => l.Name.Contains(area));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(l => l.Category == category);

        return await query.OrderBy(l => l.Name).ToListAsync();
    }

    public async Task CacheLocationsAsync(List<CachedLocation> locations)
    {
        foreach (var location in locations)
        {
            var existing = await db.CachedLocations
                .FirstOrDefaultAsync(l => l.Name == location.Name && l.Category == location.Category);

            if (existing is not null)
            {
                existing.Latitude = location.Latitude;
                existing.Longitude = location.Longitude;
                existing.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                location.LastUpdated = DateTime.UtcNow;
                db.CachedLocations.Add(location);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<CachedLocation?> GetCachedLocationByNameAsync(string name)
    {
        return await db.CachedLocations
            .FirstOrDefaultAsync(l => l.Name == name);
    }
}
