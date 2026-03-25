# CachedLocation Service Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `search_area` tool functional with cache-first location queries (30-day TTL) backed by SQLite, with LLM fallback when cache is empty.

**Architecture:** Extend `IPersistenceService` with 3 new methods for cached location CRUD. Update `AgentService` to query the cache on `search_area` calls and cache locations when `add_trip_event` is called with coordinates. Add optional lat/lng parameters to the `add_trip_event` tool definition.

**Tech Stack:** .NET 8, EF Core (SQLite), xUnit, NSubstitute

**Spec:** `docs/superpowers/specs/2026-03-10-cached-location-service-design.md`

---

## Task 0: Add Persistence Methods to IPersistenceService

**Files:**
- Modify: `src/SmartTripPlanner.Api/Services/IPersistenceService.cs`
- Modify: `src/SmartTripPlanner.Api/Services/PersistenceService.cs`
- Test: `src/SmartTripPlanner.Tests/Services/PersistenceServiceTests.cs`

### Step 1: Write the 4 failing tests

Add the following 4 tests to the bottom of `src/SmartTripPlanner.Tests/Services/PersistenceServiceTests.cs`, inside the `PersistenceServiceTests` class:

```csharp
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
```

Note: `PersistenceServiceTests` already imports `Microsoft.EntityFrameworkCore`, so `FirstOrDefaultAsync` will resolve.

### Step 2: Run tests to verify they fail

```bash
dotnet test SmartTripPlanner.sln --filter "PersistenceServiceTests" -v minimal
```

Expected: FAIL — `SearchCachedLocationsAsync` and `CacheLocationsAsync` do not exist on `PersistenceService`.

### Step 3: Add method signatures to IPersistenceService

Add the following 3 method signatures to the bottom of `src/SmartTripPlanner.Api/Services/IPersistenceService.cs`, inside the interface, before the closing brace:

```csharp
Task<List<CachedLocation>> SearchCachedLocationsAsync(string area, string? category = null);
Task CacheLocationsAsync(List<CachedLocation> locations);
Task<CachedLocation?> GetCachedLocationByNameAsync(string name);
```

Also add the `CachedLocation` model import if not present. The file already has `using SmartTripPlanner.Api.Models;` so no new using needed.

### Step 4: Implement the 3 methods in PersistenceService

Add the following 3 methods to the bottom of `src/SmartTripPlanner.Api/Services/PersistenceService.cs`, inside the `PersistenceService` class, before the closing brace:

```csharp
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
```

### Step 5: Run the 4 new tests

```bash
dotnet test SmartTripPlanner.sln --filter "PersistenceServiceTests" -v minimal
```

Expected: 9 tests passed (5 existing + 4 new), 0 failed.

### Step 6: Run all tests for regression check

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: 41 tests passed (37 existing + 4 new), 0 failed.

### Step 7: Commit

```bash
git add src/SmartTripPlanner.Api/Services/IPersistenceService.cs src/SmartTripPlanner.Api/Services/PersistenceService.cs src/SmartTripPlanner.Tests/Services/PersistenceServiceTests.cs
git commit -m "feat: add cached location persistence methods with 30-day TTL"
```

---

## Task 1: Add lat/lng to add_trip_event Tool Definition

**Files:**
- Modify: `src/SmartTripPlanner.Api/Tools/ToolDefinitions.cs`

### Step 1: Add latitude and longitude properties to add_trip_event

In `src/SmartTripPlanner.Api/Tools/ToolDefinitions.cs`, find the `add_trip_event` tool's `properties` block. After the `description` property, add:

```csharp
latitude = new { type = "number", description = "Location latitude (optional)" },
longitude = new { type = "number", description = "Location longitude (optional)" }
```

The `required` array stays unchanged: `new[] { "summary", "location", "start", "end" }`.

### Step 2: Verify build

```bash
dotnet build SmartTripPlanner.sln
```

Expected: Build succeeded, 0 errors.

### Step 3: Run all tests

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: 41 tests passed, 0 failed. (The `ToolDefinitionsTests` check tool count and names, not properties, so they still pass.)

### Step 4: Commit

```bash
git add src/SmartTripPlanner.Api/Tools/ToolDefinitions.cs
git commit -m "feat: add optional lat/lng parameters to add_trip_event tool"
```

---

## Task 2: Update AgentService search_area and add_trip_event Handlers

**Files:**
- Modify: `src/SmartTripPlanner.Api/Services/AgentService.cs`
- Test: `src/SmartTripPlanner.Tests/Services/AgentServiceTests.cs`

### Step 1: Write the failing test

Add the following test to the bottom of `src/SmartTripPlanner.Tests/Services/AgentServiceTests.cs`, inside the `AgentServiceTests` class:

```csharp
[Fact]
public async Task RunAsync_SearchAreaWithCachedResults_ReturnsCachedLocations()
{
    var cachedLocations = new List<CachedLocation>
    {
        new() { Id = 1, Name = "Tokyo Tower", Latitude = 35.6586, Longitude = 139.7454, Category = "attractions" }
    };

    _persistenceService.SearchCachedLocationsAsync("Tokyo", null)
        .Returns(cachedLocations);

    // First call: Ollama calls search_area
    // Second call: Ollama returns text with the results
    _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
        .Returns(
            new OllamaChatResponse
            {
                Message = new OllamaMessage
                {
                    Role = "assistant",
                    ToolCalls = [new OllamaToolCall
                    {
                        Function = new OllamaFunctionCall
                        {
                            Name = "search_area",
                            Arguments = new Dictionary<string, object> { ["area"] = "Tokyo" }
                        }
                    }]
                },
                Done = true
            },
            new OllamaChatResponse
            {
                Message = new OllamaMessage { Role = "assistant", Content = "Found Tokyo Tower!" },
                Done = true
            });

    var result = await _sut.RunAsync("What's in Tokyo?");

    Assert.Equal("Found Tokyo Tower!", result);
    await _persistenceService.Received(1).SearchCachedLocationsAsync("Tokyo", null);
}
```

Add `using SmartTripPlanner.Api.Models;` to the top of the test file if not already present.

### Step 2: Run test to verify it fails

```bash
dotnet test SmartTripPlanner.sln --filter "RunAsync_SearchAreaWithCachedResults_ReturnsCachedLocations" -v minimal
```

Expected: FAIL — the current `search_area` handler doesn't call `SearchCachedLocationsAsync`.

### Step 3: Replace the search_area handler in AgentService

In `src/SmartTripPlanner.Api/Services/AgentService.cs`, find the `search_area` case in the `ExecuteToolAsync` method (around line 104). Replace:

```csharp
"search_area" => new { note = "search_area uses LLM internal knowledge, no external call needed",
                       area = args["area"].ToString() },
```

With:

```csharp
"search_area" => await SearchAreaAsync(args),
```

Then add this new method to the `AgentService` class, after `AddTripEventWithPersistence`:

```csharp
private async Task<object> SearchAreaAsync(Dictionary<string, object> args)
{
    var area = args["area"].ToString()!;
    var category = args.TryGetValue("category", out var cat) ? cat.ToString() : null;
    var limit = args.TryGetValue("limit", out var lim) ? Convert.ToInt32(lim) : 5;

    var cached = await persistenceService.SearchCachedLocationsAsync(area, category);

    if (cached.Count > 0)
    {
        var results = cached.Take(limit).Select(l => new
        {
            l.Name, l.Latitude, l.Longitude, l.Category
        });
        return new { cached = true, locations = results };
    }

    return new
    {
        cached = false, area, category,
        hint = "No cached locations found. Please suggest places with their coordinates."
    };
}
```

### Step 4: Update the add_trip_event handler to parse lat/lng and cache

In `src/SmartTripPlanner.Api/Services/AgentService.cs`, find the `AddTripEventWithPersistence` method. Replace it entirely with:

```csharp
private async Task<object> AddTripEventWithPersistence(Dictionary<string, object> args)
{
    var summary = args["summary"].ToString()!;
    var location = args["location"].ToString()!;
    var start = DateTime.Parse(args["start"].ToString()!);
    var end = DateTime.Parse(args["end"].ToString()!);
    var description = args.TryGetValue("description", out var desc) ? desc.ToString() : null;
    var latitude = args.TryGetValue("latitude", out var lat) ? Convert.ToDouble(lat) : 0.0;
    var longitude = args.TryGetValue("longitude", out var lng) ? Convert.ToDouble(lng) : 0.0;

    var calendarEventId = await calendarService.CreateEventAsync(summary, location, start, end, description);

    var trips = await persistenceService.GetTripsAsync();
    var trip = trips.FirstOrDefault(t => t.Status == "draft")
        ?? await persistenceService.CreateTripAsync(location, start, end);

    await persistenceService.AddTripEventAsync(trip.Id, summary, location, latitude, longitude, start, end, calendarEventId);

    if (latitude != 0.0 || longitude != 0.0)
    {
        await persistenceService.CacheLocationsAsync([
            new CachedLocation
            {
                Name = location,
                Latitude = latitude,
                Longitude = longitude,
                Category = "general"
            }
        ]);
    }

    return new { calendarEventId, summary, location, start, end, latitude, longitude };
}
```

### Step 5: Run the new test

```bash
dotnet test SmartTripPlanner.sln --filter "RunAsync_SearchAreaWithCachedResults_ReturnsCachedLocations" -v minimal
```

Expected: 1 test passed.

### Step 6: Run all tests for regression check

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: 42 tests passed (37 original + 4 persistence + 1 agent), 0 failed.

### Step 7: Commit

```bash
git add src/SmartTripPlanner.Api/Services/AgentService.cs src/SmartTripPlanner.Tests/Services/AgentServiceTests.cs
git commit -m "feat: implement cache-first search_area and location caching in add_trip_event"
```

---

## Summary

| Task | Component | Description |
|------|-----------|-------------|
| 0 | PersistenceService | Add SearchCachedLocationsAsync, CacheLocationsAsync, GetCachedLocationByNameAsync + 4 tests |
| 1 | ToolDefinitions | Add optional lat/lng to add_trip_event tool |
| 2 | AgentService | Replace search_area no-op with cache query, extend add_trip_event to cache locations + 1 test |
