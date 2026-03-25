# Google Calendar OAuth Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Wire up Google Calendar OAuth 2.0 user consent flow so CalendarService works at runtime with a real Google Calendar.

**Architecture:** A static `GoogleCalendarFactory` class handles the one-time OAuth browser consent flow using `GoogleWebAuthorizationBroker`. Tokens are persisted to `.tokens/` via `FileDataStore`. Program.cs registers the resulting `CalendarApi` in DI. If `client_secret.json` is missing, CalendarApi is registered as null and calendar features degrade gracefully (existing behavior).

**Tech Stack:** Google.Apis.Auth, Google.Apis.Calendar.v3, .NET 8

---

## Task 0: Add NuGet Package and Update Config

**Files:**
- Modify: `src/SmartTripPlanner.Api/SmartTripPlanner.Api.csproj`
- Modify: `src/SmartTripPlanner.Api/appsettings.json`
- Modify: `.gitignore`

**Step 1: Add Google.Apis.Auth package**

Run from `C:/Users/Jeff/Documents/Github_new/SmartTripPlanner`:

```bash
dotnet add src/SmartTripPlanner.Api package Google.Apis.Auth
```

**Step 2: Add GoogleCalendar config to appsettings.json**

The file is at `src/SmartTripPlanner.Api/appsettings.json`. Add a `GoogleCalendar` section after the `Ollama` section:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=SmartTripPlanner.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3.5:35b-a3b-q4_K_M"
  },
  "GoogleCalendar": {
    "CredentialPath": "client_secret.json",
    "TokenDirectory": ".tokens"
  }
}
```

**Step 3: Add `.tokens/` to .gitignore**

The `.gitignore` is in the repo root. Add this line to the "Secrets & credentials" section, after `appsettings.Development.json`:

```
.tokens/
```

**Step 4: Verify build**

```bash
dotnet build SmartTripPlanner.sln
```

Expected: Build succeeded, 0 errors.

**Step 5: Commit**

```bash
git add src/SmartTripPlanner.Api/SmartTripPlanner.Api.csproj src/SmartTripPlanner.Api/appsettings.json .gitignore
git commit -m "chore: add Google.Apis.Auth package and calendar config"
```

---

## Task 1: Create GoogleCalendarFactory

**Files:**
- Create: `src/SmartTripPlanner.Api/Services/GoogleCalendarFactory.cs`
- Test: `src/SmartTripPlanner.Tests/Services/GoogleCalendarFactoryTests.cs`

**Step 1: Write the test**

Create `src/SmartTripPlanner.Tests/Services/GoogleCalendarFactoryTests.cs`:

```csharp
namespace SmartTripPlanner.Tests.Services;

using SmartTripPlanner.Api.Services;

public class GoogleCalendarFactoryTests
{
    [Fact]
    public async Task CreateAsync_MissingCredentialFile_ReturnsNull()
    {
        var result = await GoogleCalendarFactory.CreateAsync(
            "nonexistent_file.json", ".tokens_test");

        Assert.Null(result);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test SmartTripPlanner.sln --filter "GoogleCalendarFactoryTests"
```

Expected: FAIL — `GoogleCalendarFactory` does not exist yet.

**Step 3: Write the implementation**

Create `src/SmartTripPlanner.Api/Services/GoogleCalendarFactory.cs`:

```csharp
namespace SmartTripPlanner.Api.Services;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using CalendarApi = Google.Apis.Calendar.v3.CalendarService;

public static class GoogleCalendarFactory
{
    private static readonly string[] Scopes = [CalendarService.Scope.Calendar];

    public static async Task<CalendarApi?> CreateAsync(string credentialPath, string tokenDirectory)
    {
        if (!File.Exists(credentialPath))
            return null;

        using var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets,
            Scopes,
            "user",
            CancellationToken.None,
            new FileDataStore(tokenDirectory, true));

        return new CalendarApi(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "SmartTripPlanner"
        });
    }
}
```

**Step 4: Run tests**

```bash
dotnet test SmartTripPlanner.sln --filter "GoogleCalendarFactoryTests"
```

Expected: 1 test passed.

**Step 5: Run all tests to check for regressions**

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: 37 tests passed.

**Step 6: Commit**

```bash
git add src/SmartTripPlanner.Api/Services/GoogleCalendarFactory.cs src/SmartTripPlanner.Tests/Services/GoogleCalendarFactoryTests.cs
git commit -m "feat: add GoogleCalendarFactory with OAuth 2.0 support"
```

---

## Task 2: Wire Factory into Program.cs and Update CalendarService DI

**Files:**
- Modify: `src/SmartTripPlanner.Api/Program.cs`
- Modify: `src/SmartTripPlanner.Api/Services/CalendarService.cs`

**Step 1: Update Program.cs to register CalendarApi via factory**

In `src/SmartTripPlanner.Api/Program.cs`, replace the existing line:

```csharp
builder.Services.AddScoped<ICalendarService, CalendarService>();
```

With this block:

```csharp
var credentialPath = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["GoogleCalendar:CredentialPath"] ?? "client_secret.json");
var tokenDirectory = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["GoogleCalendar:TokenDirectory"] ?? ".tokens");

var calendarApi = GoogleCalendarFactory.CreateAsync(credentialPath, tokenDirectory)
    .GetAwaiter().GetResult();

if (calendarApi is not null)
{
    Log.Information("Google Calendar API configured successfully");
    builder.Services.AddSingleton(calendarApi);
}
else
{
    Log.Warning("Google Calendar not configured — client_secret.json not found at {Path}. Calendar features disabled.", credentialPath);
}

builder.Services.AddScoped<ICalendarService, CalendarService>();
```

Also add this using at the top of Program.cs (it may already exist from the `CalendarApi` alias, but `GoogleCalendarFactory` is in the `Services` namespace which is already imported):

No new usings needed — `SmartTripPlanner.Api.Services` is already imported.

**Step 2: Update CalendarService constructor to handle optional CalendarApi**

The current constructor is:

```csharp
public CalendarService(CalendarApi? calendarService, ILogger<CalendarService>? logger = null)
```

DI won't resolve `CalendarApi?` — it needs to handle the case where CalendarApi is not registered. Change the constructor to accept `IServiceProvider` and try to resolve:

Actually, the simpler approach: use two constructors. Replace the existing constructor in `src/SmartTripPlanner.Api/Services/CalendarService.cs`:

```csharp
public CalendarService(ILogger<CalendarService> logger, CalendarApi? calendarService = null)
{
    _calendarApi = calendarService;
    _logger = logger;
}
```

Note the parameter order swap — `ILogger` first (always available via DI), `CalendarApi?` second with default null (resolved only if registered).

**Important:** The `TestableCalendarService` in the test file calls `base(calendarService: null!)`. This uses named parameters, so it will still work because the parameter is still named `calendarService`.

Wait — named parameter `calendarService` still exists so tests won't break. But the base call passes `null!` for CalendarApi and doesn't pass a logger. We need to also keep a constructor that works for tests. The simplest fix: keep one constructor with both parameters having defaults:

```csharp
public CalendarService(CalendarApi? calendarService = null, ILogger<CalendarService>? logger = null)
```

This is the **existing signature** — it already works. The issue is DI resolution: if `CalendarApi` is not registered in DI, the DI container can't resolve `CalendarService` because it can't provide a value for `calendarService`.

The fix: register `CalendarService` with a factory in Program.cs instead. Replace the `AddScoped` line with:

```csharp
builder.Services.AddScoped<ICalendarService>(sp =>
{
    var api = sp.GetService<CalendarApi>();
    var logger = sp.GetRequiredService<ILogger<CalendarService>>();
    return new CalendarService(api, logger);
});
```

This way, `sp.GetService<CalendarApi>()` returns null if not registered (unlike `GetRequiredService` which throws).

**Final Program.cs change — replace the plain `AddScoped` with the factory version:**

Replace:
```csharp
builder.Services.AddScoped<ICalendarService, CalendarService>();
```

With:
```csharp
builder.Services.AddScoped<ICalendarService>(sp =>
{
    var api = sp.GetService<CalendarApi>();
    var logger = sp.GetRequiredService<ILogger<CalendarService>>();
    return new CalendarService(api, logger);
});
```

**Step 3: Verify build**

```bash
dotnet build SmartTripPlanner.sln
```

Expected: Build succeeded, 0 errors, 0 warnings.

**Step 4: Run all tests**

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: 37 tests passed (TestableCalendarService still works because its `base(calendarService: null!)` call matches the existing constructor).

**Step 5: Commit**

```bash
git add src/SmartTripPlanner.Api/Program.cs src/SmartTripPlanner.Api/Services/CalendarService.cs
git commit -m "feat: wire Google Calendar OAuth factory into DI pipeline"
```

---

## Task 3: Fix launchSettings.json

**Files:**
- Modify: `src/SmartTripPlanner.Api/Properties/launchSettings.json`

**Step 1: Update launchSettings.json**

Replace the entire file content of `src/SmartTripPlanner.Api/Properties/launchSettings.json`:

```json
{
  "$schema": "http://json.schemastore.org/launchsettings.json",
  "profiles": {
    "SmartTripPlanner": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5197",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

This removes the stale `weatherforecast` launch URL, drops the unused IIS Express and HTTPS profiles, and points to the Blazor UI root.

**Step 2: Verify build**

```bash
dotnet build SmartTripPlanner.sln
```

Expected: Build succeeded.

**Step 3: Run all tests**

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: 37 tests passed.

**Step 4: Commit**

```bash
git add src/SmartTripPlanner.Api/Properties/launchSettings.json
git commit -m "fix: update launchSettings to remove template defaults"
```

---

## Summary

| Task | Component | Description |
|------|-----------|-------------|
| 0 | Config | Add Google.Apis.Auth NuGet, appsettings config, .gitignore |
| 1 | GoogleCalendarFactory | Static factory with OAuth 2.0 browser consent + FileDataStore |
| 2 | DI Wiring | Register CalendarApi via factory in Program.cs, factory-based CalendarService registration |
| 3 | Cleanup | Fix launchSettings.json template defaults |
