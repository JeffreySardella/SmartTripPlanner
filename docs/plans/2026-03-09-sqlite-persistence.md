# SQLite Persistence Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Persist trips and trip events to SQLite so agent work survives restarts, and expose REST endpoints to query trip history.

**Architecture:** IPersistenceService wraps EF Core CRUD. AgentService saves trip events to DB when `add_trip_event` fires. TripController gets new GET endpoints for listing/retrieving trips.

**Tech Stack:** .NET 8, EF Core + SQLite, xUnit, in-memory SQLite for tests

---

## Task 0: PersistenceService

**Files:**
- Create: `src/AetherPlan.Api/Services/IPersistenceService.cs`
- Create: `src/AetherPlan.Api/Services/PersistenceService.cs`
- Create: `src/AetherPlan.Tests/Services/PersistenceServiceTests.cs`
- Modify: `src/AetherPlan.Api/Program.cs`

**Step 1: Write the failing tests**

```csharp
// src/AetherPlan.Tests/Services/PersistenceServiceTests.cs
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
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~PersistenceServiceTests" -v minimal
```

Expected: FAIL — `PersistenceService` does not exist.

**Step 3: Create the interface**

```csharp
// src/AetherPlan.Api/Services/IPersistenceService.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface IPersistenceService
{
    Task<Trip> CreateTripAsync(string destination, DateTime startDate, DateTime endDate);
    Task<TripEvent> AddTripEventAsync(int tripId, string summary, string location,
        double latitude, double longitude, DateTime start, DateTime end, string? calendarEventId);
    Task<List<Trip>> GetTripsAsync();
    Task<Trip?> GetTripByIdAsync(int id);
}
```

**Step 4: Implement PersistenceService**

```csharp
// src/AetherPlan.Api/Services/PersistenceService.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Data;
using AetherPlan.Api.Models;
using Microsoft.EntityFrameworkCore;

public class PersistenceService(AetherPlanDbContext db) : IPersistenceService
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
}
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~PersistenceServiceTests" -v minimal
```

Expected: All 5 tests PASS.

**Step 6: Register in DI**

Add to Program.cs before `var app = builder.Build();`:
```csharp
builder.Services.AddScoped<IPersistenceService, PersistenceService>();
```

**Step 7: Commit**

```bash
git add src/AetherPlan.Api/Services/IPersistenceService.cs src/AetherPlan.Api/Services/PersistenceService.cs src/AetherPlan.Tests/Services/PersistenceServiceTests.cs src/AetherPlan.Api/Program.cs
git commit -m "feat: add PersistenceService for trip CRUD with SQLite"
```

---

## Task 1: Wire Persistence into AgentService

**Files:**
- Modify: `src/AetherPlan.Api/Services/AgentService.cs`
- Modify: `src/AetherPlan.Api/Services/IAgentService.cs`
- Create: `src/AetherPlan.Tests/Services/AgentServicePersistenceTests.cs`

**Step 1: Write the failing test**

```csharp
// src/AetherPlan.Tests/Services/AgentServicePersistenceTests.cs
namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using NSubstitute;
using Microsoft.Extensions.Logging;

public class AgentServicePersistenceTests
{
    [Fact]
    public async Task RunAsync_AddTripEventToolCall_PersistsToDatabase()
    {
        var ollamaClient = Substitute.For<IOllamaClient>();
        var calendarService = Substitute.For<ICalendarService>();
        var travelService = Substitute.For<ITravelService>();
        var persistenceService = Substitute.For<IPersistenceService>();
        var logger = Substitute.For<ILogger<AgentService>>();

        var sut = new AgentService(ollamaClient, calendarService, travelService, persistenceService, logger);

        // First call: Ollama wants to add a trip event
        // Second call: Ollama returns final text
        ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
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
                                Name = "add_trip_event",
                                Arguments = new Dictionary<string, object>
                                {
                                    ["summary"] = "Visit Eiffel Tower",
                                    ["location"] = "Champ de Mars, Paris",
                                    ["start"] = "2026-03-15T10:00:00",
                                    ["end"] = "2026-03-15T12:00:00"
                                }
                            }
                        }]
                    },
                    Done = true
                },
                new OllamaChatResponse
                {
                    Message = new OllamaMessage { Role = "assistant", Content = "Event added!" },
                    Done = true
                });

        calendarService.CreateEventAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string?>())
            .Returns("cal-event-123");

        persistenceService.CreateTripAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Trip { Id = 1, Destination = "Paris" });

        persistenceService.AddTripEventAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string?>())
            .Returns(new TripEvent { Id = 1, Summary = "Visit Eiffel Tower", Location = "Champ de Mars, Paris" });

        var result = await sut.RunAsync("Plan a day in Paris");

        Assert.Equal("Event added!", result);
        await persistenceService.Received(1).AddTripEventAsync(
            Arg.Any<int>(), Arg.Is("Visit Eiffel Tower"), Arg.Is("Champ de Mars, Paris"),
            Arg.Any<double>(), Arg.Any<double>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Is("cal-event-123"));
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~AgentServicePersistenceTests" -v minimal
```

Expected: FAIL — AgentService constructor doesn't accept IPersistenceService yet.

**Step 3: Update AgentService**

Add `IPersistenceService persistenceService` to the primary constructor:

```csharp
public class AgentService(
    IOllamaClient ollamaClient,
    ICalendarService calendarService,
    ITravelService travelService,
    IPersistenceService persistenceService,
    ILogger<AgentService> logger) : IAgentService
```

Update the `add_trip_event` case in `ExecuteToolAsync` to also persist:

```csharp
"add_trip_event" => await AddTripEventWithPersistence(args),
```

Add a new private method:

```csharp
private async Task<object> AddTripEventWithPersistence(Dictionary<string, object> args)
{
    var summary = args["summary"].ToString()!;
    var location = args["location"].ToString()!;
    var start = DateTime.Parse(args["start"].ToString()!);
    var end = DateTime.Parse(args["end"].ToString()!);
    var description = args.TryGetValue("description", out var desc) ? desc.ToString() : null;

    // Create calendar event
    var calendarEventId = await calendarService.CreateEventAsync(summary, location, start, end, description);

    // Ensure a trip exists (create one if this is the first event)
    var trips = await persistenceService.GetTripsAsync();
    var trip = trips.FirstOrDefault(t => t.Status == "draft")
        ?? await persistenceService.CreateTripAsync(location, start, end);

    // Persist the event
    await persistenceService.AddTripEventAsync(trip.Id, summary, location, 0, 0, start, end, calendarEventId);

    return new { calendarEventId, summary, location, start, end };
}
```

**Step 4: Update existing AgentServiceTests and AgentServiceErrorTests**

The AgentService constructor now has 5 parameters. Update the existing test constructors to include a mocked `IPersistenceService`:

In `AgentServiceTests`:
```csharp
private readonly IPersistenceService _persistenceService = Substitute.For<IPersistenceService>();
// ...
_sut = new AgentService(_ollamaClient, _calendarService, _travelService, _persistenceService, logger);
```

Same for `AgentServiceErrorTests`.

**Step 5: Run all tests**

```bash
dotnet test AetherPlan.sln -v minimal
```

Expected: All tests pass (existing + 1 new).

**Step 6: Commit**

```bash
git add src/AetherPlan.Api/Services/AgentService.cs src/AetherPlan.Tests/Services/AgentServicePersistenceTests.cs src/AetherPlan.Tests/Services/AgentServiceTests.cs src/AetherPlan.Tests/Services/AgentServiceErrorTests.cs
git commit -m "feat: wire PersistenceService into AgentService for trip event saving"
```

---

## Task 2: Trip History Endpoints

**Files:**
- Modify: `src/AetherPlan.Api/Controllers/TripController.cs`
- Modify: `src/AetherPlan.Tests/Controllers/TripControllerTests.cs`

**Step 1: Write the failing tests**

Add to the existing `TripControllerTests.cs`:

```csharp
private readonly IPersistenceService _persistenceService = Substitute.For<IPersistenceService>();

// Update constructor:
public TripControllerTests()
{
    var logger = Substitute.For<ILogger<TripController>>();
    _sut = new TripController(_agentService, _persistenceService, logger);
}

[Fact]
public async Task GetTrips_ReturnsList()
{
    _persistenceService.GetTripsAsync().Returns(new List<Trip>
    {
        new() { Id = 1, Destination = "Tokyo" },
        new() { Id = 2, Destination = "Paris" }
    });

    var result = await _sut.GetTrips();

    var ok = Assert.IsType<OkObjectResult>(result);
    Assert.Equal(200, ok.StatusCode);
}

[Fact]
public async Task GetTrip_Found_ReturnsTrip()
{
    _persistenceService.GetTripByIdAsync(1).Returns(new Trip
    {
        Id = 1, Destination = "Tokyo", Events = [
            new TripEvent { Id = 1, Summary = "Temple Visit", Location = "Asakusa" }
        ]
    });

    var result = await _sut.GetTrip(1);

    var ok = Assert.IsType<OkObjectResult>(result);
    Assert.Equal(200, ok.StatusCode);
}

[Fact]
public async Task GetTrip_NotFound_Returns404()
{
    _persistenceService.GetTripByIdAsync(999).Returns((Trip?)null);

    var result = await _sut.GetTrip(999);

    Assert.IsType<NotFoundResult>(result);
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~TripControllerTests" -v minimal
```

**Step 3: Update TripController**

Add `IPersistenceService` to the constructor and new endpoints:

```csharp
[ApiController]
[Route("api/[controller]")]
public class TripController(IAgentService agentService, IPersistenceService persistenceService, ILogger<TripController> logger) : ControllerBase
{
    [HttpPost("plan")]
    public async Task<IActionResult> PlanTrip([FromBody] TripRequest request)
    {
        try
        {
            var result = await agentService.RunAsync(request.Prompt);
            return Ok(new { response = result });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process trip request");
            return StatusCode(500, new { error = "An internal error occurred. Please try again." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTrips()
    {
        var trips = await persistenceService.GetTripsAsync();
        return Ok(trips);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTrip(int id)
    {
        var trip = await persistenceService.GetTripByIdAsync(id);
        if (trip is null) return NotFound();
        return Ok(trip);
    }
}
```

**Step 4: Run tests**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~TripControllerTests" -v minimal
```

Expected: All 5 tests PASS (2 existing + 3 new).

**Step 5: Run all tests**

```bash
dotnet test AetherPlan.sln -v minimal
```

**Step 6: Commit**

```bash
git add src/AetherPlan.Api/Controllers/TripController.cs src/AetherPlan.Tests/Controllers/TripControllerTests.cs
git commit -m "feat: add GET /api/trip and GET /api/trip/{id} endpoints for trip history"
```

---

## Summary

| Task | Component | New Tests |
|------|-----------|-----------|
| 0 | PersistenceService CRUD | 5 |
| 1 | Wire into AgentService | 1 |
| 2 | Trip history endpoints | 3 |
