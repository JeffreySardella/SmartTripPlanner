namespace SmartTripPlanner.Tests.Services;

using SmartTripPlanner.Api.Data;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.EntityFrameworkCore;

public class PersistenceServiceTests : IDisposable
{
    private readonly SmartTripPlannerDbContext _db;
    private readonly PersistenceService _sut;

    public PersistenceServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartTripPlannerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new SmartTripPlannerDbContext(options);
        _sut = new PersistenceService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateTripAsync_SavesTrip_ReturnsWithId()
    {
        var trip = await _sut.CreateTripAsync("Tokyo", DateTime.Today, DateTime.Today.AddDays(3));

        Assert.True(trip.Id > 0);
        Assert.Equal("Tokyo", trip.Destination);
        Assert.Equal("draft", trip.Status);
    }

    [Fact]
    public async Task AddTripEventAsync_SavesEvent_LinksToTrip()
    {
        var trip = await _sut.CreateTripAsync("Paris", DateTime.Today, DateTime.Today.AddDays(2));

        var evt = await _sut.AddTripEventAsync(trip.Id, "Eiffel Tower", "Champ de Mars",
            48.8584, 2.2945, DateTime.Today.AddHours(10), DateTime.Today.AddHours(12), "cal-123");

        Assert.True(evt.Id > 0);
        Assert.Equal(trip.Id, evt.TripId);
        Assert.Equal("cal-123", evt.CalendarEventId);
    }

    [Fact]
    public async Task GetTripsAsync_ReturnsAllTrips()
    {
        await _sut.CreateTripAsync("Tokyo", DateTime.Today, DateTime.Today.AddDays(3));
        await _sut.CreateTripAsync("Paris", DateTime.Today, DateTime.Today.AddDays(2));

        var trips = await _sut.GetTripsAsync();

        Assert.Equal(2, trips.Count);
    }

    [Fact]
    public async Task GetTripByIdAsync_WithEvents_ReturnsTripAndEvents()
    {
        var trip = await _sut.CreateTripAsync("London", DateTime.Today, DateTime.Today.AddDays(1));
        await _sut.AddTripEventAsync(trip.Id, "Big Ben", "Westminster", 51.5007, -0.1246,
            DateTime.Today.AddHours(9), DateTime.Today.AddHours(10), null);

        var result = await _sut.GetTripByIdAsync(trip.Id);

        Assert.NotNull(result);
        Assert.Single(result!.Events);
        Assert.Equal("Big Ben", result.Events[0].Summary);
    }

    [Fact]
    public async Task GetTripByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _sut.GetTripByIdAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchCachedLocationsAsync_MatchingArea_ReturnsResults()
    {
        _db.CachedLocations.Add(new CachedLocation
        {
            Name = "Tokyo Tower", Latitude = 35.6586, Longitude = 139.7454,
            Category = "attractions", LastUpdated = DateTime.UtcNow
        });
        _db.CachedLocations.Add(new CachedLocation
        {
            Name = "Shibuya Crossing", Latitude = 35.6595, Longitude = 139.7004,
            Category = "attractions", LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var results = await _sut.SearchCachedLocationsAsync("Tokyo");

        Assert.Single(results);
        Assert.Equal("Tokyo Tower", results[0].Name);
    }

    [Fact]
    public async Task SearchCachedLocationsAsync_StaleEntries_ExcludesOlderThan30Days()
    {
        _db.CachedLocations.Add(new CachedLocation
        {
            Name = "Old Tokyo Spot", Latitude = 35.68, Longitude = 139.76,
            Category = "attractions", LastUpdated = DateTime.UtcNow.AddDays(-31)
        });
        _db.CachedLocations.Add(new CachedLocation
        {
            Name = "New Tokyo Spot", Latitude = 35.69, Longitude = 139.77,
            Category = "attractions", LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var results = await _sut.SearchCachedLocationsAsync("Tokyo");

        Assert.Single(results);
        Assert.Equal("New Tokyo Spot", results[0].Name);
    }

    [Fact]
    public async Task SearchCachedLocationsAsync_WithCategory_FiltersCorrectly()
    {
        _db.CachedLocations.Add(new CachedLocation
        {
            Name = "Sushi Dai", Latitude = 35.66, Longitude = 139.77,
            Category = "restaurants", LastUpdated = DateTime.UtcNow
        });
        _db.CachedLocations.Add(new CachedLocation
        {
            Name = "Senso-ji Temple", Latitude = 35.71, Longitude = 139.79,
            Category = "attractions", LastUpdated = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var results = await _sut.SearchCachedLocationsAsync("", "restaurants");

        Assert.Single(results);
        Assert.Equal("Sushi Dai", results[0].Name);
    }

    [Fact]
    public async Task CacheLocationsAsync_NewLocations_PersistsToDb()
    {
        var locations = new List<CachedLocation>
        {
            new() { Name = "Fushimi Inari", Latitude = 34.9671, Longitude = 135.7727, Category = "attractions" }
        };

        await _sut.CacheLocationsAsync(locations);

        var saved = await _db.CachedLocations.FirstOrDefaultAsync(l => l.Name == "Fushimi Inari");
        Assert.NotNull(saved);
        Assert.Equal(34.9671, saved!.Latitude);
    }
}
