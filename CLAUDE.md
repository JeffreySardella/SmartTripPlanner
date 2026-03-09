# AetherPlan - Local Smart Trip Planner

Autonomous travel agent using local LLM inference to research locations,
calculate travel feasibility, and push dynamic itineraries to Google Calendar.

## Stack

- **Runtime:** .NET 8 / ASP.NET Core Web API (C#)
- **LLM:** Qwen3.5-35B-A3B via Ollama (local, no cloud AI)
- **Agent Orchestration:** Ollama native tool-calling API (no external framework)
- **Calendar:** Google.Apis.Calendar.v3
- **Database:** SQLite via EF Core (trip history, preferences, cached locations)
- **Travel Estimation:** Haversine formula fallback (40mph avg), Google Maps API optional
- **Logging:** Serilog with structured logging
- **Hardware:** AMD 7900 XTX (24GB VRAM, ROCm)

## Architecture

```
User → Ollama prompt / Minimal Web UI
            │
            ▼
┌──────────────────────┐
│   ASP.NET Core        │  Web API + Agent loop
│   AetherPlan.Api      │
├──────────────────────┤
│  AgentService         │  Calls Ollama with tool definitions
│  CalendarService      │  Google Calendar CRUD
│  TravelService        │  Distance/time estimation
│  ItineraryService     │  Builds + validates plans
│  PersistenceService   │  SQLite via EF Core
└──────────┬───────────┘
           │ HTTP localhost:11434
           ▼
┌──────────────────────┐
│   Ollama              │  qwen3.5:35b-a3b-q4_K_M
│   (tool-calling)      │
└──────────────────────┘
```

The agent loop lives in C#: send user request + tool definitions to Ollama,
receive tool calls back, execute them, return results, repeat until done.

## Project Structure

```
AetherPlan/
├── src/
│   ├── AetherPlan.Api/
│   │   ├── Controllers/             # REST endpoints
│   │   ├── Services/
│   │   │   ├── AgentService.cs      # Ollama agent loop
│   │   │   ├── CalendarService.cs   # Google Calendar integration
│   │   │   ├── TravelService.cs     # Distance/time calculations
│   │   │   ├── ItineraryService.cs  # Trip planning logic
│   │   │   └── PersistenceService.cs # SQLite data access
│   │   ├── Models/                  # DTOs, domain models, EF entities
│   │   ├── Data/                    # DbContext, migrations
│   │   ├── Tools/                   # Tool definitions for Ollama
│   │   └── Program.cs
│   └── AetherPlan.Tests/           # xUnit tests
├── docs/plans/
├── CLAUDE.md
└── AetherPlan.sln
```

## Coding Conventions

- C# 12, nullable reference types enabled, file-scoped namespaces
- Async/await throughout — all I/O methods return `Task<T>`
- Dependency injection via built-in .NET DI container
- Configuration via `appsettings.json` + `dotnet user-secrets` for credentials
- **System.Text.Json only** — no Newtonsoft.Json
- One class per file, interfaces per service (`ICalendarService`, `ITravelService`, etc.)
- xUnit for tests, naming: `MethodName_Scenario_ExpectedResult`
- Serilog structured logging — log agent loop iterations, tool calls, and errors

## Build & Run

```bash
# Build
dotnet build AetherPlan.sln

# Run tests
dotnet test AetherPlan.sln

# Run the API (Ollama must be running)
dotnet run --project src/AetherPlan.Api

# Apply EF Core migrations
dotnet ef database update --project src/AetherPlan.Api
```

## AMD GPU / Ollama Setup

```bash
# Install Ollama (Linux — for WSL or native)
curl -fsSL https://ollama.com/install.sh | sh

# Pull the MoE model (q4_K_M quant, ~21GB VRAM)
ollama pull qwen3.5:35b-a3b-q4_K_M

# Start Ollama and verify
ollama run qwen3.5:35b-a3b-q4_K_M

# Check GPU utilization
rocm-smi
```

Ollama auto-detects ROCm on the 7900 XTX. If VRAM issues occur, drop to q4_K_S.

## Google Calendar Auth

- OAuth 2.0 with user consent flow
- Store `client_secret.json` in project root (gitignored)
- Token stored via `dotnet user-secrets`
- **Never commit credentials**

## Ollama Tool-Calling

Use Ollama `/api/chat` with `tools` array. Each tool is `type: "function"` with
name, description, and JSON Schema parameters. When Ollama responds with
`tool_calls`, execute the matching C# service method and return results as a
`tool` role message. Tools exposed to the LLM:

1. **get_calendar_view** — Free/busy blocks for a date range
2. **validate_travel** — Can you get from A to B in the available time?
3. **add_trip_event** — Create a Google Calendar event with location + description
4. **search_area** — LLM uses internal knowledge to suggest attractions/restaurants

## Agent System Prompt

> You are a professional travel logistician. Maximize sightseeing while
> minimizing travel fatigue. You have access to the user's Google Calendar.
> When a trip is requested, check for free slots, research the area, and
> only commit events once you've verified travel times between locations.

## SQLite Schema (key tables)

- **Trips** — id, destination, start_date, end_date, status, created_at
- **TripEvents** — id, trip_id, summary, location, lat, lng, start, end, calendar_event_id
- **CachedLocations** — id, name, lat, lng, category, last_updated
- **UserPreferences** — id, key, value

## Testing Strategy

- Mock `IOllamaClient` to test agent loop without running Ollama
- Use in-memory SQLite for database tests
- Integration tests hit real Ollama (marked `[Trait("Category", "Integration")]`)
- Test Haversine calculations with known city-pair distances

## Implementation Phases

### Phase 1: Foundation
- [ ] Initialize ASP.NET Core Web API + solution + SQLite/EF Core
- [ ] Google Calendar OAuth + CalendarService (GetCalendarView, PushItinerary)
- [ ] TravelService with Haversine formula + unit tests

### Phase 2: Agent Loop
- [ ] OllamaClient — HTTP client for /api/chat with tool-calling
- [ ] AgentService — request → tool-call → execute → respond loop
- [ ] Tool definitions matching Ollama JSON format
- [ ] Wire up system prompt + all four tools

### Phase 3: Integration & UI
- [ ] End-to-end: user request → agent → validated itinerary → calendar events
- [ ] SQLite persistence for trip history and cached locations
- [ ] Minimal web UI (Blazor or static HTML) or polish CLI interaction
- [ ] Error handling: Ollama timeouts, calendar API failures, invalid tool calls

### Phase 4: Polish
- [ ] Google Maps API integration for real travel times (optional)
- [ ] User preferences (pace, interests, budget)
- [ ] Multi-day trip optimization

## Key Decisions

- **No external agent framework** — Ollama tool-calling is sufficient
- **Haversine first** — No Google Maps API key needed for MVP
- **System.Text.Json only** — Built-in, faster, no Newtonsoft
- **SQLite** — Local-first, zero config, EF Core handles it
- **API-first** — Ollama prompt or curl initially; UI is Phase 3

## Gitignore

Ensure gitignored: `client_secret.json`, `token.json`, `*.pfx`,
`appsettings.Development.json`, `AetherPlan.db`, `bin/`, `obj/`, `.vs/`
