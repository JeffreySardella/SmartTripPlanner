namespace SmartTripPlanner.Tests.Services;

using SmartTripPlanner.Api.Data;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.EntityFrameworkCore;

public class PreferenceTests : IDisposable
{
    private readonly SmartTripPlannerDbContext _db;
    private readonly PersistenceService _sut;

    public PreferenceTests()
    {
        var options = new DbContextOptionsBuilder<SmartTripPlannerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new SmartTripPlannerDbContext(options);
        _sut = new PersistenceService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SavePreferenceAsync_NewKey_InsertsRow()
    {
        await _sut.SavePreferenceAsync("pace", "packed", "learned");
        var pref = await _db.UserPreferences.FirstOrDefaultAsync(p => p.Key == "pace");
        Assert.NotNull(pref);
        Assert.Equal("packed", pref!.Value);
        Assert.Equal("learned", pref.Source);
    }

    [Fact]
    public async Task SavePreferenceAsync_ExistingKey_UpdatesValueAndSource()
    {
        await _sut.SavePreferenceAsync("pace", "relaxed", "learned");
        await _sut.SavePreferenceAsync("pace", "packed", "user");
        var all = await _db.UserPreferences.Where(p => p.Key == "pace").ToListAsync();
        Assert.Single(all);
        Assert.Equal("packed", all[0].Value);
        Assert.Equal("user", all[0].Source);
    }

    [Fact]
    public async Task GetPreferencesAsync_ReturnsAll()
    {
        await _sut.SavePreferenceAsync("pace", "packed", "learned");
        await _sut.SavePreferenceAsync("dietary", "vegetarian", "user");
        var prefs = await _sut.GetPreferencesAsync();
        Assert.Equal(2, prefs.Count);
    }

    [Fact]
    public async Task DeletePreferenceAsync_ExistingKey_ReturnsTrue()
    {
        await _sut.SavePreferenceAsync("pace", "packed", "learned");
        var result = await _sut.DeletePreferenceAsync("pace");
        Assert.True(result);
        Assert.Empty(await _db.UserPreferences.ToListAsync());
    }

    [Fact]
    public async Task DeletePreferenceAsync_MissingKey_ReturnsFalse()
    {
        var result = await _sut.DeletePreferenceAsync("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task GetUserChoiceHistoryAsync_AggregatesPaceFromTripDensity()
    {
        var trip = await _sut.CreateTripAsync("Tokyo", DateTime.Today, DateTime.Today.AddDays(1));
        for (var i = 0; i < 7; i++)
        {
            await _sut.AddTripEventAsync(trip.Id, $"Event {i}", "Tokyo",
                35.68, 139.76, DateTime.Today.AddHours(9 + i), DateTime.Today.AddHours(10 + i), null);
        }
        var history = await _sut.GetUserChoiceHistoryAsync();
        Assert.True(history.ContainsKey("pace"));
        Assert.True(history["pace"].ContainsKey("packed"));
        Assert.Equal(1, history["pace"]["packed"]);
    }
}
