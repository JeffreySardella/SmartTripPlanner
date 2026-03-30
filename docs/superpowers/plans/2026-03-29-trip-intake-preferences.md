# Trip Intake Flow & User Preferences Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a structured trip intake form, freeform-to-confirmation card flow, and implicit user preference learning to SmartTripPlanner.

**Architecture:** Prompt-driven approach — the intake form and preferences feed structured context into the existing agent loop via serialized text blocks. New tools (`confirm_trip`, `save_preference`, `delete_preference`, `get_user_choice_history`) are added to the LLM tool definitions. `AgentService.RunAsync` returns a typed `AgentResult` instead of `string` to distinguish confirmation cards from text responses. User preferences are stored in the existing `UserPreferences` SQLite table with a new `Source` column, dynamically injected into the system prompt before each request.

**Tech Stack:** .NET 8, ASP.NET Core, Blazor Server, EF Core + SQLite, xUnit + NSubstitute, Tailwind CSS

**Spec:** `docs/superpowers/specs/2026-03-29-trip-intake-preferences-design.md`

---

## File Structure

### New Files
- `src/SmartTripPlanner.Api/Models/AgentResult.cs` — Typed return from agent (text response vs. confirmation card)
- `src/SmartTripPlanner.Api/Models/TripConfirmation.cs` — Data model for the confirmation card fields
- `src/SmartTripPlanner.Tests/Services/PreferenceTests.cs` — Tests for preference CRUD and choice history
- `src/SmartTripPlanner.Tests/Services/AgentServiceConfirmTests.cs` — Tests for confirm_trip tool handling and AgentResult

### Modified Files
- `src/SmartTripPlanner.Api/Models/UserPreference.cs` — Add `Source` property
- `src/SmartTripPlanner.Api/Data/SmartTripPlannerDbContext.cs` — Configure `Source` column default
- `src/SmartTripPlanner.Api/Services/IPersistenceService.cs` — Add 4 new method signatures
- `src/SmartTripPlanner.Api/Services/PersistenceService.cs` — Implement preference CRUD + choice history
- `src/SmartTripPlanner.Api/Tools/ToolDefinitions.cs` — Add 4 new tool definitions
- `src/SmartTripPlanner.Api/Services/IAgentService.cs` — Change return type to `AgentResult`, add `AgentResultType` enum
- `src/SmartTripPlanner.Api/Services/AgentService.cs` — Handle new tools, inject preferences, return typed result
- `src/SmartTripPlanner.Api/Prompts/system-prompt.md` — Add Step 0, preference awareness, learning behavior
- `src/SmartTripPlanner.Api/Components/Pages/Home.razor` — Add guided form, confirmation card, dual entry points
- `src/SmartTripPlanner.Api/Components/Pages/Settings.razor` — Add "My Preferences" panel
- `src/SmartTripPlanner.Tests/Tools/ToolDefinitionsTests.cs` — Update tool count, add new tool name checks

---

## Chunk 1: Data Layer & Preference Persistence

### Task 1: Add Source property to UserPreference model

**Files:**
- Modify: `src/SmartTripPlanner.Api/Models/UserPreference.cs`

- [ ] **Step 1: Add Source property**

```csharp
// UserPreference.cs — full file
namespace SmartTripPlanner.Api.Models;

public class UserPreference
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string Source { get; set; } = "user";
}
```

- [ ] **Step 2: Update DbContext to configure Source default**

In `SmartTripPlannerDbContext.cs`, inside `OnModelCreating`, after the existing `UserPreference` index config (line 20-22), add:

```csharp
modelBuilder.Entity<UserPreference>()
    .Property(p => p.Source)
    .HasDefaultValue("user");
```

- [ ] **Step 3: Generate EF migration**

Run: `cd src/SmartTripPlanner.Api && dotnet ef migrations add AddUserPreferenceSource`

Expected: New migration file created in `Migrations/`

- [ ] **Step 4: Verify migration applies**

Run: `cd src/SmartTripPlanner.Api && dotnet ef database update`

Expected: Migration applied successfully

- [ ] **Step 5: Commit**

```bash
git add src/SmartTripPlanner.Api/Models/UserPreference.cs src/SmartTripPlanner.Api/Data/SmartTripPlannerDbContext.cs src/SmartTripPlanner.Api/Migrations/
git commit -m "feat: add Source column to UserPreference model"
```

---

### Task 2: Add preference CRUD methods to PersistenceService

**Files:**
- Modify: `src/SmartTripPlanner.Api/Services/IPersistenceService.cs`
- Modify: `src/SmartTripPlanner.Api/Services/PersistenceService.cs`
- Create: `src/SmartTripPlanner.Tests/Services/PreferenceTests.cs`

- [ ] **Step 1: Write failing tests for SavePreferenceAsync**

Create `src/SmartTripPlanner.Tests/Services/PreferenceTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --filter "FullyQualifiedName~PreferenceTests" --no-restore`

Expected: FAIL — methods don't exist yet

- [ ] **Step 3: Add method signatures to IPersistenceService**

In `IPersistenceService.cs`, add after line 15 (`DeleteTripEventAsync`):

```csharp
Task SavePreferenceAsync(string key, string value, string source);
Task<List<UserPreference>> GetPreferencesAsync();
Task<bool> DeletePreferenceAsync(string key);
Task<Dictionary<string, Dictionary<string, int>>> GetUserChoiceHistoryAsync();
```

- [ ] **Step 4: Implement methods in PersistenceService**

In `PersistenceService.cs`, add after the `DeleteTripEventAsync` method (after line 109):

```csharp
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

    // Aggregate pace from event density per trip
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

    // Aggregate time-of-day patterns
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
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --filter "FullyQualifiedName~PreferenceTests" --no-restore`

Expected: All 5 tests PASS

- [ ] **Step 6: Write test for GetUserChoiceHistoryAsync** (should have been in Step 1, but adding now)

Add to `PreferenceTests.cs`:

```csharp
[Fact]
public async Task GetUserChoiceHistoryAsync_AggregatesPaceFromTripDensity()
{
    // Create a "packed" trip: 7 events in 1 day
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
```

- [ ] **Step 7: Run full test suite to verify nothing broke**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --no-restore`

Expected: All tests PASS

- [ ] **Step 8: Commit**

```bash
git add src/SmartTripPlanner.Api/Services/IPersistenceService.cs src/SmartTripPlanner.Api/Services/PersistenceService.cs src/SmartTripPlanner.Tests/Services/PreferenceTests.cs
git commit -m "feat: add preference CRUD and choice history to PersistenceService"
```

---

## Chunk 2: Tool Definitions & Agent Result Type

### Task 3: Add new tool definitions

**Files:**
- Modify: `src/SmartTripPlanner.Api/Tools/ToolDefinitions.cs`
- Modify: `src/SmartTripPlanner.Tests/Tools/ToolDefinitionsTests.cs`

- [ ] **Step 1: Update test to expect 13 tools**

In `ToolDefinitionsTests.cs`, change line 8-12:

```csharp
[Fact]
public void GetAllTools_ReturnsThirteenTools()
{
    var tools = ToolDefinitions.GetAllTools();
    Assert.Equal(13, tools.Count);
}
```

Add new `[InlineData]` entries to the `[Theory]` test:

```csharp
[InlineData("confirm_trip")]
[InlineData("save_preference")]
[InlineData("delete_preference")]
[InlineData("get_user_choice_history")]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --filter "FullyQualifiedName~ToolDefinitionsTests" --no-restore`

Expected: FAIL — count is 9, new tool names not found

- [ ] **Step 3: Add 4 new tool definitions**

In `ToolDefinitions.cs`, add after the `search_hotels` tool (before the closing `];` on line 184):

```csharp
new LlmTool
{
    Function = new LlmFunction
    {
        Name = "confirm_trip",
        Description = "Present a parsed trip request to the user for confirmation before planning begins. Only call this for freeform text requests, not structured [TRIP REQUEST] blocks. The user will see an editable card with these fields and must confirm before you proceed.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                destination = new { type = "string", description = "Parsed destination city/region" },
                dates = new { type = "string", description = "Parsed date range (e.g. 'Apr 15-22, 2026') or null if ambiguous" },
                pace = new { type = "string", description = "relaxed, moderate, or packed — or null if not specified" },
                travelers = new { type = "integer", description = "Number of travelers (default 1)" },
                budget = new { type = "string", description = "Budget level or null" },
                interests = new { type = "array", items = new { type = "string" }, description = "List of interest categories" },
                dietary = new { type = "string", description = "Dietary restrictions or null" },
                accessibility = new { type = "string", description = "Accessibility needs or null" },
                must_see = new { type = "array", items = new { type = "string" }, description = "Must-see places" },
                avoid = new { type = "array", items = new { type = "string" }, description = "Things to avoid" }
            },
            required = new[] { "destination" }
        }
    }
},
new LlmTool
{
    Function = new LlmFunction
    {
        Name = "save_preference",
        Description = "Save or update a user preference for future trip planning. Uses upsert — if the key exists, updates value and source. Call with source 'learned' when you detect a pattern across 3+ trips. Call with source 'user' when the user explicitly states a preference.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                key = new { type = "string", description = "Preference category (e.g. pace, dietary, interests, morning_start)" },
                value = new { type = "string", description = "Preference value" },
                source = new { type = "string", description = "'learned' (inferred from patterns) or 'user' (explicitly stated)" }
            },
            required = new[] { "key", "value", "source" }
        }
    }
},
new LlmTool
{
    Function = new LlmFunction
    {
        Name = "delete_preference",
        Description = "Remove a saved user preference by key. Use when the user asks to forget a preference.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                key = new { type = "string", description = "Preference key to delete" }
            },
            required = new[] { "key" }
        }
    }
},
new LlmTool
{
    Function = new LlmFunction
    {
        Name = "get_user_choice_history",
        Description = "Retrieve aggregated history of the user's past trip choices to detect patterns. Returns counts per category (e.g. pace: packed x4, moderate x1). Call after completing a trip plan to check if any choices should be saved as learned preferences.",
        Parameters = new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --filter "FullyQualifiedName~ToolDefinitionsTests" --no-restore`

Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/SmartTripPlanner.Api/Tools/ToolDefinitions.cs src/SmartTripPlanner.Tests/Tools/ToolDefinitionsTests.cs
git commit -m "feat: add confirm_trip, save_preference, delete_preference, get_user_choice_history tools"
```

---

### Task 4: Create AgentResult/TripConfirmation models, update AgentService, and fix existing tests

This is a large task because the return type change (`string` → `AgentResult`) must be applied atomically — the project must compile and tests must pass at the end. Splitting this across commits would leave non-compiling intermediate states.

**Files:**
- Create: `src/SmartTripPlanner.Api/Models/AgentResult.cs`
- Create: `src/SmartTripPlanner.Api/Models/TripConfirmation.cs`
- Modify: `src/SmartTripPlanner.Api/Services/IAgentService.cs`
- Modify: `src/SmartTripPlanner.Api/Services/AgentService.cs`
- Modify: `src/SmartTripPlanner.Tests/Services/AgentServiceTests.cs`
- Modify: `src/SmartTripPlanner.Tests/Services/AgentServiceErrorTests.cs`
- Modify: `src/SmartTripPlanner.Tests/Services/AgentServicePersistenceTests.cs`
- Create: `src/SmartTripPlanner.Tests/Services/AgentServiceConfirmTests.cs`

- [ ] **Step 1: Create TripConfirmation model**

Create `src/SmartTripPlanner.Api/Models/TripConfirmation.cs`:

```csharp
namespace SmartTripPlanner.Api.Models;

public class TripConfirmation
{
    public string Destination { get; set; } = "";
    public string? Dates { get; set; }
    public string? Pace { get; set; }
    public int Travelers { get; set; } = 1;
    public string? Budget { get; set; }
    public List<string> Interests { get; set; } = [];
    public string? Dietary { get; set; }
    public string? Accessibility { get; set; }
    public List<string> MustSee { get; set; } = [];
    public List<string> Avoid { get; set; } = [];
}
```

- [ ] **Step 2: Create AgentResult model**

Create `src/SmartTripPlanner.Api/Models/AgentResult.cs`:

```csharp
namespace SmartTripPlanner.Api.Models;

public enum AgentResultType
{
    TextResponse,
    TripConfirmation
}

public class AgentResult
{
    public AgentResultType Type { get; init; }
    public string? TextContent { get; init; }
    public TripConfirmation? Confirmation { get; init; }

    public static AgentResult Text(string content) => new()
    {
        Type = AgentResultType.TextResponse,
        TextContent = content
    };

    public static AgentResult Confirm(TripConfirmation confirmation) => new()
    {
        Type = AgentResultType.TripConfirmation,
        Confirmation = confirmation
    };
}
```

- [ ] **Step 3: Update IAgentService to return AgentResult**

Replace `IAgentService.cs` fully:

```csharp
namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public interface IAgentService
{
    Task<AgentResult> RunAsync(string userRequest, int maxIterations = 10);
    Task<AgentResult> RunAsync(string userRequest, Action<AgentProgress> onProgress, int maxIterations = 10);
}

public class AgentProgress
{
    public int Iteration { get; set; }
    public int MaxIterations { get; set; }
    public string Status { get; set; } = "";
    public string? ToolName { get; set; }
    public double ElapsedSec { get; set; }
}
```

- [ ] **Step 4: Update AgentService.RunAsync to return AgentResult**

In `AgentService.cs`, make the following changes:

Change `RunAsync` signatures (lines 76-77):
```csharp
public Task<AgentResult> RunAsync(string userRequest, int maxIterations = 10)
    => RunAsync(userRequest, _ => { }, maxIterations);
```

Change `RunAsync` with progress (line 79):
```csharp
public async Task<AgentResult> RunAsync(string userRequest, Action<AgentProgress> onProgress, int maxIterations = 10)
```

Add preference injection at line 82, before building messages list:
```csharp
var preferences = await persistenceService.GetPreferencesAsync();
var promptWithPrefs = SystemPrompt;
if (preferences.Count > 0)
{
    var prefsBlock = string.Join("\n", preferences.Select(p =>
        $"{p.Key}: {p.Value} ({(p.Source == "user" ? "set by you" : "learned")})"));
    promptWithPrefs += $"\n\n[USER PREFERENCES]\n{prefsBlock}";
}

var messages = new List<LlmMessage>
{
    new() { Role = "system", Content = promptWithPrefs },
    new() { Role = "user", Content = userRequest }
};
```

Change the "no tool calls" return (line 122):
```csharp
return AgentResult.Text(message.Content ?? string.Empty);
```

Change the max iterations return (line 174):
```csharp
return AgentResult.Text("Agent reached max iterations without completing. Please try a more specific request.");
```

Change the LLM unavailable return (line 109):
```csharp
return AgentResult.Text($"LLM service is unavailable: {ex.Message}");
```

- [ ] **Step 5: Handle confirm_trip tool — return early with AgentResult.Confirm**

In the `foreach (var toolCall in message.ToolCalls)` loop (around line 129), add a special case before the existing `ExecuteToolAsync` call:

```csharp
// Special handling: confirm_trip returns immediately to the UI
if (toolCall.Function.Name == "confirm_trip")
{
    onProgress(new AgentProgress
    {
        Iteration = i + 1, MaxIterations = maxIterations,
        Status = "Awaiting confirmation", ToolName = "confirm_trip",
        ElapsedSec = sw.Elapsed.TotalSeconds
    });

    var args = toolCall.Function.Arguments;
    var confirmation = new TripConfirmation
    {
        Destination = args["destination"].ToString()!,
        Dates = args.GetStringOrDefault("dates"),
        Pace = args.GetStringOrDefault("pace"),
        Travelers = args.GetIntOrDefault("travelers", 1),
        Budget = args.GetStringOrDefault("budget"),
        Dietary = args.GetStringOrDefault("dietary"),
        Accessibility = args.GetStringOrDefault("accessibility"),
    };

    if (args.TryGetValue("interests", out var interestsVal) && interestsVal is JsonElement je && je.ValueKind == JsonValueKind.Array)
        confirmation.Interests = je.EnumerateArray().Select(e => e.GetString()!).ToList();

    if (args.TryGetValue("must_see", out var mustSeeVal) && mustSeeVal is JsonElement ms && ms.ValueKind == JsonValueKind.Array)
        confirmation.MustSee = ms.EnumerateArray().Select(e => e.GetString()!).ToList();

    if (args.TryGetValue("avoid", out var avoidVal) && avoidVal is JsonElement av && av.ValueKind == JsonValueKind.Array)
        confirmation.Avoid = av.EnumerateArray().Select(e => e.GetString()!).ToList();

    return AgentResult.Confirm(confirmation);
}
```

- [ ] **Step 6: Add new tool cases to ExecuteToolAsync**

In `ExecuteToolAsync` (the `switch` expression), add before the `_ =>` default case:

```csharp
"save_preference" => await SavePreferenceAsync(args),

"delete_preference" => await DeletePreferenceAsync(args),

"get_user_choice_history" => await persistenceService.GetUserChoiceHistoryAsync(),
```

Add the helper methods after `GetTripAsync`:

```csharp
private async Task<object> SavePreferenceAsync(Dictionary<string, object> args)
{
    var key = args["key"].ToString()!;
    var value = args["value"].ToString()!;
    var source = args.GetStringOrDefault("source") ?? "learned";

    await persistenceService.SavePreferenceAsync(key, value, source);
    return new { success = true, key, value, source };
}

private async Task<object> DeletePreferenceAsync(Dictionary<string, object> args)
{
    var key = args["key"].ToString()!;
    var deleted = await persistenceService.DeletePreferenceAsync(key);
    return deleted
        ? new { success = true, message = $"Preference '{key}' deleted" }
        : (object)new { success = false, message = $"Preference '{key}' not found" };
}
```

Note: the existing `DeleteTripEventAsync` private method name conflicts. The new preference delete helper should be named `DeletePreferenceToolAsync` instead:

```csharp
"delete_preference" => await DeletePreferenceToolAsync(args),
```

```csharp
private async Task<object> DeletePreferenceToolAsync(Dictionary<string, object> args)
{
    var key = args["key"].ToString()!;
    var deleted = await persistenceService.DeletePreferenceAsync(key);
    return deleted
        ? new { success = true, message = $"Preference '{key}' deleted" }
        : (object)new { success = false, message = $"Preference '{key}' not found" };
}
```

- [ ] **Step 7: Add friendly names for progress display**

In the `friendlyName` switch (line 131-143), add:

```csharp
"confirm_trip" => "Parsing your request",
"save_preference" => "Saving preference",
"delete_preference" => "Removing preference",
"get_user_choice_history" => "Reviewing your travel patterns",
```

- [ ] **Step 8: Update existing tests to use AgentResult**

The existing test files assert `string result = await _sut.RunAsync(...)` which no longer compiles. Update each file:

**In `AgentServiceTests.cs`**, update every `var result = await _sut.RunAsync(...)` assertion:

- `RunAsync_DirectTextResponse_ReturnsContent` (line 37-39):
  ```csharp
  var result = await _sut.RunAsync("Plan a trip");
  Assert.Equal("Here is your plan.", result.TextContent);
  ```

- `RunAsync_ToolCallThenTextResponse_ExecutesToolAndReturns` (line 80-82):
  ```csharp
  var result = await _sut.RunAsync("Am I free March 10-12?");
  Assert.Equal("You're free all day!", result.TextContent);
  ```

- `RunAsync_MaxIterationsReached_ReturnsWarning` (line 108-110):
  ```csharp
  var result = await _sut.RunAsync("Plan Tokyo trip", maxIterations: 3);
  Assert.Contains("max iterations", result.TextContent!, StringComparison.OrdinalIgnoreCase);
  ```

- `RunAsync_SearchAreaWithCachedResults_ReturnsCachedLocations` (line 150-152):
  ```csharp
  var result = await _sut.RunAsync("What's in Tokyo?");
  Assert.Equal("Found Tokyo Tower!", result.TextContent);
  ```

**In `AgentServiceErrorTests.cs`**, same pattern:

- `RunAsync_ToolThrows_FeedsErrorBackToOllama` (line 62-64):
  ```csharp
  var result = await _sut.RunAsync("Check my calendar");
  Assert.Equal("Sorry, calendar is unavailable.", result.TextContent);
  ```

- `RunAsync_LlmUnavailable_ReturnsUserFriendlyMessage` (line 73-76):
  ```csharp
  var result = await _sut.RunAsync("Plan a trip");
  Assert.Contains("LLM", result.TextContent!);
  Assert.Contains("unavailable", result.TextContent!, StringComparison.OrdinalIgnoreCase);
  ```

- `RunAsync_BadToolArguments_FeedsParseErrorToOllama` (line 110-112):
  ```csharp
  var result = await _sut.RunAsync("Check calendar");
  Assert.Equal("Let me try valid dates.", result.TextContent);
  ```

**In `AgentServicePersistenceTests.cs`**, update all `string result` to use `.TextContent`:
  Find all `Assert.Contains("...", result)` and change to `Assert.Contains("...", result.TextContent!)`.

- [ ] **Step 9: Write new tests for confirm_trip and save_preference**

Create `src/SmartTripPlanner.Tests/Services/AgentServiceConfirmTests.cs`:

```csharp
namespace SmartTripPlanner.Tests.Services;

using NSubstitute;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using SmartTripPlanner.Api.Tools;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public class AgentServiceConfirmTests
{
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();
    private readonly ICalendarService _calendar = Substitute.For<ICalendarService>();
    private readonly ITravelService _travel = Substitute.For<ITravelService>();
    private readonly IPersistenceService _persistence = Substitute.For<IPersistenceService>();
    private readonly AgentService _sut;

    public AgentServiceConfirmTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        var env = Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        env.ContentRootPath.Returns(Path.GetTempPath());
        var weather = new WeatherService(Substitute.For<IHttpClientFactory>(), Substitute.For<ILogger<WeatherService>>());
        var poi = new PoiService(Substitute.For<IHttpClientFactory>(), Substitute.For<ILogger<PoiService>>());
        _sut = new AgentService(_llm, _calendar, _travel, _persistence, weather, poi, env, logger);
    }

    [Fact]
    public async Task RunAsync_ConfirmTripTool_ReturnsConfirmationResult()
    {
        var toolCallResponse = new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls =
                [
                    new LlmToolCall
                    {
                        Id = "call_1",
                        Function = new LlmFunctionCall
                        {
                            Name = "confirm_trip",
                            Arguments = new Dictionary<string, object>
                            {
                                ["destination"] = JsonSerializer.SerializeToElement("Tokyo"),
                                ["pace"] = JsonSerializer.SerializeToElement("packed"),
                                ["travelers"] = JsonSerializer.SerializeToElement(2)
                            }
                        }
                    }
                ]
            }
        };

        _llm.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(toolCallResponse);

        var result = await _sut.RunAsync("plan me a trip to Tokyo");

        Assert.Equal(AgentResultType.TripConfirmation, result.Type);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("Tokyo", result.Confirmation!.Destination);
        Assert.Equal("packed", result.Confirmation.Pace);
        Assert.Equal(2, result.Confirmation.Travelers);
    }

    [Fact]
    public async Task RunAsync_SavePreferenceTool_CallsPersistenceService()
    {
        var toolCallResponse = new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls =
                [
                    new LlmToolCall
                    {
                        Id = "call_1",
                        Function = new LlmFunctionCall
                        {
                            Name = "save_preference",
                            Arguments = new Dictionary<string, object>
                            {
                                ["key"] = JsonSerializer.SerializeToElement("pace"),
                                ["value"] = JsonSerializer.SerializeToElement("packed"),
                                ["source"] = JsonSerializer.SerializeToElement("learned")
                            }
                        }
                    }
                ]
            }
        };

        var finalResponse = new LlmChatResponse
        {
            Message = new LlmMessage { Role = "assistant", Content = "Preference saved." }
        };

        _llm.ChatAsync(Arg.Any<List<LlmMessage>>(), Arg.Any<List<LlmTool>?>())
            .Returns(toolCallResponse, finalResponse);

        await _sut.RunAsync("I always prefer packed trips");

        await _persistence.Received(1).SavePreferenceAsync("pace", "packed", "learned");
    }
}
```

- [ ] **Step 10: Run full test suite**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --no-restore`

Expected: All tests PASS (existing tests updated, new tests pass)

- [ ] **Step 11: Commit**

```bash
git add src/SmartTripPlanner.Api/Models/AgentResult.cs src/SmartTripPlanner.Api/Models/TripConfirmation.cs src/SmartTripPlanner.Api/Services/IAgentService.cs src/SmartTripPlanner.Api/Services/AgentService.cs src/SmartTripPlanner.Tests/Services/AgentServiceTests.cs src/SmartTripPlanner.Tests/Services/AgentServiceErrorTests.cs src/SmartTripPlanner.Tests/Services/AgentServicePersistenceTests.cs src/SmartTripPlanner.Tests/Services/AgentServiceConfirmTests.cs
git commit -m "feat: AgentService returns AgentResult, handles new tools, injects preferences"
```

---

## Chunk 4: System Prompt Updates

### Task 6: Update system prompt

**Files:**
- Modify: `src/SmartTripPlanner.Api/Prompts/system-prompt.md`

- [ ] **Step 1: Add Step 0 before existing Step 1 in Agent Workflow section**

Find the line `1. **Parse request**` (line 126) and add BEFORE it:

```markdown
0. **Confirm freeform requests** - If the user's message is freeform text (NOT a structured `[TRIP REQUEST]` block), call the `confirm_trip` tool with your parsed understanding. Extract destination, dates, pace, budget, interests, dietary needs, accessibility requirements, must-see spots, and things to avoid. Leave fields null if not mentioned or ambiguous. Do NOT proceed to step 1 until the user confirms. If the request IS a `[TRIP REQUEST]` block, skip directly to step 1.
```

- [ ] **Step 2: Add preference awareness section**

After the `## Agent Workflow` section's closing (after line 138, "The agent iterates steps 4-6..."), add:

```markdown
## User Preferences

A `[USER PREFERENCES]` block will be included in your context when available. These are the user's saved preferences — use them as defaults when the request doesn't specify a value. For example, if the user's learned pace is "packed" and the request doesn't mention pace, use packed. Explicit request values ALWAYS override preferences.

You can manage preferences conversationally:
- If the user asks to forget a preference, call `delete_preference` with the key.
- If the user explicitly states a new preference (e.g., "I'm vegetarian"), call `save_preference` with source "user".
```

- [ ] **Step 3: Add learning behavior after the final workflow step**

After the preference section, add:

```markdown
## Learning from Patterns

After completing a trip plan, call `get_user_choice_history` to review the user's past trip patterns. If any choice appears in 3 or more trips (e.g., pace "packed" used 3+ times), call `save_preference` with source "learned" to remember it. Do NOT save one-off choices — only consistent patterns across multiple trips.
```

- [ ] **Step 4: Commit**

```bash
git add src/SmartTripPlanner.Api/Prompts/system-prompt.md
git commit -m "feat: update system prompt with intake confirmation, preferences, and learning behavior"
```

---

## Chunk 5: UI — Home Page (Guided Form + Confirmation Card)

### Task 7: Update Home.razor with guided form and confirmation card

**Files:**
- Modify: `src/SmartTripPlanner.Api/Components/Pages/Home.razor`

- [ ] **Step 1: Update the code section to handle AgentResult and form state**

Replace the entire `@code` block (lines 111-243) with:

```csharp
@code {
    private readonly List<ChatMessage> _messages = [];
    private string _input = string.Empty;
    private bool _isLoading;
    private double _elapsedSec;
    private Timer? _timer;

    // Guided form state
    private bool _showGuidedForm;
    private string _formDestination = "";
    private DateTime? _formStartDate;
    private DateTime? _formEndDate;
    private string _formPace = "moderate";
    private int _formTravelers = 1;
    private string _formBudget = "";
    private HashSet<string> _formInterests = [];
    private string _formDietary = "";
    private string _formAccessibility = "";
    private string _formMustSee = "";
    private string _formAvoid = "";

    // Confirmation card state
    private TripConfirmation? _pendingConfirmation;
    private bool _editingConfirmation;

    private static readonly string[] AvailableInterests =
        ["Food", "Culture", "Nightlife", "Outdoors", "Shopping", "History", "Art", "Adventure", "Wellness"];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JS.InvokeVoidAsync("eval", "if ('Notification' in window && Notification.permission === 'default') Notification.requestPermission()");
        }
    }

    private async Task FillAndSend(string text)
    {
        _input = text;
        await SendMessage();
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(_input)) return;

        var userMessage = _input.Trim();
        _input = string.Empty;
        _messages.Add(new ChatMessage("user", userMessage));
        _isLoading = true;
        _elapsedSec = 0;
        _pendingConfirmation = null;
        StateHasChanged();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _timer = new Timer(_ =>
        {
            _elapsedSec = sw.Elapsed.TotalSeconds;
            InvokeAsync(StateHasChanged);
        }, null, 1000, 1000);

        try
        {
            var result = await AgentService.RunAsync(userMessage);

            if (result.Type == AgentResultType.TripConfirmation && result.Confirmation is not null)
            {
                _pendingConfirmation = result.Confirmation;
                _editingConfirmation = false;
            }
            else
            {
                _messages.Add(new ChatMessage("assistant", result.TextContent ?? ""));
                await JS.InvokeVoidAsync("eval",
                    "if ('Notification' in window && Notification.permission === 'granted' && document.hidden) " +
                    "new Notification('SmartTripPlanner', { body: 'Your trip itinerary is ready!' })");
            }
        }
        catch (Exception ex)
        {
            _messages.Add(new ChatMessage("assistant", $"Something went wrong: {ex.Message}"));
        }
        finally
        {
            _timer?.Dispose();
            _timer = null;
            _isLoading = false;
        }
    }

    private async Task SubmitGuidedForm()
    {
        if (string.IsNullOrWhiteSpace(_formDestination)) return;

        var lines = new List<string> { "[TRIP REQUEST]" };
        lines.Add($"Destination: {_formDestination}");
        if (_formStartDate.HasValue && _formEndDate.HasValue)
            lines.Add($"Dates: {_formStartDate.Value:MMM d} - {_formEndDate.Value:MMM d, yyyy}");
        lines.Add($"Pace: {_formPace}");
        lines.Add($"Travelers: {_formTravelers}");
        if (!string.IsNullOrWhiteSpace(_formBudget)) lines.Add($"Budget: {_formBudget}");
        if (_formInterests.Count > 0) lines.Add($"Interests: {string.Join(", ", _formInterests)}");
        if (!string.IsNullOrWhiteSpace(_formDietary)) lines.Add($"Dietary: {_formDietary}");
        if (!string.IsNullOrWhiteSpace(_formAccessibility)) lines.Add($"Accessibility: {_formAccessibility}");
        if (!string.IsNullOrWhiteSpace(_formMustSee)) lines.Add($"Must-see: {_formMustSee}");
        if (!string.IsNullOrWhiteSpace(_formAvoid)) lines.Add($"Avoid: {_formAvoid}");

        _input = string.Join("\n", lines);
        _showGuidedForm = false;
        await SendMessage();
    }

    private async Task ConfirmTrip()
    {
        if (_pendingConfirmation is null) return;

        var c = _pendingConfirmation;
        var lines = new List<string> { "[TRIP REQUEST]" };
        lines.Add($"Destination: {c.Destination}");
        if (!string.IsNullOrWhiteSpace(c.Dates)) lines.Add($"Dates: {c.Dates}");
        if (!string.IsNullOrWhiteSpace(c.Pace)) lines.Add($"Pace: {c.Pace}");
        lines.Add($"Travelers: {c.Travelers}");
        if (!string.IsNullOrWhiteSpace(c.Budget)) lines.Add($"Budget: {c.Budget}");
        if (c.Interests.Count > 0) lines.Add($"Interests: {string.Join(", ", c.Interests)}");
        if (!string.IsNullOrWhiteSpace(c.Dietary)) lines.Add($"Dietary: {c.Dietary}");
        if (!string.IsNullOrWhiteSpace(c.Accessibility)) lines.Add($"Accessibility: {c.Accessibility}");
        if (c.MustSee.Count > 0) lines.Add($"Must-see: {string.Join(", ", c.MustSee)}");
        if (c.Avoid.Count > 0) lines.Add($"Avoid: {string.Join(", ", c.Avoid)}");

        _input = string.Join("\n", lines);
        _pendingConfirmation = null;
        await SendMessage();
    }

    private void ToggleInterest(string interest)
    {
        if (!_formInterests.Remove(interest))
            _formInterests.Add(interest);
    }

    private static string FormatElapsed(double sec)
    {
        if (sec < 60) return $"{(int)sec}s";
        return $"{(int)(sec / 60)}m {(int)(sec % 60)}s";
    }

    private static string FormatContent(string content)
    {
        var lines = content.Split('\n');
        var result = new System.Text.StringBuilder();
        var inTable = false;
        var headerDone = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith('|') && line.EndsWith('|'))
            {
                if (line.Replace("|", "").Replace("-", "").Replace(" ", "").Length == 0)
                { headerDone = true; continue; }

                if (!inTable)
                { result.Append("<table class='w-full mt-4 mb-4 text-sm border-collapse rounded-xl overflow-hidden'>"); inTable = true; headerDone = false; }

                var cells = line.Split('|', StringSplitOptions.TrimEntries).Where(c => c.Length > 0).ToArray();
                if (!headerDone)
                {
                    result.Append("<thead><tr class='bg-surface-container-highest'>");
                    foreach (var cell in cells)
                        result.Append($"<th class='p-3 text-left font-bold text-[10px] uppercase tracking-widest text-primary'>{FormatInline(cell)}</th>");
                    result.Append("</tr></thead><tbody class='bg-surface-container-low/50'>");
                    headerDone = true;
                }
                else
                {
                    result.Append("<tr class='border-b border-outline-variant/10'>");
                    foreach (var cell in cells)
                        result.Append($"<td class='p-3'>{FormatInline(cell)}</td>");
                    result.Append("</tr>");
                }
                continue;
            }

            if (inTable) { result.Append("</tbody></table>"); inTable = false; headerDone = false; }

            if (string.IsNullOrWhiteSpace(line)) { result.Append("<br/>"); continue; }
            if (line.StartsWith("### ")) { result.Append($"<h4 class='text-base font-bold text-tertiary-fixed tracking-tight mt-4 mb-1'>{FormatInline(line[4..])}</h4>"); continue; }
            if (line.StartsWith("## ")) { result.Append($"<h3 class='text-xl font-bold text-tertiary-fixed tracking-tight mt-4 mb-2'>{FormatInline(line[3..])}</h3>"); continue; }
            if (line.StartsWith("# ")) { result.Append($"<h2 class='text-2xl font-black text-on-surface tracking-tight mt-4 mb-2'>{FormatInline(line[2..])}</h2>"); continue; }
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                result.Append($"<div class='flex gap-2 my-1'><span class='text-tertiary mt-1'>•</span><span>{FormatInline(line[2..])}</span></div>");
                continue;
            }
            if (line.StartsWith("---")) { result.Append("<hr class='border-outline-variant/20 my-3'/>"); continue; }

            result.Append($"<p class='my-1'>{FormatInline(line)}</p>");
        }

        if (inTable) result.Append("</tbody></table>");
        return result.ToString();
    }

    private static string FormatInline(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong class='text-on-surface font-semibold'>$1</strong>");
        return text;
    }

    private record ChatMessage(string Role, string Content);
}
```

- [ ] **Step 2: Update the hero section to include guided form toggle**

Note: `@using SmartTripPlanner.Api.Models` and `@using SmartTripPlanner.Api.Services` are already in `_Imports.razor`, so no additional `@using` is needed.

Replace the quick-start buttons section (lines 19-41) — add a "Guided Planning" button after the existing 3 quick-start buttons:

```razor
<div class="flex flex-wrap justify-center gap-4">
    <button @onclick='() => FillAndSend("Plan a 3-day trip to Tokyo. I like temples, ramen, and nightlife. Packed pace.")'
            class="group relative flex items-center gap-4 bg-surface-container-high p-2 pr-6 rounded-full hover:bg-surface-container-highest transition-all duration-300 border border-outline-variant/10">
        <div class="w-10 h-10 rounded-full bg-gradient-to-br from-secondary to-error flex items-center justify-center">
            <span class="material-symbols-outlined text-white text-lg">temple_buddhist</span>
        </div>
        <span class="text-sm font-medium">3 days in Tokyo — temples & ramen</span>
    </button>
    <button @onclick='() => FillAndSend("Plan a weekend trip to Portland, OR. I like craft beer, hiking, and food carts. Relaxed pace.")'
            class="group relative flex items-center gap-4 bg-surface-container-high p-2 pr-6 rounded-full hover:bg-surface-container-highest transition-all duration-300 border border-outline-variant/10">
        <div class="w-10 h-10 rounded-full bg-gradient-to-br from-tertiary-dim to-tertiary flex items-center justify-center">
            <span class="material-symbols-outlined text-on-tertiary text-lg">forest</span>
        </div>
        <span class="text-sm font-medium">Weekend in Portland — beer & hiking</span>
    </button>
    <button @onclick='() => FillAndSend("Plan a 2-day trip to Austin, TX. Focus on BBQ, live music, and Barton Springs. Moderate pace.")'
            class="group relative flex items-center gap-4 bg-surface-container-high p-2 pr-6 rounded-full hover:bg-surface-container-highest transition-all duration-300 border border-outline-variant/10">
        <div class="w-10 h-10 rounded-full bg-gradient-to-br from-primary to-primary-dim flex items-center justify-center">
            <span class="material-symbols-outlined text-white text-lg">music_note</span>
        </div>
        <span class="text-sm font-medium">2 days in Austin — BBQ & live music</span>
    </button>
</div>
<div class="mt-6">
    <button @onclick="() => _showGuidedForm = !_showGuidedForm"
            class="flex items-center gap-2 mx-auto px-6 py-3 bg-surface-container-high rounded-full border border-primary/20 hover:bg-surface-container-highest transition-all text-sm font-medium text-primary">
        <span class="material-symbols-outlined text-lg">checklist</span>
        @(_showGuidedForm ? "Hide Guided Planning" : "Guided Planning")
    </button>
</div>
```

- [ ] **Step 3: Add guided form panel**

After the guided planning button div, add the form (still inside the hero `@if (_messages.Count == 0)` block):

```razor
@if (_showGuidedForm)
{
    <div class="w-full max-w-2xl mx-auto bg-surface-container rounded-xl p-8 border border-outline-variant/10 space-y-6 mt-4">
        <h3 class="text-lg font-bold tracking-tight text-on-surface">Plan Your Trip</h3>

        <div class="grid grid-cols-2 gap-4">
            <div class="col-span-2">
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Destination *</label>
                <input @bind="_formDestination" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" placeholder="e.g. Tokyo, Japan" />
            </div>
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Start Date *</label>
                <input type="date" @bind="_formStartDate" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" />
            </div>
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">End Date *</label>
                <input type="date" @bind="_formEndDate" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" />
            </div>
        </div>

        <div>
            <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-2 block">Pace *</label>
            <div class="flex gap-2">
                @foreach (var pace in new[] { "relaxed", "moderate", "packed" })
                {
                    <button @onclick="() => _formPace = pace"
                            class="px-4 py-2 rounded-full text-xs font-bold uppercase tracking-widest transition-all
                                   @(_formPace == pace ? "bg-primary text-on-primary" : "bg-surface-container-low text-on-surface-variant border border-outline-variant/10 hover:bg-surface-container-highest")">
                        @pace
                    </button>
                }
            </div>
        </div>

        <div class="grid grid-cols-2 gap-4">
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Travelers</label>
                <div class="flex items-center gap-3">
                    <button @onclick="() => { if (_formTravelers > 1) _formTravelers--; }"
                            class="w-8 h-8 rounded-full bg-surface-container-low border border-outline-variant/10 flex items-center justify-center hover:bg-surface-container-highest">−</button>
                    <span class="text-lg font-bold w-8 text-center">@_formTravelers</span>
                    <button @onclick="() => _formTravelers++"
                            class="w-8 h-8 rounded-full bg-surface-container-low border border-outline-variant/10 flex items-center justify-center hover:bg-surface-container-highest">+</button>
                </div>
            </div>
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Budget</label>
                <select @bind="_formBudget" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary">
                    <option value="">No preference</option>
                    <option value="Budget">Budget</option>
                    <option value="Mid-range">Mid-range</option>
                    <option value="Luxury">Luxury</option>
                </select>
            </div>
        </div>

        <div>
            <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-2 block">Interests</label>
            <div class="flex flex-wrap gap-2">
                @foreach (var interest in AvailableInterests)
                {
                    <button @onclick="() => ToggleInterest(interest)"
                            class="px-3 py-1.5 rounded-full text-xs font-medium transition-all
                                   @(_formInterests.Contains(interest) ? "bg-secondary text-on-secondary" : "bg-surface-container-low text-on-surface-variant border border-outline-variant/10 hover:bg-surface-container-highest")">
                        @interest
                    </button>
                }
            </div>
        </div>

        <div class="grid grid-cols-2 gap-4">
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Dietary Restrictions</label>
                <input @bind="_formDietary" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" placeholder="e.g. Vegetarian, Halal" />
            </div>
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Accessibility Needs</label>
                <input @bind="_formAccessibility" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" placeholder="e.g. Wheelchair accessible" />
            </div>
        </div>

        <div class="grid grid-cols-2 gap-4">
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Must-See</label>
                <input @bind="_formMustSee" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" placeholder="e.g. Shibuya, Tsukiji" />
            </div>
            <div>
                <label class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mb-1 block">Avoid</label>
                <input @bind="_formAvoid" class="w-full bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary" placeholder="e.g. Tourist traps, malls" />
            </div>
        </div>

        <button @onclick="SubmitGuidedForm"
                disabled="@(string.IsNullOrWhiteSpace(_formDestination) || !_formStartDate.HasValue || !_formEndDate.HasValue)"
                class="w-full bg-gradient-to-r from-primary to-primary-dim text-on-primary py-3 rounded-full font-bold text-xs uppercase tracking-[0.2em] hover:brightness-110 active:scale-[0.98] transition-all disabled:opacity-30">
            Plan This Trip
        </button>
    </div>
}
```

- [ ] **Step 4: Add confirmation card rendering**

After the loading indicator block (after line 87, the closing `</div>` of `@if (_isLoading)`), add:

```razor
@if (_pendingConfirmation is not null)
{
    <div class="flex justify-start gap-4">
        <div class="w-10 h-10 rounded-full bg-surface-container-highest flex items-center justify-center shrink-0 border border-primary/20">
            <span class="material-symbols-outlined text-primary-fixed-dim text-xl" style="font-variation-settings: 'FILL' 1;">smart_toy</span>
        </div>
        <div class="bg-surface-container/40 p-6 rounded-2xl rounded-tl-none border border-primary/20 max-w-2xl w-full">
            <p class="text-xs text-on-surface-variant mb-4">Here's what I understood from your request. Confirm or edit before I start planning:</p>

            <div class="space-y-3">
                <div class="flex justify-between items-center">
                    <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant">Destination</span>
                    <span class="text-sm font-medium text-on-surface">@_pendingConfirmation.Destination</span>
                </div>
                <div class="flex justify-between items-center">
                    <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant">Dates</span>
                    <span class="text-sm text-on-surface">@(_pendingConfirmation.Dates ?? "Not specified")</span>
                </div>
                <div class="flex justify-between items-center">
                    <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant">Pace</span>
                    <span class="text-sm text-on-surface">@(_pendingConfirmation.Pace ?? "Not specified")</span>
                </div>
                <div class="flex justify-between items-center">
                    <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant">Travelers</span>
                    <span class="text-sm text-on-surface">@_pendingConfirmation.Travelers</span>
                </div>
                @if (!string.IsNullOrWhiteSpace(_pendingConfirmation.Budget))
                {
                    <div class="flex justify-between items-center">
                        <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant">Budget</span>
                        <span class="text-sm text-on-surface">@_pendingConfirmation.Budget</span>
                    </div>
                }
                @if (_pendingConfirmation.Interests.Count > 0)
                {
                    <div class="flex justify-between items-start">
                        <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mt-1">Interests</span>
                        <div class="flex flex-wrap gap-1 justify-end">
                            @foreach (var interest in _pendingConfirmation.Interests)
                            {
                                <span class="px-2 py-0.5 bg-secondary/20 text-secondary text-[10px] font-bold rounded-full">@interest</span>
                            }
                        </div>
                    </div>
                }
                @if (!string.IsNullOrWhiteSpace(_pendingConfirmation.Dietary))
                {
                    <div class="flex justify-between items-center">
                        <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant">Dietary</span>
                        <span class="text-sm text-on-surface">@_pendingConfirmation.Dietary</span>
                    </div>
                }
                @if (_pendingConfirmation.MustSee.Count > 0)
                {
                    <div class="flex justify-between items-start">
                        <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mt-1">Must-See</span>
                        <span class="text-sm text-on-surface text-right">@string.Join(", ", _pendingConfirmation.MustSee)</span>
                    </div>
                }
                @if (_pendingConfirmation.Avoid.Count > 0)
                {
                    <div class="flex justify-between items-start">
                        <span class="text-[10px] font-bold uppercase tracking-widest text-on-surface-variant mt-1">Avoid</span>
                        <span class="text-sm text-on-surface text-right">@string.Join(", ", _pendingConfirmation.Avoid)</span>
                    </div>
                }
            </div>

            <div class="flex gap-3 mt-6">
                <button @onclick="ConfirmTrip"
                        class="flex-1 bg-gradient-to-r from-primary to-primary-dim text-on-primary py-2.5 rounded-full font-bold text-xs uppercase tracking-widest hover:brightness-110 transition-all">
                    Looks good, plan it
                </button>
                <button @onclick="() => _pendingConfirmation = null"
                        class="px-6 py-2.5 border border-outline-variant/20 rounded-full text-xs font-bold uppercase tracking-widest text-on-surface-variant hover:bg-surface-container-highest transition-all">
                    Cancel
                </button>
            </div>
        </div>
    </div>
}
```

- [ ] **Step 5: Verify the project builds**

Run: `cd src/SmartTripPlanner.Api && dotnet build --no-restore`

Expected: Build succeeds

- [ ] **Step 6: Run full test suite**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --no-restore`

Expected: All tests PASS

- [ ] **Step 7: Commit**

```bash
git add src/SmartTripPlanner.Api/Components/Pages/Home.razor
git commit -m "feat: add guided planning form and confirmation card to Home page"
```

---

## Chunk 6: UI — Settings Preferences Panel

### Task 8: Add "My Preferences" panel to Settings

**Files:**
- Modify: `src/SmartTripPlanner.Api/Components/Pages/Settings.razor`

- [ ] **Step 1: Add IPersistenceService injection**

At the top of `Settings.razor`, add after line 4 (`@inject IConfiguration Config`):

```razor
@inject IPersistenceService PersistenceService
```

- [ ] **Step 2: Add preferences state variables**

In the `@code` block, add after the calendar state variables (after line 334, `_confirmDeleteId`):

```csharp
// Preferences
private List<UserPreference> _preferences = [];
private string _newPrefKey = "";
private string _newPrefValue = "";
private int? _editingPrefId;
private string _editPrefValue = "";
```

- [ ] **Step 3: Load preferences in OnInitializedAsync**

In `OnInitializedAsync`, add after the calendar loading (after line 350):

```csharp
_preferences = await PersistenceService.GetPreferencesAsync();
```

- [ ] **Step 4: Add the "My Preferences" panel markup**

After the Google Calendar section's closing `</section>` (line 313), add a new section inside the grid:

```razor
@* ── My Preferences ── *@
<section class="col-span-12 bg-surface-container rounded-xl p-8 border border-outline-variant/5">
    <div class="flex justify-between items-center mb-6">
        <div class="flex items-center gap-3">
            <div class="w-10 h-10 rounded-full bg-surface-container-highest flex items-center justify-center">
                <span class="material-symbols-outlined text-primary">tune</span>
            </div>
            <div>
                <h3 class="text-lg font-bold tracking-tight">My Preferences</h3>
                <p class="text-xs text-on-surface-variant">Your travel preferences — learned from usage or set by you</p>
            </div>
        </div>
    </div>

    @if (_preferences.Count > 0)
    {
        <div class="space-y-3 mb-8">
            @foreach (var pref in _preferences)
            {
                <div class="flex items-center justify-between p-4 bg-surface-container-low rounded-lg border border-outline-variant/5">
                    <div class="flex items-center gap-4">
                        <div>
                            <p class="text-sm font-bold tracking-tight capitalize">@pref.Key.Replace("_", " ")</p>
                            @if (_editingPrefId == pref.Id)
                            {
                                <input @bind="_editPrefValue" @bind:event="oninput"
                                       class="mt-1 bg-surface-container border border-outline-variant/10 rounded px-2 py-1 text-sm text-on-surface focus:ring-0 focus:border-primary" />
                            }
                            else
                            {
                                <p class="text-sm text-on-surface-variant">@pref.Value</p>
                            }
                        </div>
                    </div>
                    <div class="flex items-center gap-3">
                        <span class="text-[10px] font-bold uppercase tracking-widest px-2 py-0.5 rounded-full
                               @(pref.Source == "user" ? "bg-primary/10 text-primary" : "bg-secondary/10 text-secondary")">
                            @(pref.Source == "user" ? "Set by you" : "Learned")
                        </span>
                        @if (_editingPrefId == pref.Id)
                        {
                            <button @onclick="() => SaveEditedPreference(pref)"
                                    class="material-symbols-outlined text-tertiary text-lg hover:scale-110 transition-transform">check</button>
                            <button @onclick="() => _editingPrefId = null"
                                    class="material-symbols-outlined text-outline text-lg hover:scale-110 transition-transform">close</button>
                        }
                        else
                        {
                            <button @onclick="() => { _editingPrefId = pref.Id; _editPrefValue = pref.Value; }"
                                    class="material-symbols-outlined text-on-surface-variant text-lg hover:text-primary transition-colors">edit</button>
                            <button @onclick="() => DeletePreference(pref.Key)"
                                    class="material-symbols-outlined text-on-surface-variant text-lg hover:text-error transition-colors">delete</button>
                        }
                    </div>
                </div>
            }
        </div>
    }
    else
    {
        <p class="text-sm text-on-surface-variant italic mb-8">No preferences saved yet. The AI will learn your patterns over time, or you can add them manually below.</p>
    }

    <div class="flex gap-2">
        <input @bind="_newPrefKey" @bind:event="oninput"
               class="flex-1 bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary placeholder:text-outline"
               placeholder="Preference (e.g. dietary)" />
        <input @bind="_newPrefValue" @bind:event="oninput"
               class="flex-1 bg-surface-container-low border border-outline-variant/10 rounded-lg px-3 py-2 text-sm text-on-surface focus:ring-0 focus:border-primary placeholder:text-outline"
               placeholder="Value (e.g. vegetarian)" />
        <button @onclick="AddPreference"
                disabled="@(string.IsNullOrWhiteSpace(_newPrefKey) || string.IsNullOrWhiteSpace(_newPrefValue))"
                class="px-6 py-2 bg-primary text-on-primary rounded-lg font-bold text-xs uppercase tracking-widest hover:brightness-110 transition-all disabled:opacity-30">
            Add
        </button>
    </div>
</section>
```

- [ ] **Step 5: Add preferences methods to the @code block**

Add after the `FormatMb` method (after line 419):

```csharp
private async Task AddPreference()
{
    if (string.IsNullOrWhiteSpace(_newPrefKey) || string.IsNullOrWhiteSpace(_newPrefValue)) return;
    await PersistenceService.SavePreferenceAsync(_newPrefKey.Trim().ToLowerInvariant().Replace(" ", "_"), _newPrefValue.Trim(), "user");
    _preferences = await PersistenceService.GetPreferencesAsync();
    _newPrefKey = ""; _newPrefValue = "";
}

private async Task DeletePreference(string key)
{
    await PersistenceService.DeletePreferenceAsync(key);
    _preferences = await PersistenceService.GetPreferencesAsync();
}

private async Task SaveEditedPreference(UserPreference pref)
{
    if (string.IsNullOrWhiteSpace(_editPrefValue)) return;
    await PersistenceService.SavePreferenceAsync(pref.Key, _editPrefValue.Trim(), "user");
    _preferences = await PersistenceService.GetPreferencesAsync();
    _editingPrefId = null;
}
```

- [ ] **Step 6: Verify build**

Note: `@using SmartTripPlanner.Api.Models` and `@using SmartTripPlanner.Api.Services` are already in `_Imports.razor`.

Run: `cd src/SmartTripPlanner.Api && dotnet build --no-restore`

Expected: Build succeeds

- [ ] **Step 7: Run full test suite**

Run: `cd src/SmartTripPlanner.Tests && dotnet test --no-restore`

Expected: All tests PASS

- [ ] **Step 8: Commit**

```bash
git add src/SmartTripPlanner.Api/Components/Pages/Settings.razor
git commit -m "feat: add My Preferences panel to Settings page"
```

---

## Chunk 7: Final Verification

### Task 9: Full build and test verification

- [ ] **Step 1: Clean build the entire solution**

Run: `cd C:/Users/Jeff/Documents/Github_new/SmartTripPlanner && dotnet build`

Expected: Build succeeds with no errors

- [ ] **Step 2: Run all tests**

Run: `cd C:/Users/Jeff/Documents/Github_new/SmartTripPlanner && dotnet test`

Expected: All tests PASS

- [ ] **Step 3: Commit any fixups**

If any fixes were needed, commit them:

```bash
git add -A
git commit -m "fix: resolve build/test issues from trip intake feature"
```

- [ ] **Step 4: Final summary commit (if needed)**

Only if there are uncommitted changes from fixing issues across tasks.
