namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Data;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using Microsoft.EntityFrameworkCore;

public class PersistenceServiceTests : IDisposable
{
    private readonly AetherPlanDbContext _db;
    private readonly PersistenceService _sut;

    public PersistenceServiceTests()
    {
        var options = new DbContextOptionsBuilder<AetherPlanDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AetherPlanDbContext(options);
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
}
