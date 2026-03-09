# AetherPlan Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a local smart trip planner that uses Ollama (Qwen3.5) with tool-calling to create validated travel itineraries and push them to Google Calendar.

**Architecture:** ASP.NET Core Web API hosts an agent loop that sends user requests to Ollama with tool definitions. Ollama responds with tool calls (get calendar, validate travel, add events). The C# backend executes each tool call against Google Calendar API and a Haversine distance calculator, returning results to Ollama until the itinerary is complete. SQLite stores trip history and cached locations.

**Tech Stack:** .NET 8, ASP.NET Core, xUnit, EF Core + SQLite, Google.Apis.Calendar.v3, Serilog, System.Text.Json, Ollama HTTP API

---

## Task 0: Project Scaffolding

**Files:**
- Create: `AetherPlan.sln`
- Create: `src/AetherPlan.Api/AetherPlan.Api.csproj`
- Create: `src/AetherPlan.Api/Program.cs`
- Create: `src/AetherPlan.Tests/AetherPlan.Tests.csproj`
- Create: `.gitignore`

**Step 1: Create solution and API project**

```bash
cd /c/Users/Jeff/Documents/Github_new/SmartTripPlanner
dotnet new sln -n AetherPlan
dotnet new webapi -n AetherPlan.Api -o src/AetherPlan.Api --framework net8.0 --no-openapi
dotnet sln AetherPlan.sln add src/AetherPlan.Api/AetherPlan.Api.csproj
```

**Step 2: Create test project**

```bash
dotnet new xunit -n AetherPlan.Tests -o src/AetherPlan.Tests --framework net8.0
dotnet sln AetherPlan.sln add src/AetherPlan.Tests/AetherPlan.Tests.csproj
dotnet add src/AetherPlan.Tests/AetherPlan.Tests.csproj reference src/AetherPlan.Api/AetherPlan.Api.csproj
```

**Step 3: Add NuGet packages**

```bash
# API project
dotnet add src/AetherPlan.Api package Google.Apis.Calendar.v3
dotnet add src/AetherPlan.Api package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/AetherPlan.Api package Microsoft.EntityFrameworkCore.Design
dotnet add src/AetherPlan.Api package Serilog.AspNetCore

# Test project
dotnet add src/AetherPlan.Tests package Microsoft.EntityFrameworkCore.InMemory
dotnet add src/AetherPlan.Tests package NSubstitute
```

**Step 4: Create .gitignore**

Create `.gitignore` with standard .NET ignores plus:
```
bin/
obj/
.vs/
*.user
client_secret.json
token.json
*.pfx
appsettings.Development.json
AetherPlan.db
```

**Step 5: Enable nullable reference types**

Edit both `.csproj` files to ensure `<Nullable>enable</Nullable>` is set.

**Step 6: Verify build**

```bash
dotnet build AetherPlan.sln
```

Expected: Build succeeded with 0 errors.

**Step 7: Commit**

```bash
git add AetherPlan.sln src/ .gitignore
git commit -m "feat: scaffold solution with API and test projects"
```

---

## Task 1: Domain Models

**Files:**
- Create: `src/AetherPlan.Api/Models/Trip.cs`
- Create: `src/AetherPlan.Api/Models/TripEvent.cs`
- Create: `src/AetherPlan.Api/Models/CachedLocation.cs`
- Create: `src/AetherPlan.Api/Models/UserPreference.cs`
- Create: `src/AetherPlan.Api/Models/FreeBusyBlock.cs`
- Create: `src/AetherPlan.Api/Models/TravelValidation.cs`

**Step 1: Create entity models**

```csharp
// src/AetherPlan.Api/Models/Trip.cs
namespace AetherPlan.Api.Models;

public class Trip
{
    public int Id { get; set; }
    public required string Destination { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TripEvent> Events { get; set; } = [];
}
```

```csharp
// src/AetherPlan.Api/Models/TripEvent.cs
namespace AetherPlan.Api.Models;

public class TripEvent
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public required string Summary { get; set; }
    public required string Location { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string? CalendarEventId { get; set; }
    public Trip? Trip { get; set; }
}
```

```csharp
// src/AetherPlan.Api/Models/CachedLocation.cs
namespace AetherPlan.Api.Models;

public class CachedLocation
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public required string Category { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
```

```csharp
// src/AetherPlan.Api/Models/UserPreference.cs
namespace AetherPlan.Api.Models;

public class UserPreference
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
}
```

**Step 2: Create DTOs for service layer**

```csharp
// src/AetherPlan.Api/Models/FreeBusyBlock.cs
namespace AetherPlan.Api.Models;

public class FreeBusyBlock
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsBusy { get; set; }
}
```

```csharp
// src/AetherPlan.Api/Models/TravelValidation.cs
namespace AetherPlan.Api.Models;

public class TravelValidation
{
    public double DistanceKm { get; set; }
    public double EstimatedMinutes { get; set; }
    public double AvailableMinutes { get; set; }
    public bool IsFeasible { get; set; }
}
```

**Step 3: Verify build**

```bash
dotnet build AetherPlan.sln
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/AetherPlan.Api/Models/
git commit -m "feat: add domain models and DTOs"
```

---

## Task 2: EF Core DbContext + SQLite

**Files:**
- Create: `src/AetherPlan.Api/Data/AetherPlanDbContext.cs`
- Modify: `src/AetherPlan.Api/Program.cs`

**Step 1: Create DbContext**

```csharp
// src/AetherPlan.Api/Data/AetherPlanDbContext.cs
namespace AetherPlan.Api.Data;

using AetherPlan.Api.Models;
using Microsoft.EntityFrameworkCore;

public class AetherPlanDbContext(DbContextOptions<AetherPlanDbContext> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripEvent> TripEvents => Set<TripEvent>();
    public DbSet<CachedLocation> CachedLocations => Set<CachedLocation>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Trip>()
            .HasMany(t => t.Events)
            .WithOne(e => e.Trip)
            .HasForeignKey(e => e.TripId);

        modelBuilder.Entity<UserPreference>()
            .HasIndex(p => p.Key)
            .IsUnique();
    }
}
```

**Step 2: Register in Program.cs**

Add to `Program.cs` service registration:

```csharp
using AetherPlan.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddDbContext<AetherPlanDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=AetherPlan.db"));

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

**Step 3: Add connection string to appsettings.json**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=AetherPlan.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

**Step 4: Create initial migration**

```bash
dotnet ef migrations add InitialCreate --project src/AetherPlan.Api
```

**Step 5: Verify build**

```bash
dotnet build AetherPlan.sln
```

**Step 6: Commit**

```bash
git add src/AetherPlan.Api/Data/ src/AetherPlan.Api/Program.cs src/AetherPlan.Api/Migrations/ src/AetherPlan.Api/appsettings.json
git commit -m "feat: add EF Core DbContext with SQLite"
```

---

## Task 3: TravelService (Haversine)

**Files:**
- Create: `src/AetherPlan.Api/Services/ITravelService.cs`
- Create: `src/AetherPlan.Api/Services/TravelService.cs`
- Create: `src/AetherPlan.Tests/Services/TravelServiceTests.cs`

**Step 1: Write the failing tests**

```csharp
// src/AetherPlan.Tests/Services/TravelServiceTests.cs
namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Services;

public class TravelServiceTests
{
    private readonly TravelService _sut = new();

    [Fact]
    public void CalculateDistanceKm_NewYorkToLA_ReturnsApprox3940()
    {
        // NYC: 40.7128, -74.0060  LA: 34.0522, -118.2437
        var distance = _sut.CalculateDistanceKm(40.7128, -74.0060, 34.0522, -118.2437);
        Assert.InRange(distance, 3900, 3980);
    }

    [Fact]
    public void CalculateDistanceKm_SamePoint_ReturnsZero()
    {
        var distance = _sut.CalculateDistanceKm(51.5074, -0.1278, 51.5074, -0.1278);
        Assert.Equal(0, distance);
    }

    [Fact]
    public void CalculateDistanceKm_LondonToParis_ReturnsApprox344()
    {
        // London: 51.5074, -0.1278  Paris: 48.8566, 2.3522
        var distance = _sut.CalculateDistanceKm(51.5074, -0.1278, 48.8566, 2.3522);
        Assert.InRange(distance, 330, 360);
    }

    [Fact]
    public void EstimateTravelMinutes_100Km_Returns93AtDefaultSpeed()
    {
        // 100km at 40mph (64.37 km/h) = ~93 minutes
        var minutes = _sut.EstimateTravelMinutes(100);
        Assert.InRange(minutes, 90, 96);
    }

    [Fact]
    public void ValidateTravel_EnoughTime_ReturnsFeasible()
    {
        // London to Paris (~344km), ~320 min travel, given 6 hours (360 min)
        var result = _sut.ValidateTravel(
            51.5074, -0.1278, 48.8566, 2.3522,
            DateTime.Today.AddHours(8),
            DateTime.Today.AddHours(14));

        Assert.True(result.IsFeasible);
    }

    [Fact]
    public void ValidateTravel_NotEnoughTime_ReturnsNotFeasible()
    {
        // NYC to LA (~3940km), would need ~61 hours, given only 2 hours
        var result = _sut.ValidateTravel(
            40.7128, -74.0060, 34.0522, -118.2437,
            DateTime.Today.AddHours(8),
            DateTime.Today.AddHours(10));

        Assert.False(result.IsFeasible);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~TravelServiceTests" -v minimal
```

Expected: FAIL — `TravelService` does not exist yet.

**Step 3: Create the interface**

```csharp
// src/AetherPlan.Api/Services/ITravelService.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface ITravelService
{
    double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2);
    double EstimateTravelMinutes(double distanceKm, double averageSpeedMph = 40);
    TravelValidation ValidateTravel(double lat1, double lon1, double lat2, double lon2,
        DateTime departureTime, DateTime arrivalDeadline);
}
```

**Step 4: Implement TravelService**

```csharp
// src/AetherPlan.Api/Services/TravelService.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public class TravelService : ITravelService
{
    private const double EarthRadiusKm = 6371.0;
    private const double MphToKmh = 1.60934;

    public double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    public double EstimateTravelMinutes(double distanceKm, double averageSpeedMph = 40)
    {
        var speedKmh = averageSpeedMph * MphToKmh;
        return (distanceKm / speedKmh) * 60;
    }

    public TravelValidation ValidateTravel(double lat1, double lon1, double lat2, double lon2,
        DateTime departureTime, DateTime arrivalDeadline)
    {
        var distanceKm = CalculateDistanceKm(lat1, lon1, lat2, lon2);
        var estimatedMinutes = EstimateTravelMinutes(distanceKm);
        var availableMinutes = (arrivalDeadline - departureTime).TotalMinutes;

        return new TravelValidation
        {
            DistanceKm = Math.Round(distanceKm, 2),
            EstimatedMinutes = Math.Round(estimatedMinutes, 2),
            AvailableMinutes = Math.Round(availableMinutes, 2),
            IsFeasible = estimatedMinutes <= availableMinutes
        };
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~TravelServiceTests" -v minimal
```

Expected: All 6 tests PASS.

**Step 6: Register in DI**

Add to `Program.cs`:
```csharp
builder.Services.AddSingleton<ITravelService, TravelService>();
```

**Step 7: Commit**

```bash
git add src/AetherPlan.Api/Services/ITravelService.cs src/AetherPlan.Api/Services/TravelService.cs src/AetherPlan.Tests/Services/ src/AetherPlan.Api/Program.cs
git commit -m "feat: add TravelService with Haversine distance calculation"
```

---

## Task 4: CalendarService (Google Calendar Integration)

**Files:**
- Create: `src/AetherPlan.Api/Services/ICalendarService.cs`
- Create: `src/AetherPlan.Api/Services/CalendarService.cs`
- Create: `src/AetherPlan.Tests/Services/CalendarServiceTests.cs`

**Step 1: Write the failing tests (with mocked Google Calendar)**

```csharp
// src/AetherPlan.Tests/Services/CalendarServiceTests.cs
namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using NSubstitute;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;

public class CalendarServiceTests
{
    [Fact]
    public async Task GetCalendarViewAsync_ReturnsFreeBusyBlocks()
    {
        // This test verifies the mapping logic from Google Events to FreeBusyBlocks.
        // We test the public interface with a test double that returns known events.
        var service = new TestableCalendarService(new List<Event>
        {
            new() { Summary = "Meeting", Start = new EventDateTime { DateTime = new DateTime(2026, 3, 10, 9, 0, 0) },
                     End = new EventDateTime { DateTime = new DateTime(2026, 3, 10, 10, 0, 0) } }
        });

        var blocks = await service.GetCalendarViewAsync(
            new DateTime(2026, 3, 10, 8, 0, 0),
            new DateTime(2026, 3, 10, 12, 0, 0));

        Assert.Contains(blocks, b => b.IsBusy && b.Start.Hour == 9);
        Assert.Contains(blocks, b => !b.IsBusy);
    }

    [Fact]
    public async Task GetCalendarViewAsync_NoEvents_AllFree()
    {
        var service = new TestableCalendarService(new List<Event>());

        var blocks = await service.GetCalendarViewAsync(
            new DateTime(2026, 3, 10, 8, 0, 0),
            new DateTime(2026, 3, 10, 12, 0, 0));

        Assert.All(blocks, b => Assert.False(b.IsBusy));
    }
}

/// <summary>
/// Test double that bypasses Google API auth and returns canned events.
/// </summary>
internal class TestableCalendarService : CalendarService
{
    private readonly List<Event> _events;

    public TestableCalendarService(List<Event> events) : base(calendarService: null!)
    {
        _events = events;
    }

    protected override Task<IList<Event>> FetchEventsAsync(DateTime start, DateTime end)
    {
        IList<Event> result = _events
            .Where(e => e.Start.DateTime >= start && e.End.DateTime <= end)
            .ToList();
        return Task.FromResult(result);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~CalendarServiceTests" -v minimal
```

Expected: FAIL — `CalendarService` does not exist yet.

**Step 3: Create the interface**

```csharp
// src/AetherPlan.Api/Services/ICalendarService.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;
using Google.Apis.Calendar.v3.Data;

public interface ICalendarService
{
    Task<List<FreeBusyBlock>> GetCalendarViewAsync(DateTime start, DateTime end);
    Task<string> CreateEventAsync(string summary, string location, DateTime start, DateTime end, string? description = null);
    Task<List<string>> PushItineraryAsync(List<TripEvent> events);
}
```

**Step 4: Implement CalendarService**

```csharp
// src/AetherPlan.Api/Services/CalendarService.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Logging;

public class CalendarService : ICalendarService
{
    private readonly CalendarApi? _calendarApi;
    private readonly ILogger<CalendarService>? _logger;

    public CalendarService(CalendarApi? calendarService, ILogger<CalendarService>? logger = null)
    {
        _calendarApi = calendarService;
        _logger = logger;
    }

    public async Task<List<FreeBusyBlock>> GetCalendarViewAsync(DateTime start, DateTime end)
    {
        var events = await FetchEventsAsync(start, end);
        var blocks = new List<FreeBusyBlock>();

        // Sort events by start time
        var sorted = events.OrderBy(e => e.Start.DateTime).ToList();

        var cursor = start;
        foreach (var evt in sorted)
        {
            var evtStart = evt.Start.DateTime ?? start;
            var evtEnd = evt.End.DateTime ?? end;

            if (cursor < evtStart)
            {
                blocks.Add(new FreeBusyBlock { Start = cursor, End = evtStart, IsBusy = false });
            }

            blocks.Add(new FreeBusyBlock { Start = evtStart, End = evtEnd, IsBusy = true });
            cursor = evtEnd > cursor ? evtEnd : cursor;
        }

        if (cursor < end)
        {
            blocks.Add(new FreeBusyBlock { Start = cursor, End = end, IsBusy = false });
        }

        return blocks;
    }

    public async Task<string> CreateEventAsync(string summary, string location,
        DateTime start, DateTime end, string? description = null)
    {
        if (_calendarApi is null) throw new InvalidOperationException("Google Calendar API not configured");

        var calendarEvent = new Event
        {
            Summary = summary,
            Location = location,
            Description = description,
            Start = new EventDateTime { DateTime = start },
            End = new EventDateTime { DateTime = end }
        };

        var request = _calendarApi.Events.Insert(calendarEvent, "primary");
        var created = await request.ExecuteAsync();

        _logger?.LogInformation("Created calendar event {EventId}: {Summary}", created.Id, summary);
        return created.Id;
    }

    public async Task<List<string>> PushItineraryAsync(List<TripEvent> events)
    {
        var ids = new List<string>();
        foreach (var evt in events)
        {
            var id = await CreateEventAsync(evt.Summary, evt.Location, evt.Start, evt.End);
            ids.Add(id);
        }
        return ids;
    }

    protected virtual async Task<IList<Event>> FetchEventsAsync(DateTime start, DateTime end)
    {
        if (_calendarApi is null) throw new InvalidOperationException("Google Calendar API not configured");

        var request = _calendarApi.Events.List("primary");
        request.TimeMinDateTimeOffset = new DateTimeOffset(start);
        request.TimeMaxDateTimeOffset = new DateTimeOffset(end);
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var result = await request.ExecuteAsync();
        return result.Items ?? (IList<Event>)new List<Event>();
    }
}
```

Note: `CalendarApi` is a type alias we'll use for `Google.Apis.Calendar.v3.CalendarService`. Since `CalendarService` name collides, add this using alias to the file:
```csharp
using CalendarApi = Google.Apis.Calendar.v3.CalendarService;
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~CalendarServiceTests" -v minimal
```

Expected: All 2 tests PASS.

**Step 6: Commit**

```bash
git add src/AetherPlan.Api/Services/ICalendarService.cs src/AetherPlan.Api/Services/CalendarService.cs src/AetherPlan.Tests/Services/CalendarServiceTests.cs
git commit -m "feat: add CalendarService with Google Calendar integration"
```

---

## Task 5: OllamaClient (HTTP Client for Tool-Calling)

**Files:**
- Create: `src/AetherPlan.Api/Services/IOllamaClient.cs`
- Create: `src/AetherPlan.Api/Services/OllamaClient.cs`
- Create: `src/AetherPlan.Api/Models/OllamaModels.cs`
- Create: `src/AetherPlan.Tests/Services/OllamaClientTests.cs`

**Step 1: Create Ollama request/response models**

```csharp
// src/AetherPlan.Api/Models/OllamaModels.cs
namespace AetherPlan.Api.Models;

using System.Text.Json.Serialization;

public class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<OllamaMessage> Messages { get; set; }

    [JsonPropertyName("tools")]
    public List<OllamaTool>? Tools { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

public class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required OllamaFunction Function { get; set; }
}

public class OllamaFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("parameters")]
    public required object Parameters { get; set; }
}

public class OllamaToolCall
{
    [JsonPropertyName("function")]
    public required OllamaFunctionCall Function { get; set; }
}

public class OllamaFunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public required Dictionary<string, object> Arguments { get; set; }
}

public class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public required OllamaMessage Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
```

**Step 2: Write the failing tests**

```csharp
// src/AetherPlan.Tests/Services/OllamaClientTests.cs
namespace AetherPlan.Tests.Services;

using System.Net;
using System.Text.Json;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;

public class OllamaClientTests
{
    [Fact]
    public async Task ChatAsync_WithToolResponse_ReturnsToolCalls()
    {
        var expectedResponse = new OllamaChatResponse
        {
            Message = new OllamaMessage
            {
                Role = "assistant",
                Content = null,
                ToolCalls = [new OllamaToolCall
                {
                    Function = new OllamaFunctionCall
                    {
                        Name = "get_calendar_view",
                        Arguments = new Dictionary<string, object>
                        {
                            ["start"] = "2026-03-10",
                            ["end"] = "2026-03-12"
                        }
                    }
                }]
            },
            Done = true
        };

        var handler = new FakeHttpHandler(JsonSerializer.Serialize(expectedResponse));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "qwen3.5:35b-a3b-q4_K_M");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "Plan a trip to Tokyo March 10-12" }
        };

        var response = await client.ChatAsync(messages, tools: null);

        Assert.NotNull(response.Message.ToolCalls);
        Assert.Single(response.Message.ToolCalls);
        Assert.Equal("get_calendar_view", response.Message.ToolCalls[0].Function.Name);
    }

    [Fact]
    public async Task ChatAsync_WithTextResponse_ReturnsContent()
    {
        var expectedResponse = new OllamaChatResponse
        {
            Message = new OllamaMessage { Role = "assistant", Content = "Here is your itinerary." },
            Done = true
        };

        var handler = new FakeHttpHandler(JsonSerializer.Serialize(expectedResponse));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "qwen3.5:35b-a3b-q4_K_M");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "Summarize the plan" }
        };

        var response = await client.ChatAsync(messages, tools: null);

        Assert.NotNull(response.Message.Content);
        Assert.Null(response.Message.ToolCalls);
    }
}

internal class FakeHttpHandler(string responseJson) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
```

**Step 3: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~OllamaClientTests" -v minimal
```

Expected: FAIL — `OllamaClient` does not exist.

**Step 4: Create the interface and implementation**

```csharp
// src/AetherPlan.Api/Services/IOllamaClient.cs
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface IOllamaClient
{
    Task<OllamaChatResponse> ChatAsync(List<OllamaMessage> messages, List<OllamaTool>? tools);
}
```

```csharp
// src/AetherPlan.Api/Services/OllamaClient.cs
namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Models;

public class OllamaClient(HttpClient httpClient, string model) : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<OllamaChatResponse> ChatAsync(List<OllamaMessage> messages, List<OllamaTool>? tools)
    {
        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Tools = tools,
            Stream = false
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("/api/chat", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Ollama response");
    }
}
```

**Step 5: Run tests to verify they pass**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~OllamaClientTests" -v minimal
```

Expected: All 2 tests PASS.

**Step 6: Register in DI**

Add to `Program.cs`:
```csharp
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromMinutes(5); // LLM can be slow
});
```

Add to `appsettings.json`:
```json
"Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3.5:35b-a3b-q4_K_M"
}
```

**Step 7: Commit**

```bash
git add src/AetherPlan.Api/Models/OllamaModels.cs src/AetherPlan.Api/Services/IOllamaClient.cs src/AetherPlan.Api/Services/OllamaClient.cs src/AetherPlan.Tests/Services/OllamaClientTests.cs src/AetherPlan.Api/Program.cs src/AetherPlan.Api/appsettings.json
git commit -m "feat: add OllamaClient HTTP client with tool-calling support"
```

---

## Task 6: Tool Definitions

**Files:**
- Create: `src/AetherPlan.Api/Tools/ToolDefinitions.cs`
- Create: `src/AetherPlan.Tests/Tools/ToolDefinitionsTests.cs`

**Step 1: Write the failing test**

```csharp
// src/AetherPlan.Tests/Tools/ToolDefinitionsTests.cs
namespace AetherPlan.Tests.Tools;

using AetherPlan.Api.Tools;

public class ToolDefinitionsTests
{
    [Fact]
    public void GetAllTools_ReturnsFourTools()
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Equal(4, tools.Count);
    }

    [Theory]
    [InlineData("get_calendar_view")]
    [InlineData("validate_travel")]
    [InlineData("add_trip_event")]
    [InlineData("search_area")]
    public void GetAllTools_ContainsTool(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~ToolDefinitionsTests" -v minimal
```

Expected: FAIL.

**Step 3: Implement ToolDefinitions**

```csharp
// src/AetherPlan.Api/Tools/ToolDefinitions.cs
namespace AetherPlan.Api.Tools;

using AetherPlan.Api.Models;

public static class ToolDefinitions
{
    public static List<OllamaTool> GetAllTools() =>
    [
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "get_calendar_view",
                Description = "Returns free/busy time blocks for a date range from Google Calendar",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        start = new { type = "string", description = "ISO 8601 start date (e.g. 2026-03-10T00:00:00)" },
                        end = new { type = "string", description = "ISO 8601 end date (e.g. 2026-03-12T23:59:59)" }
                    },
                    required = new[] { "start", "end" }
                }
            }
        },
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "validate_travel",
                Description = "Checks if travel between two locations is feasible given departure time and arrival deadline",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        from_lat = new { type = "number", description = "Origin latitude" },
                        from_lon = new { type = "number", description = "Origin longitude" },
                        to_lat = new { type = "number", description = "Destination latitude" },
                        to_lon = new { type = "number", description = "Destination longitude" },
                        departure_time = new { type = "string", description = "ISO 8601 departure time" },
                        arrival_deadline = new { type = "string", description = "ISO 8601 arrival deadline" }
                    },
                    required = new[] { "from_lat", "from_lon", "to_lat", "to_lon", "departure_time", "arrival_deadline" }
                }
            }
        },
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "add_trip_event",
                Description = "Creates a Google Calendar event with location and description",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        summary = new { type = "string", description = "Event title" },
                        location = new { type = "string", description = "Event location name or address" },
                        start = new { type = "string", description = "ISO 8601 start time" },
                        end = new { type = "string", description = "ISO 8601 end time" },
                        description = new { type = "string", description = "Event description (optional)" }
                    },
                    required = new[] { "summary", "location", "start", "end" }
                }
            }
        },
        new OllamaTool
        {
            Function = new OllamaFunction
            {
                Name = "search_area",
                Description = "Uses internal knowledge to suggest attractions, restaurants, and activities in a given area",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        area = new { type = "string", description = "City, neighborhood, or region to search" },
                        category = new { type = "string", description = "Category: attractions, restaurants, activities, hotels" },
                        limit = new { type = "integer", description = "Max number of suggestions (default 5)" }
                    },
                    required = new[] { "area" }
                }
            }
        }
    ];
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~ToolDefinitionsTests" -v minimal
```

Expected: All 5 tests PASS.

**Step 5: Commit**

```bash
git add src/AetherPlan.Api/Tools/ src/AetherPlan.Tests/Tools/
git commit -m "feat: add Ollama tool definitions for all four agent tools"
```

---

## Task 7: AgentService (The Agent Loop)

**Files:**
- Create: `src/AetherPlan.Api/Services/IAgentService.cs`
- Create: `src/AetherPlan.Api/Services/AgentService.cs`
- Create: `src/AetherPlan.Tests/Services/AgentServiceTests.cs`

**Step 1: Write the failing tests**

```csharp
// src/AetherPlan.Tests/Services/AgentServiceTests.cs
namespace AetherPlan.Tests.Services;

using System.Text.Json;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using NSubstitute;
using Microsoft.Extensions.Logging;

public class AgentServiceTests
{
    private readonly IOllamaClient _ollamaClient = Substitute.For<IOllamaClient>();
    private readonly ICalendarService _calendarService = Substitute.For<ICalendarService>();
    private readonly ITravelService _travelService = Substitute.For<ITravelService>();
    private readonly AgentService _sut;

    public AgentServiceTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        _sut = new AgentService(_ollamaClient, _calendarService, _travelService, logger);
    }

    [Fact]
    public async Task RunAsync_DirectTextResponse_ReturnsContent()
    {
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .Returns(new OllamaChatResponse
            {
                Message = new OllamaMessage { Role = "assistant", Content = "Here is your plan." },
                Done = true
            });

        var result = await _sut.RunAsync("Plan a trip");

        Assert.Equal("Here is your plan.", result);
    }

    [Fact]
    public async Task RunAsync_ToolCallThenTextResponse_ExecutesToolAndReturns()
    {
        // First call: Ollama wants to call get_calendar_view
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
                                Name = "get_calendar_view",
                                Arguments = new Dictionary<string, object>
                                {
                                    ["start"] = "2026-03-10T00:00:00",
                                    ["end"] = "2026-03-12T23:59:59"
                                }
                            }
                        }]
                    },
                    Done = true
                },
                new OllamaChatResponse
                {
                    Message = new OllamaMessage { Role = "assistant", Content = "You're free all day!" },
                    Done = true
                });

        _calendarService.GetCalendarViewAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<FreeBusyBlock>
            {
                new() { Start = DateTime.Today, End = DateTime.Today.AddHours(24), IsBusy = false }
            });

        var result = await _sut.RunAsync("Am I free March 10-12?");

        Assert.Equal("You're free all day!", result);
        await _calendarService.Received(1).GetCalendarViewAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task RunAsync_MaxIterationsReached_ReturnsWarning()
    {
        // Always return tool calls, never a text response
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .Returns(new OllamaChatResponse
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
            });

        var result = await _sut.RunAsync("Plan Tokyo trip", maxIterations: 3);

        Assert.Contains("max iterations", result, StringComparison.OrdinalIgnoreCase);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~AgentServiceTests" -v minimal
```

Expected: FAIL.

**Step 3: Create interface and implementation**

```csharp
// src/AetherPlan.Api/Services/IAgentService.cs
namespace AetherPlan.Api.Services;

public interface IAgentService
{
    Task<string> RunAsync(string userRequest, int maxIterations = 10);
}
```

```csharp
// src/AetherPlan.Api/Services/AgentService.cs
namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Models;
using AetherPlan.Api.Tools;

public class AgentService(
    IOllamaClient ollamaClient,
    ICalendarService calendarService,
    ITravelService travelService,
    ILogger<AgentService> logger) : IAgentService
{
    private const string SystemPrompt =
        "You are a professional travel logistician. Maximize sightseeing while " +
        "minimizing travel fatigue. You have access to the user's Google Calendar. " +
        "When a trip is requested, check for free slots, research the area, and " +
        "only commit events once you've verified travel times between locations.";

    public async Task<string> RunAsync(string userRequest, int maxIterations = 10)
    {
        var messages = new List<OllamaMessage>
        {
            new() { Role = "system", Content = SystemPrompt },
            new() { Role = "user", Content = userRequest }
        };

        var tools = ToolDefinitions.GetAllTools();

        for (var i = 0; i < maxIterations; i++)
        {
            logger.LogInformation("Agent iteration {Iteration}", i + 1);

            var response = await ollamaClient.ChatAsync(messages, tools);
            var message = response.Message;

            // If no tool calls, we have a final text response
            if (message.ToolCalls is null || message.ToolCalls.Count == 0)
            {
                return message.Content ?? string.Empty;
            }

            // Add assistant message with tool calls to history
            messages.Add(message);

            // Execute each tool call
            foreach (var toolCall in message.ToolCalls)
            {
                logger.LogInformation("Executing tool: {ToolName}", toolCall.Function.Name);
                var result = await ExecuteToolAsync(toolCall);

                messages.Add(new OllamaMessage
                {
                    Role = "tool",
                    Content = JsonSerializer.Serialize(result)
                });
            }
        }

        return "Agent reached max iterations without completing. Please try a more specific request.";
    }

    private async Task<object> ExecuteToolAsync(OllamaToolCall toolCall)
    {
        var args = toolCall.Function.Arguments;

        return toolCall.Function.Name switch
        {
            "get_calendar_view" => await calendarService.GetCalendarViewAsync(
                DateTime.Parse(args["start"].ToString()!),
                DateTime.Parse(args["end"].ToString()!)),

            "validate_travel" => travelService.ValidateTravel(
                Convert.ToDouble(args["from_lat"]),
                Convert.ToDouble(args["from_lon"]),
                Convert.ToDouble(args["to_lat"]),
                Convert.ToDouble(args["to_lon"]),
                DateTime.Parse(args["departure_time"].ToString()!),
                DateTime.Parse(args["arrival_deadline"].ToString()!)),

            "add_trip_event" => await calendarService.CreateEventAsync(
                args["summary"].ToString()!,
                args["location"].ToString()!,
                DateTime.Parse(args["start"].ToString()!),
                DateTime.Parse(args["end"].ToString()!),
                args.TryGetValue("description", out var desc) ? desc.ToString() : null),

            "search_area" => new { note = "search_area uses LLM internal knowledge, no external call needed",
                                   area = args["area"].ToString() },

            _ => new { error = $"Unknown tool: {toolCall.Function.Name}" }
        };
    }
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/AetherPlan.Tests --filter "FullyQualifiedName~AgentServiceTests" -v minimal
```

Expected: All 3 tests PASS.

**Step 5: Register in DI**

Add to `Program.cs`:
```csharp
builder.Services.AddScoped<IAgentService, AgentService>();
```

**Step 6: Commit**

```bash
git add src/AetherPlan.Api/Services/IAgentService.cs src/AetherPlan.Api/Services/AgentService.cs src/AetherPlan.Tests/Services/AgentServiceTests.cs src/AetherPlan.Api/Program.cs
git commit -m "feat: add AgentService with Ollama tool-calling loop"
```

---

## Task 8: API Controller

**Files:**
- Create: `src/AetherPlan.Api/Controllers/TripController.cs`

**Step 1: Create the controller**

```csharp
// src/AetherPlan.Api/Controllers/TripController.cs
namespace AetherPlan.Api.Controllers;

using AetherPlan.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class TripController(IAgentService agentService) : ControllerBase
{
    [HttpPost("plan")]
    public async Task<IActionResult> PlanTrip([FromBody] TripRequest request)
    {
        var result = await agentService.RunAsync(request.Prompt);
        return Ok(new { response = result });
    }
}

public class TripRequest
{
    public required string Prompt { get; set; }
}
```

**Step 2: Verify build**

```bash
dotnet build AetherPlan.sln
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/AetherPlan.Api/Controllers/
git commit -m "feat: add TripController with POST /api/trip/plan endpoint"
```

---

## Task 9: Full Build Verification

**Step 1: Run all tests**

```bash
dotnet test AetherPlan.sln -v minimal
```

Expected: All tests pass (13+ tests across 4 test classes).

**Step 2: Verify the API starts (quick smoke test)**

```bash
dotnet build AetherPlan.sln
```

Expected: Build succeeded, 0 warnings, 0 errors.

**Step 3: Commit any remaining fixes**

If any adjustments were needed, commit them:

```bash
git add -A
git commit -m "fix: resolve build issues from integration"
```

---

## Summary

| Task | Component | Tests |
|------|-----------|-------|
| 0 | Project scaffolding | - |
| 1 | Domain models + DTOs | - |
| 2 | EF Core DbContext + SQLite | - |
| 3 | TravelService (Haversine) | 6 tests |
| 4 | CalendarService (Google Calendar) | 2 tests |
| 5 | OllamaClient (HTTP) | 2 tests |
| 6 | Tool definitions | 5 tests |
| 7 | AgentService (agent loop) | 3 tests |
| 8 | TripController (API endpoint) | - |
| 9 | Full build verification | all |
