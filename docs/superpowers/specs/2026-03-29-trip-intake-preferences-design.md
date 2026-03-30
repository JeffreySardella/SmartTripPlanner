# Trip Intake Flow & User Preferences Design

## Overview

Add a structured trip intake form, freeform-to-confirmation flow, and implicit preference learning to SmartTripPlanner. The goal is to gather better input before planning begins and personalize the experience over time.

**Approach:** Prompt-driven — the intake form and preferences feed structured context into the existing agent loop. No new backend orchestration services; the LLM remains the brain.

**Assumptions:** Single-user local app. No multi-user/auth considerations.

---

## Section 1: Trip Intake Form

The Home page presents two entry points side by side. The guided form appears as a collapsible panel that expands above the chat input area when toggled, pushing the chat messages up. When collapsed, the UI returns to the current freeform-only layout.

### Freeform (existing)
- Text box with "Describe your trip" placeholder
- Quick-start buttons remain (Tokyo, Portland, Austin)

### Guided Planning (new)
- A "Guided Planning" toggle button expands a structured form above the input area
- Fields:
  - **Destination** — text input (required)
  - **Dates** — start/end date pickers (required)
  - **Pace** — 3 toggle buttons: Relaxed / Moderate / Packed (required)
  - **Travelers** — number stepper, minimum 1 (required)
  - **Budget** — optional dropdown: Budget / Mid-range / Luxury / No preference
  - **Interests** — multi-select chips: Food, Culture, Nightlife, Outdoors, Shopping, History, Art, Adventure, Wellness
  - **Dietary restrictions** — optional text input
  - **Accessibility needs** — optional text input
  - **Must-see** — optional text input
  - **Avoid** — optional text input

### Serialization
Form submission serializes values into a structured block prepended to the agent request:

```
[TRIP REQUEST]
Destination: Tokyo
Dates: Apr 15-22, 2026
Pace: Moderate
Travelers: 2
Budget: Mid-range
Interests: Food, Culture, Nightlife
Dietary: Vegetarian
Must-see: Shibuya crossing, Tsukiji outer market
```

The system prompt is updated to recognize and use this format.

---

## Section 2: Freeform Summary Card

When the user types a freeform request, the agent's first response is a confirmation card instead of jumping into planning.

### Flow
1. User submits freeform text (e.g., "plan me a trip to Tokyo next month, love food and nightlife")
2. System prompt instructs the agent to call the `confirm_trip` tool with its parsed understanding of the request
3. `AgentService` receives the `confirm_trip` tool call, stores the parsed data, and returns a signal to the UI
4. The UI renders the tool call result as an **editable card** — same fields as the intake form, pre-filled with parsed values
5. Null/empty fields display as "Not specified" with an edit button. When dates are ambiguous or unspecified (e.g., "sometime in spring"), date fields are left empty and display "Please select dates" with date pickers in edit mode
6. Card has two actions: **"Looks good, plan it"** and **"Edit"**
7. On confirm, edited values are serialized into the `[TRIP REQUEST]` format and sent as a new agent request
8. On edit, fields become inline-editable

### Why a tool call, not a JSON text response
Using a tool (`confirm_trip`) instead of asking the LLM to emit raw JSON in its text response is far more reliable:
- The existing tool-calling pipeline already handles structured output parsing
- Avoids issues with LLMs wrapping JSON in markdown code fences or adding explanatory text
- `AgentService` can return a typed result (not just `string`) to distinguish confirmation cards from normal responses

### Key constraint
The agent does NOT begin planning until the user confirms. This prevents wasted tool calls on misunderstood requests.

---

## Section 3: User Preferences & Implicit Learning

### New tools

Four new tools added to `ToolDefinitions.cs`:

#### `save_preference`
```json
{
  "name": "save_preference",
  "description": "Save or update a user preference for future trip planning. Uses upsert semantics — if a preference with the given key already exists, updates its value and source; otherwise inserts a new row.",
  "parameters": {
    "key": { "type": "string", "description": "Preference category (e.g., pace, dietary, interests, morning_start)" },
    "value": { "type": "string", "description": "Preference value" },
    "source": { "type": "string", "enum": ["learned", "user"], "description": "Whether this was inferred from behavior or explicitly stated. This is a hint — the Settings UI always writes 'user' regardless." }
  }
}
```

#### `confirm_trip`
```json
{
  "name": "confirm_trip",
  "description": "Present a parsed trip request to the user for confirmation before planning begins. Only call this for freeform requests, not structured [TRIP REQUEST] blocks.",
  "parameters": {
    "destination": { "type": "string" },
    "dates": { "type": "string", "description": "Date range or null if ambiguous" },
    "pace": { "type": "string", "enum": ["relaxed", "moderate", "packed"], "description": "Or null if not specified" },
    "travelers": { "type": "integer" },
    "budget": { "type": "string" },
    "interests": { "type": "array", "items": { "type": "string" } },
    "dietary": { "type": "string" },
    "accessibility": { "type": "string" },
    "must_see": { "type": "array", "items": { "type": "string" } },
    "avoid": { "type": "array", "items": { "type": "string" } }
  }
}
```

#### `get_user_choice_history`
```json
{
  "name": "get_user_choice_history",
  "description": "Retrieve aggregated history of the user's past trip choices to detect patterns. Returns counts per category (e.g., pace: packed x4, moderate x1).",
  "parameters": {}
}
```

#### `delete_preference`
```json
{
  "name": "delete_preference",
  "description": "Remove a saved user preference by key.",
  "parameters": {
    "key": { "type": "string", "description": "Preference key to delete" }
  }
}
```

### Storage

Uses the existing `UserPreferences` table, extended with a `source` column:
- `"learned"` — agent inferred from patterns
- `"user"` — explicitly set by the user

**Upsert semantics:** `save_preference` checks if a row with the given key exists. If yes, updates value and source. If no, inserts a new row.

**Migration:** A new EF Core migration adds the `source` column. Existing rows default to `"user"`.

Example entries:
| Key | Value | Source |
|-----|-------|--------|
| pace | packed | learned |
| dietary | vegetarian | user |
| morning_start | late | learned |
| interests | food, nightlife, culture | learned |

### System prompt injection

The `[USER PREFERENCES]` block is **NOT** added to `system-prompt.md`. It is dynamically constructed and appended by `AgentService.RunAsync` before each request, after the static system prompt. This ensures preferences are always current (the static prompt file is cached once at startup).

```
[USER PREFERENCES]
Pace: packed (learned)
Dietary: vegetarian (set by you)
Interests: food, nightlife, culture (learned)
Morning start: late (learned)
```

The system prompt instructs the agent to use these as defaults. Explicit request values always override preferences.

### When the agent learns

The system prompt instructs the agent to:
1. After completing a trip plan, call `get_user_choice_history` to check for patterns
2. If a choice appears 3+ times (e.g., pace: packed used in 3+ trips), call `save_preference` with `source: "learned"`
3. If the user explicitly states a preference in chat ("I'm vegetarian"), call `save_preference` with `source: "user"` immediately
4. If the user says "forget that I'm vegetarian" or similar, call `delete_preference`

One-off choices are NOT saved — only patterns (3+ occurrences) or explicit statements.

### Execution path

All preference tools are handled by new methods on `IPersistenceService` / `PersistenceService`:
- `SavePreferenceAsync(string key, string value, string source)` — upsert
- `GetPreferencesAsync()` — returns all preferences
- `DeletePreferenceAsync(string key)` — delete by key
- `GetUserChoiceHistoryAsync()` — aggregates past trip choices from `TripEvents` table

`AgentService.ExecuteToolAsync` gets new `case` entries for each tool. The friendly-name mapping for progress display also gets entries (e.g., "Saving preference", "Loading choice history").

### Settings UI: "My Preferences"

A new panel on the `/settings` page:
- Lists all saved preferences in a card layout
- Each preference shows: key, value, and a tag
  - "Learned" tag (one color) for agent-inferred preferences
  - "Set by you" tag (different color) for explicit preferences
- Edit button on each preference to change the value (always saves with `source: "user"`)
- Delete button to remove it
- "Add Preference" button to manually add new ones (always `source: "user"`)

**The Settings UI calls `PersistenceService` directly — it does not go through the LLM.**

---

## Section 4: System Prompt Changes

Three additions to the **static** `system-prompt.md`:

### 1. Intake instructions (new Step 0, before existing Step 1)

> If the request is freeform text (not a structured `[TRIP REQUEST]` block), call the `confirm_trip` tool with your parsed understanding of the request. Do NOT proceed to planning until the user confirms. If the request is already a `[TRIP REQUEST]` block, skip to step 1.

### 2. Preference awareness (added to agent behavior section)

> A `[USER PREFERENCES]` block will be included in your context when available. Use these as defaults — e.g., if the user's learned pace is "packed" and the request doesn't specify pace, use packed. Explicit request values always override preferences. You can also call `delete_preference` if the user asks you to forget a preference.

### 3. Learning behavior (after final workflow step)

> After completing a trip plan, call `get_user_choice_history` to review patterns. If a choice appears in 3+ trips, call `save_preference` with source "learned". If the user explicitly states a preference in conversation, call `save_preference` with source "user" immediately. Do not save one-off choices.

No changes to existing workflow steps 1-7.

---

## Components Affected

| Component | Change |
|-----------|--------|
| `Home.razor` | Add guided planning form toggle, confirmation card rendering (from `confirm_trip` tool), confirm/edit flow |
| `Settings.razor` | Add "My Preferences" panel with CRUD (calls `PersistenceService` directly) |
| `Prompts/system-prompt.md` | Add Step 0 (confirm_trip), preference awareness text, learning behavior text |
| `Tools/ToolDefinitions.cs` | Add `confirm_trip`, `save_preference`, `delete_preference`, `get_user_choice_history` tools |
| `Services/AgentService.cs` | Dynamically append `[USER PREFERENCES]` block before each request; add tool execution cases; add progress display names; return typed result to distinguish confirmation cards from text responses |
| `Services/IPersistenceService.cs` | Add `SavePreferenceAsync`, `GetPreferencesAsync`, `DeletePreferenceAsync`, `GetUserChoiceHistoryAsync` |
| `Services/PersistenceService.cs` | Implement the above methods with upsert logic |
| `Data/SmartTripPlannerDbContext.cs` | Update `UserPreference` entity config for new `source` column |
| `Models/UserPreference.cs` | Add `Source` property (string, default `"user"`) |
| `Data/Migrations/` | New migration adding `source` column to `UserPreferences`, default `"user"` for existing rows |
