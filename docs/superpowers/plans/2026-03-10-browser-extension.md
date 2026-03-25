# SmartTripPlanner Browser Extension Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Chrome extension that scrapes location data from webpages and saves it to SmartTripPlanner via new REST endpoints, with LLM fallback for unrecognized pages.

**Architecture:** New `ILocationService` + `LocationsController` on the API side with 3 endpoints (save, list, assign). Manifest V3 Chrome extension with content script scraping, minimal popup UI, and background service worker for API communication. `CachedLocation` model extended with `Address`, `SourceUrl`, and `TripId` columns.

**Tech Stack:** C# 12 / ASP.NET Core 8 / EF Core / SQLite / System.Text.Json / Manifest V3 / vanilla JS

**Spec:** `docs/superpowers/specs/2026-03-10-browser-extension-design.md`

---

## File Structure

### API (C# — existing project)

| File | Action | Responsibility |
|------|--------|---------------|
| `src/SmartTripPlanner.Api/Models/CachedLocation.cs` | Modify | Add `Address`, `SourceUrl`, `TripId` properties + `Trip` nav property |
| `src/SmartTripPlanner.Api/Models/LocationDtos.cs` | Create | `SaveLocationRequest` and `AssignLocationRequest` DTOs |
| `src/SmartTripPlanner.Api/Data/SmartTripPlannerDbContext.cs` | Modify | Add `CachedLocation` → `Trip` FK relationship |
| `src/SmartTripPlanner.Api/Services/ILocationService.cs` | Create | Interface: `SaveLocationAsync`, `GetLocationsAsync`, `AssignToTripAsync` |
| `src/SmartTripPlanner.Api/Services/LocationService.cs` | Create | Implementation with LLM fallback extraction |
| `src/SmartTripPlanner.Api/Controllers/LocationsController.cs` | Create | 3 endpoints: POST save, GET list, POST assign |
| `src/SmartTripPlanner.Api/Program.cs` | Modify | Register `ILocationService`, add CORS policy |
| `src/SmartTripPlanner.Tests/Services/LocationServiceTests.cs` | Create | Unit tests for LocationService |
| `src/SmartTripPlanner.Tests/Controllers/LocationsControllerTests.cs` | Create | Unit tests for LocationsController |

### Chrome Extension (new directory)

| File | Action | Responsibility |
|------|--------|---------------|
| `src/SmartTripPlanner.Extension/manifest.json` | Create | Extension manifest with permissions |
| `src/SmartTripPlanner.Extension/content.js` | Create | Page scraping orchestrator |
| `src/SmartTripPlanner.Extension/parsers/structured.js` | Create | schema.org JSON-LD, Open Graph, meta tag parsing |
| `src/SmartTripPlanner.Extension/parsers/google-maps.js` | Create | Google Maps URL + DOM parser |
| `src/SmartTripPlanner.Extension/parsers/yelp.js` | Create | Yelp business page parser |
| `src/SmartTripPlanner.Extension/parsers/tripadvisor.js` | Create | TripAdvisor page parser |
| `src/SmartTripPlanner.Extension/background.js` | Create | Service worker, API communication |
| `src/SmartTripPlanner.Extension/popup.html` | Create | Popup UI markup |
| `src/SmartTripPlanner.Extension/popup.js` | Create | Popup logic |
| `src/SmartTripPlanner.Extension/popup.css` | Create | Popup styles |
| `src/SmartTripPlanner.Extension/options.html` | Create | Settings page |
| `src/SmartTripPlanner.Extension/options.js` | Create | Settings logic |

---

## Chunk 1: API Backend

### Task 0: Extend CachedLocation Model + EF Migration

**Files:**
- Modify: `src/SmartTripPlanner.Api/Models/CachedLocation.cs`
- Modify: `src/SmartTripPlanner.Api/Data/SmartTripPlannerDbContext.cs`

- [ ] **Step 1: Add new properties to CachedLocation**

In `src/SmartTripPlanner.Api/Models/CachedLocation.cs`, add `Address`, `SourceUrl`, `TripId`, and the `Trip` navigation property:

```csharp
namespace SmartTripPlanner.Api.Models;

public class CachedLocation
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public required string Category { get; set; }
    public string? SourceUrl { get; set; }
    public int? TripId { get; set; }
    public Trip? Trip { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Add CachedLocation → Trip FK relationship in DbContext**

In `src/SmartTripPlanner.Api/Data/SmartTripPlannerDbContext.cs`, add the relationship inside `OnModelCreating`:

```csharp
modelBuilder.Entity<CachedLocation>()
    .HasOne(cl => cl.Trip)
    .WithMany()
    .HasForeignKey(cl => cl.TripId)
    .OnDelete(DeleteBehavior.SetNull);
```

- [ ] **Step 3: Create and apply EF migration**

Run:
```bash
dotnet ef migrations add AddLocationExtensionFields --project src/SmartTripPlanner.Api
dotnet ef database update --project src/SmartTripPlanner.Api
```

- [ ] **Step 4: Verify build passes**

Run: `dotnet build SmartTripPlanner.sln`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Run existing tests to ensure nothing broke**

Run: `dotnet test SmartTripPlanner.sln --verbosity quiet`
Expected: All 58 tests pass

- [ ] **Step 6: Commit**

```bash
git add src/SmartTripPlanner.Api/Models/CachedLocation.cs src/SmartTripPlanner.Api/Data/SmartTripPlannerDbContext.cs src/SmartTripPlanner.Api/Migrations/
git commit -m "feat: extend CachedLocation with Address, SourceUrl, TripId"
```

---

### Task 1: Create DTOs and ILocationService Interface

**Files:**
- Create: `src/SmartTripPlanner.Api/Models/LocationDtos.cs`
- Create: `src/SmartTripPlanner.Api/Services/ILocationService.cs`

- [ ] **Step 1: Create DTOs**

Create `src/SmartTripPlanner.Api/Models/LocationDtos.cs`:

```csharp
namespace SmartTripPlanner.Api.Models;

public class SaveLocationRequest
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Category { get; set; }
    public string? SourceUrl { get; set; }
    public string? RawPageContent { get; set; }
}

public class AssignLocationRequest
{
    public required int TripId { get; set; }
}
```

- [ ] **Step 2: Create ILocationService interface**

Create `src/SmartTripPlanner.Api/Services/ILocationService.cs`:

```csharp
namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public interface ILocationService
{
    Task<CachedLocation> SaveLocationAsync(SaveLocationRequest request);
    Task<List<CachedLocation>> GetLocationsAsync(int? tripId, bool unassignedOnly);
    Task<CachedLocation> AssignToTripAsync(int locationId, int tripId);
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build SmartTripPlanner.sln`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/SmartTripPlanner.Api/Models/LocationDtos.cs src/SmartTripPlanner.Api/Services/ILocationService.cs
git commit -m "feat: add location DTOs and ILocationService interface"
```

---

### Task 2: LocationService Tests (TDD Red Phase)

**Files:**
- Create: `src/SmartTripPlanner.Tests/Services/LocationServiceTests.cs`

- [ ] **Step 1: Write LocationService unit tests**

Create `src/SmartTripPlanner.Tests/Services/LocationServiceTests.cs`:

```csharp
namespace SmartTripPlanner.Tests.Services;

using SmartTripPlanner.Api.Data;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

public class LocationServiceTests : IDisposable
{
    private readonly SmartTripPlannerDbContext _db;
    private readonly ILlmClient _llmClient;
    private readonly LocationService _service;

    public LocationServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartTripPlannerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new SmartTripPlannerDbContext(options);
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
```

- [ ] **Step 2: Verify tests fail (class doesn't exist yet)**

Run: `dotnet test SmartTripPlanner.sln --verbosity quiet`
Expected: Build error — `LocationService` type not found

- [ ] **Step 3: Commit**

```bash
git add src/SmartTripPlanner.Tests/Services/LocationServiceTests.cs
git commit -m "test: add LocationService unit tests (red phase)"
```

---

### Task 3: Implement LocationService (TDD Green Phase)

**Files:**
- Create: `src/SmartTripPlanner.Api/Services/LocationService.cs`

- [ ] **Step 1: Implement LocationService**

Create `src/SmartTripPlanner.Api/Services/LocationService.cs`:

```csharp
namespace SmartTripPlanner.Api.Services;

using System.Text.Json;
using SmartTripPlanner.Api.Data;
using SmartTripPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;

public class LocationService(SmartTripPlannerDbContext db, ILlmClient llmClient) : ILocationService
{
    private const int MaxRawContentInputLength = 10_000;
    private const int MaxRawContentLength = 2000;

    public async Task<CachedLocation> SaveLocationAsync(SaveLocationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RawPageContent) && request.RawPageContent.Length > MaxRawContentInputLength)
            throw new ArgumentException($"RawPageContent exceeds maximum length of {MaxRawContentInputLength} characters.");

        var name = request.Name;
        var address = request.Address;
        var category = request.Category;

        if (string.IsNullOrWhiteSpace(name))
        {
            if (string.IsNullOrWhiteSpace(request.RawPageContent))
                throw new ArgumentException("Either Name or RawPageContent must be provided.");

            var extracted = await ExtractWithLlmAsync(request.RawPageContent);
            name = extracted.Name;
            address ??= extracted.Address;
            category ??= extracted.Category;

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Could not extract location name from page content.");
        }

        var location = new CachedLocation
        {
            Name = name,
            Address = address,
            Latitude = request.Latitude ?? 0,
            Longitude = request.Longitude ?? 0,
            Category = category ?? "other",
            SourceUrl = request.SourceUrl,
            LastUpdated = DateTime.UtcNow
        };

        db.CachedLocations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    public async Task<List<CachedLocation>> GetLocationsAsync(int? tripId, bool unassignedOnly)
    {
        var query = db.CachedLocations.AsQueryable();

        if (tripId.HasValue)
            query = query.Where(l => l.TripId == tripId.Value);
        else if (unassignedOnly)
            query = query.Where(l => l.TripId == null);

        return await query.OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<CachedLocation> AssignToTripAsync(int locationId, int tripId)
    {
        var location = await db.CachedLocations.FindAsync(locationId)
            ?? throw new KeyNotFoundException($"Location {locationId} not found.");

        var tripExists = await db.Trips.AnyAsync(t => t.Id == tripId);
        if (!tripExists)
            throw new KeyNotFoundException($"Trip {tripId} not found.");

        location.TripId = tripId;
        await db.SaveChangesAsync();
        return location;
    }

    private async Task<ExtractedLocation> ExtractWithLlmAsync(string rawContent)
    {
        var truncated = rawContent.Length > MaxRawContentLength
            ? rawContent[..MaxRawContentLength]
            : rawContent;

        var prompt = $"""
            Extract location information from this webpage text. Respond with ONLY a JSON object, no other text:
            {{"name": "...", "address": "...", "category": "..."}}

            Category should be one of: restaurant, hotel, attraction, bar, cafe, shop, park, museum, other.
            If you cannot determine a field, use null.

            Webpage text:
            {truncated}
            """;

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var response = await llmClient.ChatAsync(messages);
        var content = response.Message.Content ?? "";

        // Extract JSON from response (LLM may wrap in markdown code blocks)
        var jsonStart = content.IndexOf('{');
        var jsonEnd = content.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < 0)
            return new ExtractedLocation();

        var jsonStr = content[jsonStart..(jsonEnd + 1)];

        try
        {
            return JsonSerializer.Deserialize<ExtractedLocation>(jsonStr,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ExtractedLocation();
        }
        catch (JsonException)
        {
            return new ExtractedLocation();
        }
    }

    private class ExtractedLocation
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Category { get; set; }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test SmartTripPlanner.sln --verbosity quiet`
Expected: All tests pass (58 existing + 13 new = 71 total)

- [ ] **Step 3: Commit**

```bash
git add src/SmartTripPlanner.Api/Services/LocationService.cs
git commit -m "feat: implement LocationService with LLM fallback extraction"
```

---

### Task 4: LocationsController Tests (TDD Red Phase)

**Files:**
- Create: `src/SmartTripPlanner.Tests/Controllers/LocationsControllerTests.cs`

- [ ] **Step 1: Write LocationsController unit tests**

Create `src/SmartTripPlanner.Tests/Controllers/LocationsControllerTests.cs`:

```csharp
namespace SmartTripPlanner.Tests.Controllers;

using SmartTripPlanner.Api.Controllers;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class LocationsControllerTests
{
    private readonly ILocationService _locationService;
    private readonly LocationsController _controller;

    public LocationsControllerTests()
    {
        _locationService = Substitute.For<ILocationService>();
        _controller = new LocationsController(_locationService);
    }

    [Fact]
    public async Task SaveLocation_ValidRequest_Returns201WithLocation()
    {
        var request = new SaveLocationRequest { Name = "Test", Category = "cafe" };
        var saved = new CachedLocation { Id = 1, Name = "Test", Category = "cafe" };
        _locationService.SaveLocationAsync(request).Returns(saved);

        var result = await _controller.SaveLocation(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(saved, created.Value);
    }

    [Fact]
    public async Task SaveLocation_LlmExtractionFails_Returns422()
    {
        var request = new SaveLocationRequest { RawPageContent = "no location here" };
        _locationService.SaveLocationAsync(request)
            .ThrowsAsync(new InvalidOperationException("Could not extract"));

        var result = await _controller.SaveLocation(request);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Equal(422, unprocessable.StatusCode);
    }

    [Fact]
    public async Task SaveLocation_MissingNameAndContent_Returns400()
    {
        var request = new SaveLocationRequest();
        _locationService.SaveLocationAsync(request)
            .ThrowsAsync(new ArgumentException("Either Name or RawPageContent must be provided."));

        var result = await _controller.SaveLocation(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task GetLocations_NoFilters_ReturnsAll()
    {
        var locations = new List<CachedLocation>
        {
            new() { Id = 1, Name = "A", Category = "cafe" },
            new() { Id = 2, Name = "B", Category = "restaurant" }
        };
        _locationService.GetLocationsAsync(null, false).Returns(locations);

        var result = await _controller.GetLocations(null, false);

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<List<CachedLocation>>(ok.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task GetLocations_UnassignedOnly_PassesFilterToService()
    {
        _locationService.GetLocationsAsync(null, true).Returns(new List<CachedLocation>());

        await _controller.GetLocations(null, true);

        await _locationService.Received(1).GetLocationsAsync(null, true);
    }

    [Fact]
    public async Task AssignLocation_ValidIds_Returns200()
    {
        var request = new AssignLocationRequest { TripId = 1 };
        var updated = new CachedLocation { Id = 5, Name = "Place", Category = "other", TripId = 1 };
        _locationService.AssignToTripAsync(5, 1).Returns(updated);

        var result = await _controller.AssignLocation(5, request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(updated, ok.Value);
    }

    [Fact]
    public async Task AssignLocation_NotFound_Returns404()
    {
        var request = new AssignLocationRequest { TripId = 1 };
        _locationService.AssignToTripAsync(999, 1)
            .ThrowsAsync(new KeyNotFoundException("Location 999 not found."));

        var result = await _controller.AssignLocation(999, request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }
}
```

- [ ] **Step 2: Verify tests fail (controller doesn't exist yet)**

Run: `dotnet test SmartTripPlanner.sln --verbosity quiet`
Expected: Build error — `LocationsController` type not found

- [ ] **Step 3: Commit**

```bash
git add src/SmartTripPlanner.Tests/Controllers/LocationsControllerTests.cs
git commit -m "test: add LocationsController unit tests (red phase)"
```

---

### Task 5: Implement LocationsController + Wire Up DI + CORS (TDD Green Phase)

**Files:**
- Create: `src/SmartTripPlanner.Api/Controllers/LocationsController.cs`
- Modify: `src/SmartTripPlanner.Api/Program.cs`

- [ ] **Step 1: Implement LocationsController**

Create `src/SmartTripPlanner.Api/Controllers/LocationsController.cs`:

```csharp
namespace SmartTripPlanner.Api.Controllers;

using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class LocationsController(ILocationService locationService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SaveLocation([FromBody] SaveLocationRequest request)
    {
        try
        {
            var location = await locationService.SaveLocationAsync(request);
            return CreatedAtAction(nameof(GetLocations), new { }, location);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocations(
        [FromQuery] int? tripId,
        [FromQuery] bool unassigned = false)
    {
        var locations = await locationService.GetLocationsAsync(tripId, unassigned);
        return Ok(locations);
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> AssignLocation(int id, [FromBody] AssignLocationRequest request)
    {
        try
        {
            var location = await locationService.AssignToTripAsync(id, request.TripId);
            return Ok(location);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
```

- [ ] **Step 2: Register LocationService and add CORS in Program.cs**

In `src/SmartTripPlanner.Api/Program.cs`, add after the `IPersistenceService` registration:

```csharp
builder.Services.AddScoped<ILocationService, LocationService>();
```

Add CORS policy before `builder.Build()`:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionPolicy", policy =>
        policy.SetIsOriginAllowed(origin => origin.StartsWith("chrome-extension://"))
              .WithMethods("GET", "POST")
              .AllowAnyHeader());
});
```

Add CORS middleware after `app` is built, before `app.MapControllers()`:

```csharp
app.UseCors("ExtensionPolicy");
```

- [ ] **Step 3: Run all tests**

Run: `dotnet test SmartTripPlanner.sln --verbosity quiet`
Expected: All tests pass (58 existing + 13 LocationService + 7 LocationsController = 78 total)

- [ ] **Step 4: Commit**

```bash
git add src/SmartTripPlanner.Api/Controllers/LocationsController.cs src/SmartTripPlanner.Api/Program.cs
git commit -m "feat: add LocationsController with CORS and DI wiring"
```

---

## Chunk 2: Chrome Extension

### Task 6: Extension Manifest + Scaffold

**Files:**
- Create: `src/SmartTripPlanner.Extension/manifest.json`
- Create: `src/SmartTripPlanner.Extension/icons/` (placeholder)

- [ ] **Step 1: Create manifest.json**

Create `src/SmartTripPlanner.Extension/manifest.json`:

```json
{
  "manifest_version": 3,
  "name": "SmartTripPlanner Trip Saver",
  "version": "1.0.0",
  "description": "Save locations from any webpage to your SmartTripPlanner trip planner",
  "permissions": ["activeTab", "tabs", "storage", "scripting"],
  "action": {
    "default_popup": "popup.html",
    "default_icon": {
      "16": "icons/icon16.png",
      "48": "icons/icon48.png",
      "128": "icons/icon128.png"
    }
  },
  "background": {
    "service_worker": "background.js"
  },
  "options_page": "options.html",
  "icons": {
    "16": "icons/icon16.png",
    "48": "icons/icon48.png",
    "128": "icons/icon128.png"
  }
}
```

- [ ] **Step 2: Create placeholder icons directory**

Create `src/SmartTripPlanner.Extension/icons/` directory. Add a simple 16x16, 48x48, and 128x128 PNG placeholder (solid color square is fine — can be replaced later).

- [ ] **Step 3: Commit**

```bash
git add src/SmartTripPlanner.Extension/
git commit -m "feat: add Chrome extension manifest and scaffold"
```

---

### Task 7: Structured Data Parser

**Files:**
- Create: `src/SmartTripPlanner.Extension/parsers/structured.js`

- [ ] **Step 1: Implement structured data parser**

Create `src/SmartTripPlanner.Extension/parsers/structured.js`:

```javascript
/**
 * Extracts location data from schema.org JSON-LD, Open Graph tags,
 * and other structured metadata on the page.
 * @returns {{ name?: string, address?: string, latitude?: number, longitude?: number, category?: string } | null}
 */
function parseStructuredData() {
  let result = {};

  // 1. Try schema.org JSON-LD
  const jsonLdScripts = document.querySelectorAll('script[type="application/ld+json"]');
  for (const script of jsonLdScripts) {
    try {
      const data = JSON.parse(script.textContent);
      const items = Array.isArray(data) ? data : [data];
      for (const item of items) {
        const extracted = extractFromSchemaOrg(item);
        if (extracted) {
          result = { ...result, ...extracted };
          break;
        }
      }
    } catch (e) { /* ignore malformed JSON-LD */ }
  }

  // 2. Try Open Graph meta tags
  if (!result.name) {
    const ogTitle = document.querySelector('meta[property="og:title"]');
    if (ogTitle) result.name = ogTitle.content;
  }

  // 3. Try geo meta tags
  if (!result.latitude) {
    const geoLat = document.querySelector('meta[name="geo.position"]');
    if (geoLat) {
      const parts = geoLat.content.split(';');
      if (parts.length === 2) {
        result.latitude = parseFloat(parts[0]);
        result.longitude = parseFloat(parts[1]);
      }
    }

    const icbmMeta = document.querySelector('meta[name="ICBM"]');
    if (icbmMeta && !result.latitude) {
      const parts = icbmMeta.content.split(',').map(s => s.trim());
      if (parts.length === 2) {
        result.latitude = parseFloat(parts[0]);
        result.longitude = parseFloat(parts[1]);
      }
    }
  }

  // 4. Try address elements
  if (!result.address) {
    const addressEl = document.querySelector('address');
    if (addressEl) result.address = addressEl.textContent.trim();
  }

  return result.name ? result : null;
}

function extractFromSchemaOrg(item) {
  const locationTypes = [
    'Restaurant', 'LocalBusiness', 'Place', 'Hotel', 'TouristAttraction',
    'FoodEstablishment', 'LodgingBusiness', 'Museum', 'Park', 'Store',
    'BarOrPub', 'CafeOrCoffeeShop'
  ];

  const itemType = item['@type'];
  if (!itemType) return null;

  const types = Array.isArray(itemType) ? itemType : [itemType];
  const isLocation = types.some(t => locationTypes.includes(t));
  if (!isLocation) return null;

  const result = {};
  result.name = item.name || null;

  if (item.address) {
    if (typeof item.address === 'string') {
      result.address = item.address;
    } else if (item.address.streetAddress) {
      const parts = [
        item.address.streetAddress,
        item.address.addressLocality,
        item.address.addressRegion,
        item.address.postalCode
      ].filter(Boolean);
      result.address = parts.join(', ');
    }
  }

  if (item.geo) {
    result.latitude = parseFloat(item.geo.latitude);
    result.longitude = parseFloat(item.geo.longitude);
  }

  // Map schema.org type to our category
  const typeMap = {
    'Restaurant': 'restaurant', 'FoodEstablishment': 'restaurant',
    'Hotel': 'hotel', 'LodgingBusiness': 'hotel',
    'TouristAttraction': 'attraction', 'Museum': 'museum',
    'Park': 'park', 'Store': 'shop', 'BarOrPub': 'bar',
    'CafeOrCoffeeShop': 'cafe'
  };

  for (const t of types) {
    if (typeMap[t]) { result.category = typeMap[t]; break; }
  }

  return result.name ? result : null;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/SmartTripPlanner.Extension/parsers/structured.js
git commit -m "feat: add structured data parser (schema.org, Open Graph, meta tags)"
```

---

### Task 8: Site-Specific Parsers

**Files:**
- Create: `src/SmartTripPlanner.Extension/parsers/google-maps.js`
- Create: `src/SmartTripPlanner.Extension/parsers/yelp.js`
- Create: `src/SmartTripPlanner.Extension/parsers/tripadvisor.js`

- [ ] **Step 1: Implement Google Maps parser**

Create `src/SmartTripPlanner.Extension/parsers/google-maps.js`:

```javascript
/**
 * Extracts location data from Google Maps pages.
 * Matches: google.com/maps, google.*/maps
 * @returns {{ name?: string, address?: string, latitude?: number, longitude?: number, category?: string } | null}
 */
function parseGoogleMaps() {
  const url = window.location.href;
  if (!url.includes('google.') || !url.includes('/maps')) return null;

  const result = {};

  // Extract coordinates from URL: @lat,lng,zoom
  const coordMatch = url.match(/@(-?\d+\.\d+),(-?\d+\.\d+)/);
  if (coordMatch) {
    result.latitude = parseFloat(coordMatch[1]);
    result.longitude = parseFloat(coordMatch[2]);
  }

  // Extract place name from URL: /place/Place+Name/
  const placeMatch = url.match(/\/place\/([^/@]+)/);
  if (placeMatch) {
    result.name = decodeURIComponent(placeMatch[1]).replace(/\+/g, ' ');
  }

  // Try DOM elements for name and address
  const nameEl = document.querySelector('h1.DUwDvf, h1[data-attrid="title"]');
  if (nameEl) result.name = nameEl.textContent.trim();

  const addressEl = document.querySelector('button[data-item-id="address"] div.Io6YTe');
  if (addressEl) result.address = addressEl.textContent.trim();

  const categoryEl = document.querySelector('button[jsaction*="category"] span');
  if (categoryEl) {
    const cat = categoryEl.textContent.trim().toLowerCase();
    result.category = mapGoogleCategory(cat);
  }

  return result.name ? result : null;
}

function mapGoogleCategory(googleCat) {
  const mapping = {
    'restaurant': 'restaurant', 'hotel': 'hotel', 'museum': 'museum',
    'park': 'park', 'bar': 'bar', 'cafe': 'cafe', 'store': 'shop',
    'shopping': 'shop', 'tourist attraction': 'attraction'
  };
  for (const [key, value] of Object.entries(mapping)) {
    if (googleCat.includes(key)) return value;
  }
  return 'other';
}
```

- [ ] **Step 2: Implement Yelp parser**

Create `src/SmartTripPlanner.Extension/parsers/yelp.js`:

```javascript
/**
 * Extracts location data from Yelp business pages.
 * Matches: yelp.com/biz/
 * @returns {{ name?: string, address?: string, category?: string } | null}
 */
function parseYelp() {
  if (!window.location.hostname.includes('yelp.com')) return null;
  if (!window.location.pathname.startsWith('/biz/')) return null;

  const result = {};

  // Business name from h1
  const nameEl = document.querySelector('h1');
  if (nameEl) result.name = nameEl.textContent.trim();

  // Address from the address element or specific Yelp selector
  const addressEl = document.querySelector('address p');
  if (addressEl) result.address = addressEl.textContent.trim();

  // Category from breadcrumbs or category links
  const categoryLinks = document.querySelectorAll('a[href*="/c/"] span');
  if (categoryLinks.length > 0) {
    const cat = categoryLinks[0].textContent.trim().toLowerCase();
    if (cat.includes('restaurant') || cat.includes('food')) result.category = 'restaurant';
    else if (cat.includes('bar') || cat.includes('pub')) result.category = 'bar';
    else if (cat.includes('coffee') || cat.includes('cafe')) result.category = 'cafe';
    else if (cat.includes('hotel') || cat.includes('hostel')) result.category = 'hotel';
    else if (cat.includes('shop') || cat.includes('store')) result.category = 'shop';
    else result.category = 'other';
  }

  return result.name ? result : null;
}
```

- [ ] **Step 3: Implement TripAdvisor parser**

Create `src/SmartTripPlanner.Extension/parsers/tripadvisor.js`:

```javascript
/**
 * Extracts location data from TripAdvisor pages.
 * Matches: tripadvisor.com
 * @returns {{ name?: string, address?: string, category?: string } | null}
 */
function parseTripAdvisor() {
  if (!window.location.hostname.includes('tripadvisor.com')) return null;

  const result = {};

  // Name from h1
  const nameEl = document.querySelector('h1[data-test-target="top-info-header"], h1#HEADING');
  if (nameEl) result.name = nameEl.textContent.trim();

  // Address
  const addressEl = document.querySelector('span.fHvkI, button[data-automation="open-map"] span');
  if (addressEl) result.address = addressEl.textContent.trim();

  // Category from URL path
  const path = window.location.pathname;
  if (path.includes('/Restaurant')) result.category = 'restaurant';
  else if (path.includes('/Hotel')) result.category = 'hotel';
  else if (path.includes('/Attraction')) result.category = 'attraction';
  else result.category = 'other';

  return result.name ? result : null;
}
```

- [ ] **Step 4: Commit**

```bash
git add src/SmartTripPlanner.Extension/parsers/
git commit -m "feat: add site-specific parsers (Google Maps, Yelp, TripAdvisor)"
```

---

### Task 9: Content Script

**Files:**
- Create: `src/SmartTripPlanner.Extension/content.js`

- [ ] **Step 1: Implement content script**

Create `src/SmartTripPlanner.Extension/content.js`:

```javascript
// Content script — injected on demand by the popup via chrome.scripting.executeScript
// Runs the parsing pipeline and returns extracted location data.
// Idempotency guard: skip if already injected (prevents duplicate listeners)
if (window.__smarttripplannerInjected) {
  // Already injected — do nothing (listener is already registered)
} else {
  window.__smarttripplannerInjected = true;

function extractLocation() {
  // Tier 1: Site-specific parsers (highest quality)
  const siteResult =
    (typeof parseGoogleMaps === 'function' && parseGoogleMaps()) ||
    (typeof parseYelp === 'function' && parseYelp()) ||
    (typeof parseTripAdvisor === 'function' && parseTripAdvisor());

  if (siteResult && siteResult.name) {
    return { success: true, data: siteResult, source: 'site-parser' };
  }

  // Tier 2: Structured data (generic)
  const structuredResult =
    typeof parseStructuredData === 'function' && parseStructuredData();

  if (structuredResult && structuredResult.name) {
    return { success: true, data: structuredResult, source: 'structured-data' };
  }

  // Tier 3: LLM fallback — send raw page text
  const bodyText = (document.body.innerText || '').substring(0, 2000);
  if (bodyText.length > 50) {
    return {
      success: false,
      needsLlm: true,
      rawPageContent: bodyText,
      sourceUrl: window.location.href
    };
  }

  return { success: false, needsLlm: false };
}

// Listen for messages from popup
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'extractLocation') {
    const result = extractLocation();
    result.sourceUrl = result.sourceUrl || window.location.href;
    sendResponse(result);
  }
  return true; // keep message channel open for async response
});

} // end idempotency guard
```

Note: The content script will be injected programmatically along with the parser files. The popup uses `chrome.scripting.executeScript` to inject all parser files + content.js into the active tab, then sends a message to trigger extraction.

- [ ] **Step 2: Commit**

```bash
git add src/SmartTripPlanner.Extension/content.js
git commit -m "feat: add content script with parsing pipeline"
```

---

### Task 10: Background Service Worker

**Files:**
- Create: `src/SmartTripPlanner.Extension/background.js`

- [ ] **Step 1: Implement background service worker**

Create `src/SmartTripPlanner.Extension/background.js`:

```javascript
const DEFAULT_API_URL = 'http://localhost:5000';

async function getApiUrl() {
  const result = await chrome.storage.sync.get(['apiUrl']);
  return result.apiUrl || DEFAULT_API_URL;
}

async function saveLocation(locationData) {
  const apiUrl = await getApiUrl();
  const response = await fetch(`${apiUrl}/api/locations`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(locationData)
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Unknown error' }));
    throw new Error(error.error || `API returned ${response.status}`);
  }

  return await response.json();
}

async function assignLocation(locationId, tripId) {
  const apiUrl = await getApiUrl();
  const response = await fetch(`${apiUrl}/api/locations/${locationId}/assign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tripId })
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Unknown error' }));
    throw new Error(error.error || `API returned ${response.status}`);
  }

  return await response.json();
}

async function getTrips() {
  const apiUrl = await getApiUrl();
  const response = await fetch(`${apiUrl}/api/trip`);
  if (!response.ok) throw new Error(`Failed to fetch trips: ${response.status}`);
  return await response.json();
}

// Handle messages from popup
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'saveLocation') {
    saveLocation(message.data)
      .then(result => sendResponse({ success: true, data: result }))
      .catch(err => sendResponse({ success: false, error: err.message }));
    return true;
  }

  if (message.action === 'saveAndAssign') {
    saveLocation(message.data)
      .then(saved => assignLocation(saved.id, message.tripId))
      .then(result => sendResponse({ success: true, data: result }))
      .catch(err => sendResponse({ success: false, error: err.message }));
    return true;
  }

  if (message.action === 'getTrips') {
    getTrips()
      .then(trips => sendResponse({ success: true, data: trips }))
      .catch(err => sendResponse({ success: false, error: err.message }));
    return true;
  }
});
```

- [ ] **Step 2: Commit**

```bash
git add src/SmartTripPlanner.Extension/background.js
git commit -m "feat: add background service worker for API communication"
```

---

### Task 11: Popup UI

**Files:**
- Create: `src/SmartTripPlanner.Extension/popup.html`
- Create: `src/SmartTripPlanner.Extension/popup.css`
- Create: `src/SmartTripPlanner.Extension/popup.js`

- [ ] **Step 1: Create popup.html**

Create `src/SmartTripPlanner.Extension/popup.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>SmartTripPlanner</title>
  <link rel="stylesheet" href="popup.css">
</head>
<body>
  <div id="app">
    <h1>SmartTripPlanner</h1>

    <div id="loading">
      <p id="loading-text">Scanning page...</p>
    </div>

    <div id="location-info" hidden>
      <div id="location-name" class="info-row"></div>
      <div id="location-address" class="info-row secondary"></div>
      <div id="location-category" class="info-row secondary"></div>

      <div class="actions">
        <button id="save-ideas" class="btn btn-primary">Save to Ideas</button>
        <div class="trip-row">
          <select id="trip-select" class="trip-dropdown">
            <option value="">Select a trip...</option>
          </select>
          <button id="save-trip" class="btn btn-secondary" disabled>Add to Trip</button>
        </div>
      </div>
    </div>

    <div id="no-location" hidden>
      <p>No location found on this page.</p>
    </div>

    <div id="status" hidden>
      <p id="status-text"></p>
    </div>

    <div id="error" hidden>
      <p id="error-text" class="error-text"></p>
    </div>
  </div>
  <script src="popup.js"></script>
</body>
</html>
```

- [ ] **Step 2: Create popup.css**

Create `src/SmartTripPlanner.Extension/popup.css`:

```css
* { box-sizing: border-box; margin: 0; padding: 0; }

body {
  width: 320px;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  font-size: 14px;
  color: #1a1a1a;
  background: #fff;
}

#app { padding: 16px; }

h1 {
  font-size: 16px;
  font-weight: 600;
  margin-bottom: 12px;
  color: #2563eb;
}

.info-row { margin-bottom: 4px; }
.info-row.secondary { font-size: 12px; color: #666; }

#location-name { font-weight: 600; font-size: 15px; }

.actions { margin-top: 16px; display: flex; flex-direction: column; gap: 8px; }

.btn {
  padding: 8px 16px;
  border: none;
  border-radius: 6px;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}

.btn-primary { background: #2563eb; color: #fff; }
.btn-primary:hover { background: #1d4ed8; }
.btn-secondary { background: #e5e7eb; color: #1a1a1a; }
.btn-secondary:hover { background: #d1d5db; }
.btn:disabled { opacity: 0.5; cursor: not-allowed; }

.trip-row { display: flex; gap: 8px; }

.trip-dropdown {
  flex: 1;
  padding: 8px;
  border: 1px solid #d1d5db;
  border-radius: 6px;
  font-size: 13px;
}

#loading { text-align: center; padding: 24px 0; color: #666; }
#status { text-align: center; padding: 12px 0; color: #16a34a; font-weight: 500; }
.error-text { color: #dc2626; font-size: 12px; }
```

- [ ] **Step 3: Create popup.js**

Create `src/SmartTripPlanner.Extension/popup.js`:

```javascript
const loadingEl = document.getElementById('loading');
const loadingText = document.getElementById('loading-text');
const locationInfoEl = document.getElementById('location-info');
const noLocationEl = document.getElementById('no-location');
const statusEl = document.getElementById('status');
const statusText = document.getElementById('status-text');
const errorEl = document.getElementById('error');
const errorText = document.getElementById('error-text');

const locationNameEl = document.getElementById('location-name');
const locationAddressEl = document.getElementById('location-address');
const locationCategoryEl = document.getElementById('location-category');
const saveIdeasBtn = document.getElementById('save-ideas');
const tripSelect = document.getElementById('trip-select');
const saveTripBtn = document.getElementById('save-trip');

let currentLocationData = null;

async function init() {
  try {
    // Inject parser scripts and content script into active tab
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });

    await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      files: [
        'parsers/structured.js',
        'parsers/google-maps.js',
        'parsers/yelp.js',
        'parsers/tripadvisor.js',
        'content.js'
      ]
    });

    // Ask content script to extract location
    const response = await chrome.tabs.sendMessage(tab.id, { action: 'extractLocation' });

    if (response.success) {
      showLocation(response.data, response.sourceUrl || tab.url);
    } else if (response.needsLlm) {
      loadingText.textContent = 'Analyzing page...';
      await handleLlmFallback(response.rawPageContent, response.sourceUrl || tab.url);
    } else {
      showNoLocation();
    }
  } catch (err) {
    showError('Failed to scan page: ' + err.message);
  }

  // Load trips for dropdown
  loadTrips();
}

function showLocation(data, sourceUrl) {
  currentLocationData = { ...data, sourceUrl };
  loadingEl.hidden = true;
  locationInfoEl.hidden = false;

  locationNameEl.textContent = data.name || 'Unknown';
  locationAddressEl.textContent = data.address || '';
  locationCategoryEl.textContent = data.category
    ? data.category.charAt(0).toUpperCase() + data.category.slice(1)
    : '';

  locationAddressEl.hidden = !data.address;
  locationCategoryEl.hidden = !data.category;
}

function showNoLocation() {
  loadingEl.hidden = true;
  noLocationEl.hidden = false;
}

function showStatus(msg) {
  statusEl.hidden = false;
  statusText.textContent = msg;
  setTimeout(() => window.close(), 2000);
}

function showError(msg) {
  loadingEl.hidden = true;
  errorEl.hidden = false;
  errorText.textContent = msg;
}

async function handleLlmFallback(rawPageContent, sourceUrl) {
  // Send to API for LLM extraction (saves and returns the location)
  const response = await chrome.runtime.sendMessage({
    action: 'saveLocation',
    data: { rawPageContent, sourceUrl }
  });

  if (response.success) {
    // Show extracted data so user can see what was found
    showLocation(response.data, sourceUrl);
    showStatus('Saved!');
  } else {
    showNoLocation();
    showError(response.error || 'Could not extract location');
  }
}

async function loadTrips() {
  const response = await chrome.runtime.sendMessage({ action: 'getTrips' });
  if (!response.success) return;

  const trips = response.data.filter(t => t.status !== 'completed');
  for (const trip of trips) {
    const option = document.createElement('option');
    option.value = trip.id;
    option.textContent = trip.destination;
    tripSelect.appendChild(option);
  }
}

tripSelect.addEventListener('change', () => {
  saveTripBtn.disabled = !tripSelect.value;
});

saveIdeasBtn.addEventListener('click', async () => {
  if (!currentLocationData) return;
  saveIdeasBtn.disabled = true;

  const response = await chrome.runtime.sendMessage({
    action: 'saveLocation',
    data: currentLocationData
  });

  if (response.success) {
    showStatus('Saved to Ideas!');
  } else {
    showError(response.error || 'Failed to save');
    saveIdeasBtn.disabled = false;
  }
});

saveTripBtn.addEventListener('click', async () => {
  if (!currentLocationData || !tripSelect.value) return;
  saveTripBtn.disabled = true;

  const response = await chrome.runtime.sendMessage({
    action: 'saveAndAssign',
    data: currentLocationData,
    tripId: parseInt(tripSelect.value)
  });

  if (response.success) {
    showStatus('Added to trip!');
  } else {
    showError(response.error || 'Failed to save');
    saveTripBtn.disabled = false;
  }
});

init();
```

- [ ] **Step 4: Commit**

```bash
git add src/SmartTripPlanner.Extension/popup.html src/SmartTripPlanner.Extension/popup.css src/SmartTripPlanner.Extension/popup.js
git commit -m "feat: add popup UI with save to ideas and add to trip actions"
```

---

### Task 12: Options Page

**Files:**
- Create: `src/SmartTripPlanner.Extension/options.html`
- Create: `src/SmartTripPlanner.Extension/options.js`

- [ ] **Step 1: Create options.html**

Create `src/SmartTripPlanner.Extension/options.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>SmartTripPlanner Settings</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; padding: 24px; max-width: 480px; }
    h1 { font-size: 18px; margin-bottom: 16px; }
    label { display: block; font-size: 14px; margin-bottom: 4px; font-weight: 500; }
    input { width: 100%; padding: 8px; border: 1px solid #d1d5db; border-radius: 6px; font-size: 14px; margin-bottom: 16px; }
    button { padding: 8px 20px; background: #2563eb; color: #fff; border: none; border-radius: 6px; cursor: pointer; font-size: 14px; }
    button:hover { background: #1d4ed8; }
    .saved { color: #16a34a; margin-left: 12px; font-size: 14px; }
  </style>
</head>
<body>
  <h1>SmartTripPlanner Settings</h1>
  <label for="api-url">API Base URL</label>
  <input type="text" id="api-url" placeholder="http://localhost:5000">
  <button id="save">Save</button>
  <span id="status" class="saved" hidden>Saved!</span>
  <script src="options.js"></script>
</body>
</html>
```

- [ ] **Step 2: Create options.js**

Create `src/SmartTripPlanner.Extension/options.js`:

```javascript
const apiUrlInput = document.getElementById('api-url');
const saveBtn = document.getElementById('save');
const statusEl = document.getElementById('status');

chrome.storage.sync.get(['apiUrl'], (result) => {
  apiUrlInput.value = result.apiUrl || 'http://localhost:5000';
});

saveBtn.addEventListener('click', () => {
  chrome.storage.sync.set({ apiUrl: apiUrlInput.value }, () => {
    statusEl.hidden = false;
    setTimeout(() => { statusEl.hidden = true; }, 2000);
  });
});
```

- [ ] **Step 3: Commit**

```bash
git add src/SmartTripPlanner.Extension/options.html src/SmartTripPlanner.Extension/options.js
git commit -m "feat: add extension options page for API URL configuration"
```

---

### Task 13: End-to-End Verification

- [ ] **Step 1: Build and run tests**

Run:
```bash
dotnet test SmartTripPlanner.sln --verbosity quiet
```
Expected: All tests pass

- [ ] **Step 2: Start the API**

Run:
```bash
dotnet run --project src/SmartTripPlanner.Api
```
Expected: API starts on configured port

- [ ] **Step 3: Test POST /api/locations with curl**

```bash
curl -X POST http://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"Test Cafe","address":"123 Main St","category":"cafe","sourceUrl":"https://example.com"}'
```
Expected: `201` with saved location JSON

- [ ] **Step 4: Test GET /api/locations?unassigned=true**

```bash
curl http://localhost:5000/api/locations?unassigned=true
```
Expected: Array containing the saved location

- [ ] **Step 5: Test LLM fallback with curl**

```bash
curl -X POST http://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"rawPageContent":"Welcome to The Blue Elephant Thai Restaurant located at 233 South St, Philadelphia. Best pad thai in the city."}'
```
Expected: `201` with LLM-extracted location JSON (name, address, category populated). If Ollama is not running, expect `500` — this confirms the LLM path is wired correctly.

- [ ] **Step 6: Load extension in Chrome**

1. Open `chrome://extensions/`
2. Enable "Developer mode"
3. Click "Load unpacked"
4. Select `src/SmartTripPlanner.Extension/`
5. Verify extension appears with no errors

- [ ] **Step 7: Test extension on a Yelp page**

1. Navigate to any Yelp business page
2. Click the extension icon
3. Verify popup shows extracted name and address
4. Click "Save to Ideas"
5. Verify success notification
6. Verify location appears in `GET /api/locations?unassigned=true`

- [ ] **Step 8: Final commit**

```bash
git add src/SmartTripPlanner.Extension/
git commit -m "feat: complete browser extension v1"
```
