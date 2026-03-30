# SmartTripPlanner - Local Smart Trip Planner

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

The agent loop lives in C#: send user request + tool definitions to Ollama,
receive tool calls back, execute them, return results, repeat until done.

## Project Structure

- **Controllers/**
  - `TripController.cs` - API endpoints for trip planning and management
- **Services/**
  - `ICalendarService.cs`, `CalendarService.cs` - Google Calendar OAuth and CRUD
  - `ITravelService.cs`, `TravelService.cs` - Haversine calculations and validation
  - `IOlammaClient.cs`, `OllamaClient.cs` - LLM communication and tool-calling
  - `IAgentService.cs`, `AgentService.cs` - Core agent loop orchestration
- **Models/**
  - `Trip.cs`, `TripEvent.cs`, `CachedLocation.cs`, `UserPreferences.cs` - EF Core entities
  - `TripRequest.cs`, `TripResponse.cs` - API DTOs
- **Agents/**
  - `SystemPrompt.cs` - Static system prompt configuration
- **Utils/**
  - `HaversineCalculator.cs`, `TimeRangeParser.cs`
- **Tests/**
  - Unit and integration tests for services, agent loop, and validation logic

## Coding Conventions

- C# 12, nullable reference types enabled, file-scoped namespaces
- Async/await throughout - all I/O methods return `Task<T>`
- Dependency injection via built-in .NET DI container
- Configuration via `appsettings.json` + `dotnet user-secrets` for credentials
- **System.Text.Json only** - no Newtonsoft.Json
- One class per file, interfaces per service (`ICalendarService`, `ITravelService`, etc.)
- xUnit for tests, naming: `MethodName_Scenario_ExpectedResult`
- Serilog structured logging - log agent loop iterations, tool calls, and errors

## API Endpoints

### `POST /api/trip/plan`
Primary endpoint - kicks off the agent loop for a new trip request.

**Request body:**
- `destination` (required): City/region name
- `startDate` / `endDate` (required): ISO 8601 date strings
- `preferences` (optional): Interest tags to guide `search_area` calls
- `pace` (optional): `"relaxed"` | `"moderate"` | `"packed"` - controls activity density

**Response:** Streamed via Server-Sent Events (SSE). Each SSE event is one of:
- `tool_call` - agent invoked a tool (name + arguments)
- `tool_result` - tool execution result fed back to the agent
- `itinerary` - final validated itinerary with calendar event IDs
- `error` - structured error (Ollama timeout, calendar auth failure, etc.)

In **degraded mode** (no calendar access), the `itinerary` event contains the full plan as structured JSON without `calendarEventId` fields, suitable for manual entry.

### `GET /api/trip`
Lists saved trips with pagination. Query params: `?page=1&pageSize=10&status=planned`.
Returns: `{ items: Trip[], totalCount: int, page: int }`.

### `GET /api/trip/{id}`
Returns a single trip with all `TripEvents`, travel validations, and Google Calendar deep links.

### `DELETE /api/trip/{id}`
Cancels a trip: removes associated Google Calendar events via batch delete, updates trip status to `cancelled`. Returns `204 No Content` on success.

### `GET /api/health`
Returns system readiness: Ollama connectivity (model loaded, VRAM usage), Google Calendar auth status (valid/expired/missing), and SQLite database health. Used by the UI for status indicators.

## Build & Run

1. **Prerequisites:** .NET 8 SDK, Ollama, SQLite
2. **Install Dependencies:** `dotnet restore`
3. **Configure Secrets:** `dotnet user-secrets set "GoogleCalendar:ClientId" "YOUR_ID"`
4. **Start Ollama:** `ollama run qwen3.5-35b-a3b` (ensure VRAM availability)
5. **Run API:** `dotnet run` (launches API on http://localhost:5000)

## AMD GPU / Ollama Setup

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

1. **get_calendar_view** - Free/busy blocks for a date range
2. **validate_travel** - Can you get from A to B in the available time?
3. **add_trip_event** - Create a Google Calendar event with location + description
4. **search_area** - LLM uses internal knowledge to suggest attractions/restaurants

## Agent System Prompt

> You are a professional travel logistician. Maximize sightseeing while
> minimizing travel fatigue. You have access to the user's Google Calendar.
> When a trip is requested, check for free slots, research the area, and
> only commit events once you've verified travel times between locations.

## Agent Workflow

When processing a trip request, the agent follows this ordered sequence:

0. **Confirm freeform requests** - If the user's message is freeform text (NOT a structured `[TRIP REQUEST]` block), call the `confirm_trip` tool with your parsed understanding. Extract destination, dates, pace, budget, interests, dietary needs, accessibility requirements, must-see spots, and things to avoid. Leave fields null if not mentioned or ambiguous. Do NOT proceed to step 1 until the user confirms. If the request IS a `[TRIP REQUEST]` block, skip directly to step 1.
1. **Parse request** - Extract destination, dates, preferences, and pace from the user input. Note any budget mentions or scheduling constraints.
2. **Check calendar** - Call `get_calendar_view` for the requested date range to identify free/busy blocks. Never schedule over existing events.
3. **Research area** - Call `search_area` for the destination, filtering by user preferences (food, temples, nightlife, etc.). Use cached results when available (30-day TTL). Return location-specific recommendations - name real neighborhoods, districts, and areas rather than generic suggestions.
4. **Build daily itinerary** - For each day of the trip:
   - Allocate morning (9am-12pm), afternoon (1pm-5pm), and evening (6pm-9pm) blocks by default.
   - Adjust density for pace: `relaxed` = 2-3 activities/day, `moderate` = 4-5, `packed` = 6+.
   - Respect day boundaries - activities end by 10pm, next day starts no earlier than 8am.
   - Slot meals at logical times: breakfast 8-10am, lunch 11:30am-1:30pm, dinner 6-8pm.
5. **Validate travel** - Call `validate_travel` between each pair of consecutive locations. If travel time exceeds the gap between events, adjust the schedule (swap order, remove an activity, or extend the gap). Always add 20% buffer to Haversine estimates.
6. **Confirm non-overlap** - Verify no events overlap with each other or with existing calendar events from step 2. If any conflict exists, return to step 4.
7. **Commit events** - Call `add_trip_event` for each validated activity with full event details (summary, location, start, end, description with travel time from previous stop). If calendar is unavailable, return the complete itinerary as structured JSON (degraded mode) for manual entry.

The agent iterates steps 4-6 if validation fails, up to the 10-round tool-call cap.

## User Preferences

A `[USER PREFERENCES]` block will be included in your context when available. These are the user's saved preferences — use them as defaults when the request doesn't specify a value. For example, if the user's learned pace is "packed" and the request doesn't mention pace, use packed. Explicit request values ALWAYS override preferences.

You can manage preferences conversationally:
- If the user asks to forget a preference, call `delete_preference` with the key.
- If the user explicitly states a new preference (e.g., "I'm vegetarian"), call `save_preference` with source "user".

## Learning from Patterns

After completing a trip plan, call `get_user_choice_history` to review the user's past trip patterns. If any choice appears in 3 or more trips (e.g., pace "packed" used 3+ times), call `save_preference` with source "learned" to remember it. Do NOT save one-off choices — only consistent patterns across multiple trips.

## SQLite Schema (key tables)

- **Trips** - id, destination, start_date, end_date, status, created_at
- **TripEvents** - id, trip_id, summary, location, lat, lng, start, end, calendar_event_id
- **CachedLocations** - id, name, lat, lng, category, last_updated
- **UserPreferences** - id, key, value

## Testing Strategy

- Mock `IOllamaClient` to test agent loop without running Ollama
- Use in-memory SQLite for database tests
- Integration tests hit real Ollama (marked `[Trait("Category", "Integration")]`)
- Test Haversine calculations with known city-pair distances

## Error Handling & Resilience

- **Ollama connection failures:** Retry up to 3 times with exponential backoff (1s, 2s, 4s). If Ollama is unreachable after retries, return a structured error with setup instructions. Timeout per request: 120s (LLM inference can be slow on large prompts).
- **Calendar auth failures / degraded mode:** When Google Calendar is unavailable (OAuth expired, no credentials, network error), the agent switches to **degraded mode** - it still researches locations via `search_area`, validates travel via `validate_travel`, and produces a complete itinerary as structured JSON, but skips all `add_trip_event` calls. The response includes the full itinerary for manual calendar entry.
- **Invalid tool calls:** If Ollama emits a tool call with missing or malformed parameters, respond with a `tool` role message containing the validation error so the LLM can self-correct. Cap total tool-call rounds at 10 per request to prevent infinite loops.
- **Calendar event format:** Each `add_trip_event` call must include: `summary` (activity name), `location` (address or place name with lat/lng), `start` (ISO 8601 datetime with timezone), `end` (ISO 8601 datetime with timezone), and `description` (travel notes, estimated travel time from previous stop). Events must not overlap - the agent validates time blocks before committing.
- **Travel time honesty:** Always label Haversine-derived times as estimates (e.g., "~45 min estimated driving"). Never present calculated estimates as exact times. Add a 20% buffer to Haversine results to account for real-world routing. When Google Maps API is configured, prefer it for accuracy.
- **Location cache (30-day TTL):** CachedLocations entries expire after 30 days based on `last_updated`. On cache miss or expiry, the agent calls `search_area` to repopulate. Stale entries are soft-deleted to allow offline fallback. Cache hits skip redundant LLM calls for previously researched areas.

## Implementation Phases

### Phase 1: Foundation
- [ ] Initialize ASP.NET Core Web API + solution + SQLite/EF Core
- [ ] Google Calendar OAuth + CalendarService (GetCalendarView, PushItinerary)
- [ ] TravelService with Haversine formula + unit tests

### Phase 2: Agent Loop
- [ ] OllamaClient - HTTP client for /api/chat with tool-calling
- [ ] AgentService - request - tool-call - execute - respond loop
- [ ] Tool definitions matching Ollama JSON format
- [ ] Wire up system prompt + all four tools

### Phase 3: Integration & UI
- [ ] End-to-end: user request - agent - validated itinerary - calendar events
- [ ] SQLite persistence for trip history and cached locations
- [ ] Minimal web UI (Blazor or static HTML) or polish CLI interaction
- [ ] Error handling: Ollama timeouts, calendar API failures, invalid tool calls

### Phase 4: Polish
- [ ] Google Maps API integration for real travel times (optional)
- [ ] User preferences (pace, interests, budget)
- [ ] Multi-day trip optimization

## Key Decisions

- **No external agent framework** - Ollama tool-calling is sufficient
- **Haversine first** - No Google Maps API key needed for MVP
- **System.Text.Json only** - Built-in, faster, no Newtonsoft
- **SQLite** - Local-first, zero config, EF Core handles it
- **API-first** - Ollama prompt or curl initially; UI is Phase 3

## Gitignore

Ensure gitignored: `client_secret.json`, `token.json`, `*.pfx`,
`appsettings.Development.json`, `SmartTripPlanner.db`, `bin/`, `obj/`, `.vs/`
