# CachedLocation Service Design

**Goal:** Make the `search_area` tool functional by adding cache-first location queries with LLM fallback. Locations are persisted to SQLite via the existing `CachedLocations` table and reused across trips.

**Approach:** Extend `IPersistenceService` with 3 new methods. Update the `search_area` tool handler in AgentService to query the cache. Add optional lat/lng to `add_trip_event` so locations are cached when events are created.

---

## Architecture

```
search_area tool call
        │
        ▼
SearchCachedLocationsAsync(area, category)
        │
   ┌────┴────┐
   │ hits?   │
   ▼         ▼
 Return    Return hint:
 cached    "No cached data,
 results   suggest with coords"
              │
              ▼
         LLM generates
         suggestions
              │
              ▼
         add_trip_event
         (with lat/lng)
              │
              ▼
         CacheLocationsAsync
         persists to SQLite
```

## Persistence Layer

Add 3 methods to `IPersistenceService`:

### SearchCachedLocationsAsync
- Parameters: `string area`, `string? category`
- Query: `WHERE Name LIKE '%area%' AND LastUpdated > UtcNow - 30 days`
- Optional category filter: `AND Category == category`
- Returns: `List<CachedLocation>` ordered by Name

### CacheLocationsAsync
- Parameters: `List<CachedLocation> locations`
- Upsert logic: if a location with same Name+Category exists, update coordinates and LastUpdated. Otherwise insert.

### GetCachedLocationByNameAsync
- Parameters: `string name`
- Returns: single `CachedLocation?` for coordinate lookups

## AgentService Changes

### search_area handler (replace no-op)
1. Extract `area`, `category`, `limit` from tool args
2. Call `SearchCachedLocationsAsync(area, category)`
3. Apply `limit` (default 5)
4. If results: return `{ cached: true, locations: [...] }`
5. If empty: return `{ cached: false, area, category, hint: "No cached locations found. Please suggest places with their coordinates." }`

### add_trip_event handler (extend)
- Parse optional `latitude` and `longitude` from tool args (default 0)
- Pass to existing `AddTripEventAsync` (already accepts lat/lng)
- After creating the trip event, call `CacheLocationsAsync` with the location name, coordinates, and a "general" category

## Tool Definition Changes

### add_trip_event
Add two optional parameters:
```json
"latitude": { "type": "number", "description": "Location latitude (optional)" }
"longitude": { "type": "number", "description": "Location longitude (optional)" }
```
Keep `required` array unchanged (summary, location, start, end).

## Cache Expiry

- 30-day TTL: `SearchCachedLocationsAsync` filters `WHERE LastUpdated > UtcNow.AddDays(-30)`
- Stale entries remain in DB but are invisible to queries
- Re-caching an existing location updates `LastUpdated`, resetting the 30-day window

## Testing

### PersistenceService tests (4 new)
- `SearchCachedLocationsAsync_MatchingArea_ReturnsResults`
- `SearchCachedLocationsAsync_StaleEntries_ExcludesOlderThan30Days`
- `SearchCachedLocationsAsync_WithCategory_FiltersCorrectly`
- `CacheLocationsAsync_NewLocations_PersistsToDb`

### AgentService test (1 new)
- `ExecuteToolAsync_SearchArea_ReturnsCachedLocations`

All tests use in-memory SQLite and follow existing `MethodName_Scenario_ExpectedResult` naming.

## Files Modified

| File | Change |
|------|--------|
| `Services/IPersistenceService.cs` | Add 3 method signatures |
| `Services/PersistenceService.cs` | Implement 3 methods |
| `Services/AgentService.cs` | Replace search_area no-op, extend add_trip_event |
| `Tools/ToolDefinitions.cs` | Add lat/lng to add_trip_event |
| `Tests/Services/PersistenceServiceTests.cs` | Add 4 cache tests |
| `Tests/Services/AgentServiceTests.cs` | Add 1 search_area test |
