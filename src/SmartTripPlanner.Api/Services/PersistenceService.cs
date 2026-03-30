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

    public async Task<bool> DeleteTripEventAsync(int eventId)
    {
        var evt = await db.TripEvents.FindAsync(eventId);
        if (evt is null) return false;
        db.TripEvents.Remove(evt);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task SavePreferenceAsync(string key, string value, string source)
    {
        var existing = await db.UserPreferences.FirstOrDefaultAsync(p => p.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
            existing.Source = source;
        }
        else
        {
            db.UserPreferences.Add(new UserPreference { Key = key, Value = value, Source = source });
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<UserPreference>> GetPreferencesAsync()
    {
        return await db.UserPreferences.OrderBy(p => p.Key).ToListAsync();
    }

    public async Task<bool> DeletePreferenceAsync(string key)
    {
        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.Key == key);
        if (pref is null) return false;
        db.UserPreferences.Remove(pref);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetUserChoiceHistoryAsync()
    {
        var trips = await db.Trips
            .Include(t => t.Events)
            .Where(t => t.Events.Count > 0)
            .ToListAsync();

        var history = new Dictionary<string, Dictionary<string, int>>();

        var paceCounts = new Dictionary<string, int>();
        foreach (var trip in trips)
        {
            var days = (trip.EndDate - trip.StartDate).Days;
            if (days <= 0) days = 1;
            var eventsPerDay = (double)trip.Events.Count / days;
            var pace = eventsPerDay switch
            {
                <= 3 => "relaxed",
                <= 5 => "moderate",
                _ => "packed"
            };
            paceCounts[pace] = paceCounts.GetValueOrDefault(pace) + 1;
        }
        history["pace"] = paceCounts;

        var morningCounts = new Dictionary<string, int>();
        foreach (var trip in trips)
        {
            var earliestHour = trip.Events.Min(e => e.Start.Hour);
            var morning = earliestHour switch
            {
                <= 8 => "early",
                <= 10 => "normal",
                _ => "late"
            };
            morningCounts[morning] = morningCounts.GetValueOrDefault(morning) + 1;
        }
        history["morning_start"] = morningCounts;

        return history;
    }
}
