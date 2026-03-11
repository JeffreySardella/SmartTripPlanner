namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Data;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

public class LocationServiceTests : IDisposable
{
    private readonly AetherPlanDbContext _db;
    private readonly ILlmClient _llmClient;
    private readonly LocationService _service;

    public LocationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AetherPlanDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AetherPlanDbContext(options);
        _llmClient = Substitute.For<ILlmClient>();
        _service = new LocationService(_db, _llmClient);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SaveLocationAsync_WithName_SavesWithoutLlm()
    {
        var request = new SaveLocationRequest
        {
            Name = "Test Cafe",
            Address = "123 Main St",
            Category = "cafe",
            Latitude = 48.85,
            Longitude = 2.29,
            SourceUrl = "https://example.com/cafe"
        };

        var result = await _service.SaveLocationAsync(request);

        Assert.Equal("Test Cafe", result.Name);
        Assert.Equal("123 Main St", result.Address);
        Assert.Equal("cafe", result.Category);
        Assert.Equal("https://example.com/cafe", result.SourceUrl);
        Assert.Equal(1, await _db.CachedLocations.CountAsync());
        // Verify LLM was NOT called
        await _llmClient.DidNotReceive().ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>());
    }

    [Fact]
    public async Task SaveLocationAsync_WithoutName_WithRawContent_CallsLlm()
    {
        _llmClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(new LlmChatResponse
            {
                Message = new LlmMessage
                {
                    Role = "assistant",
                    Content = """{"name": "Extracted Cafe", "address": "456 Oak Ave", "category": "cafe"}"""
                },
                Done = true
            });

        var request = new SaveLocationRequest
        {
            RawPageContent = "Welcome to Extracted Cafe at 456 Oak Ave. Best coffee in town.",
            SourceUrl = "https://blog.example.com/cafe-review"
        };

        var result = await _service.SaveLocationAsync(request);

        Assert.Equal("Extracted Cafe", result.Name);
        Assert.Equal("456 Oak Ave", result.Address);
        Assert.Equal("cafe", result.Category);
        await _llmClient.Received(1).ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>());
    }

    [Fact]
    public async Task SaveLocationAsync_WithoutName_NoRawContent_Throws()
    {
        var request = new SaveLocationRequest { SourceUrl = "https://example.com" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SaveLocationAsync(request));
    }

    [Fact]
    public async Task SaveLocationAsync_LlmFailsToExtractName_Throws()
    {
        _llmClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(new LlmChatResponse
            {
                Message = new LlmMessage
                {
                    Role = "assistant",
                    Content = """{"name": null, "address": null, "category": null}"""
                },
                Done = true
            });

        var request = new SaveLocationRequest
        {
            RawPageContent = "This page has no location information at all."
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SaveLocationAsync(request));
    }

    [Fact]
    public async Task SaveLocationAsync_RawContentExceedsLimit_Throws()
    {
        var request = new SaveLocationRequest
        {
            RawPageContent = new string('x', 10_001)
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SaveLocationAsync(request));
    }

    [Fact]
    public async Task SaveLocationAsync_DefaultsCategoryToOther()
    {
        var request = new SaveLocationRequest
        {
            Name = "Mystery Place",
            SourceUrl = "https://example.com"
        };

        var result = await _service.SaveLocationAsync(request);

        Assert.Equal("other", result.Category);
    }

    [Fact]
    public async Task SaveLocationAsync_TruncatesRawPageContent()
    {
        var longContent = new string('x', 5000);
        _llmClient.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(new LlmChatResponse
            {
                Message = new LlmMessage
                {
                    Role = "assistant",
                    Content = """{"name": "Found Place", "address": null, "category": "other"}"""
                },
                Done = true
            });

        var request = new SaveLocationRequest { RawPageContent = longContent };

        await _service.SaveLocationAsync(request);

        var receivedMessages = (List<LlmMessage>)_llmClient.ReceivedCalls().First().GetArguments()[0]!;
        var userContent = receivedMessages.First(m => m.Role == "user").Content!;
        Assert.True(userContent.Length < 3000); // 2000 char content + prompt text
    }

    [Fact]
    public async Task GetLocationsAsync_UnassignedOnly_ReturnsNullTripId()
    {
        _db.CachedLocations.AddRange(
            new CachedLocation { Name = "Idea", Category = "cafe", TripId = null },
            new CachedLocation { Name = "Assigned", Category = "restaurant", TripId = 1 }
        );
        await _db.SaveChangesAsync();

        var results = await _service.GetLocationsAsync(tripId: null, unassignedOnly: true);

        Assert.Single(results);
        Assert.Equal("Idea", results[0].Name);
    }

    [Fact]
    public async Task GetLocationsAsync_ByTripId_FiltersCorrectly()
    {
        _db.Trips.Add(new Trip
        {
            Destination = "Paris", StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(5), Status = "draft"
        });
        await _db.SaveChangesAsync();

        _db.CachedLocations.AddRange(
            new CachedLocation { Name = "Paris Spot", Category = "attraction", TripId = 1 },
            new CachedLocation { Name = "Other Spot", Category = "cafe", TripId = null }
        );
        await _db.SaveChangesAsync();

        var results = await _service.GetLocationsAsync(tripId: 1, unassignedOnly: false);

        Assert.Single(results);
        Assert.Equal("Paris Spot", results[0].Name);
    }

    [Fact]
    public async Task GetLocationsAsync_NoFilters_ReturnsAll()
    {
        _db.CachedLocations.AddRange(
            new CachedLocation { Name = "A", Category = "cafe", TripId = null },
            new CachedLocation { Name = "B", Category = "restaurant", TripId = 1 }
        );
        await _db.SaveChangesAsync();

        var results = await _service.GetLocationsAsync(tripId: null, unassignedOnly: false);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task AssignToTripAsync_ValidIds_UpdatesTripId()
    {
        _db.Trips.Add(new Trip
        {
            Destination = "Tokyo", StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(7), Status = "draft"
        });
        _db.CachedLocations.Add(new CachedLocation { Name = "Sushi Place", Category = "restaurant" });
        await _db.SaveChangesAsync();

        var result = await _service.AssignToTripAsync(locationId: 1, tripId: 1);

        Assert.Equal(1, result.TripId);
    }

    [Fact]
    public async Task AssignToTripAsync_InvalidLocationId_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.AssignToTripAsync(locationId: 999, tripId: 1));
    }

    [Fact]
    public async Task AssignToTripAsync_InvalidTripId_Throws()
    {
        _db.CachedLocations.Add(new CachedLocation { Name = "Place", Category = "other" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.AssignToTripAsync(locationId: 1, tripId: 999));
    }
}
