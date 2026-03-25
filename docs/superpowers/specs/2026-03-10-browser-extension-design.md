# SmartTripPlanner Browser Extension — Design Spec

## Goal

A Chrome extension that lets you save locations from any webpage to your SmartTripPlanner trip planner. Click the extension on a restaurant page, blog post, or travel site, and the location is saved to your ideas list or assigned to a specific trip.

## Architecture

Manifest V3 Chrome extension (works on all Chromium browsers: Chrome, Edge, Brave) communicating with new REST endpoints on the SmartTripPlanner API. The extension is a thin client — scrapes location data from the current page and sends it to the API. LLM-based extraction runs server-side as a fallback when structured scraping fails.

## Tech Stack

- **Extension:** Manifest V3, vanilla JS, Chrome APIs
- **API:** ASP.NET Core (existing SmartTripPlanner.Api project)
- **Storage:** Existing SQLite/EF Core (`CachedLocation` table, extended)
- **LLM Fallback:** `ILlmClient` (whichever provider is configured — Ollama or Claude)

---

## API Endpoints

### `POST /api/locations`

Save a location. If structured data is missing and `rawPageContent` is provided, triggers LLM extraction.

**Request:**
```json
{
  "name": "L'Astrance",
  "address": "4 Rue Beethoven, 75016 Paris",
  "latitude": 48.858,
  "longitude": 2.287,
  "category": "restaurant",
  "sourceUrl": "https://www.yelp.com/biz/lastrance-paris",
  "rawPageContent": null
}
```

**Response:** `201 Created` with saved `CachedLocation` (including generated `id`).

**LLM fallback:** When `name` is missing/empty and `rawPageContent` is provided, the API sends the content to the LLM for extraction (see LLM Extraction Contract below). If the LLM also fails to extract a name, return `422 Unprocessable Entity` with `{ "error": "Could not extract location name from page content" }`.

### `GET /api/locations?tripId={id}&unassigned=true`

List saved locations. Filter by trip assignment or get unassigned "ideas."

**Response:** Array of `CachedLocation` objects with `id`, `name`, `address`, `latitude`, `longitude`, `category`, `sourceUrl`, `tripId`, `lastUpdated`.

### `POST /api/locations/{id}/assign`

Assign a saved location to a trip.

**Request:**
```json
{
  "tripId": 1
}
```

**Response:** `200 OK` with the full updated `CachedLocation` object (same shape as save endpoint).

### Existing endpoints used

`GET /api/trip` — Returns `[{ id, destination, startDate, endDate, status }]`. The extension popup filters to trips with `status != "completed"` for the "Add to Trip" dropdown.

---

## Service Layer

New `ILocationService` / `LocationService`:

```csharp
public interface ILocationService
{
    Task<CachedLocation> SaveLocationAsync(SaveLocationRequest request);
    Task<List<CachedLocation>> GetLocationsAsync(int? tripId, bool unassignedOnly);
    Task<CachedLocation> AssignToTripAsync(int locationId, int tripId);
}
```

`SaveLocationAsync` handles the LLM fallback internally — if `Name` is empty and `RawPageContent` is provided, it calls `ILlmClient.ChatAsync` with the extraction prompt, parses the response, and saves. Otherwise delegates to `IPersistenceService` for the database write.

New `LocationsController` wires to `ILocationService`.

---

## Database Changes

Extend `CachedLocation` table with three new columns:

| Column | Type | Description |
|--------|------|-------------|
| `Address` | `string?` | Street address of the location |
| `SourceUrl` | `string?` | URL of the page the location was scraped from |
| `TripId` | `int?` | FK to `Trip`. Null = unassigned idea, set = assigned to trip |

Add `Trip` navigation property to `CachedLocation` and EF Core migration.

---

## LLM Extraction Contract

When the API receives a request with no `name` but with `rawPageContent`:

**Input validation:** `rawPageContent` is truncated to 2000 characters server-side. Requests exceeding 10,000 characters for `rawPageContent` return `400 Bad Request`.

**Prompt:**
```
Extract location information from this webpage text. Respond with ONLY a JSON object, no other text:
{"name": "...", "address": "...", "category": "..."}

Category should be one of: restaurant, hotel, attraction, bar, cafe, shop, park, museum, other.
If you cannot determine a field, use null.

Webpage text:
<rawPageContent>
```

**Parsing:** The API extracts the JSON object from the LLM response using `System.Text.Json`. If the response is not valid JSON or `name` is null/empty, return `422`.

---

## Extension Architecture

### Content Script

Runs on-demand when the popup opens (triggered via `chrome.tabs.sendMessage`). Extracts location data using a three-tier strategy:

1. **Structured data** (generic, runs on all pages):
   - `schema.org` JSON-LD (`@type: Restaurant`, `LocalBusiness`, `Place`, etc.)
   - Open Graph meta tags (`og:title`, `og:latitude`, etc.)
   - `<address>` HTML elements
   - `geo` meta tags

2. **Site-specific parsers** (pattern-match on URL hostname):
   - **Google Maps:** Extract from URL params and page structure
   - **Yelp:** Business name, address, category from page DOM
   - **TripAdvisor:** Attraction/restaurant data from page DOM

3. **LLM fallback flag:** If neither tier finds at least a `name`, the content script sends `rawPageContent` (first ~2000 chars of `document.body.innerText`) for server-side LLM extraction.

### Popup

Minimal UI shown when clicking the extension icon:

- On open, sends message to content script to extract data; shows "Scanning page..." while waiting
- Displays extracted location info: name, address, category
- Two action buttons:
  - **"Save to Ideas"** — saves with no trip assignment
  - **"Add to Trip"** — dropdown of non-completed trips (fetched from `GET /api/trip`), then saves and assigns
- Success/error notification (shown for 2 seconds)
- If nothing was extracted and LLM fallback also failed: "No location found on this page"
- If LLM fallback needed: "Analyzing page..." spinner during extraction

### Background Service Worker

Handles API communication:

- Sends extracted data to `POST /api/locations`
- If LLM fallback needed, includes `rawPageContent` in the request
- Stores API base URL (defaults to `http://localhost:5000`)

### Options Page

Single setting: API base URL (text input, defaults to `http://localhost:5000`). Stored in `chrome.storage.sync`.

---

## Extension File Structure

```
browser-extension/
├── manifest.json
├── content.js          # Page scraping logic
├── background.js       # Service worker, API communication
├── popup.html          # Popup UI
├── popup.js            # Popup logic
├── popup.css           # Popup styles
├── options.html        # Settings page
├── options.js          # Settings logic
├── parsers/
│   ├── structured.js   # schema.org, Open Graph, meta tag parsing
│   ├── google-maps.js  # Google Maps site-specific parser
│   ├── yelp.js         # Yelp site-specific parser
│   └── tripadvisor.js  # TripAdvisor site-specific parser
└── icons/
    ├── icon16.png
    ├── icon48.png
    └── icon128.png
```

Lives in the repo at `src/SmartTripPlanner.Extension/`.

**Manifest permissions:** `activeTab`, `storage`, `scripting`.

---

## Data Flow

### Happy path (structured data found)

```
1. User clicks extension on Yelp restaurant page
2. Popup opens, sends message to content script
3. Content script finds name, address, category via schema.org JSON-LD
4. Popup shows: "L'Astrance — 4 Rue Beethoven, Paris — Restaurant"
5. User clicks "Save to Ideas"
6. Service worker POSTs to /api/locations
7. API saves to CachedLocation, returns ID
8. Popup shows "Saved!" confirmation
```

### Fallback path (LLM extraction)

```
1. User clicks extension on a blog post mentioning a cafe
2. Popup opens, sends message to content script
3. Content script finds no structured data, grabs first 2000 chars of body text
4. Popup shows "Analyzing page..." spinner
5. Service worker POSTs to /api/locations with rawPageContent
6. API sends content to LLM with extraction prompt
7. LLM returns JSON with name, address, category
8. API saves to CachedLocation, returns result
9. Popup shows extracted info + "Saved!"
```

### Failure path

```
1. LLM cannot extract a name from the page content
2. API returns 422 with error message
3. Popup shows "No location found on this page"
```

### Integration with trip planning

Saved locations in `CachedLocation` are already surfaced by the `search_area` tool during agent-based trip planning. Ideas saved via the extension naturally appear as suggestions when the agent plans a trip for that area.

---

## CORS

Add a named CORS policy to `Program.cs`:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionPolicy", policy =>
        policy.SetIsOriginAllowed(origin => origin.StartsWith("chrome-extension://"))
              .WithMethods("GET", "POST")
              .AllowAnyHeader());
});
```

Note: Chrome extension origins are `chrome-extension://<extension-id>`, and the ID changes between unpacked (development) and published installs. `SetIsOriginAllowed` with a prefix check handles both.

---

## Testing

- Unit test `LocationService` with mocked `ILlmClient` and in-memory SQLite
- Test save with complete data (no LLM call)
- Test save with `rawPageContent` triggering LLM fallback
- Test LLM fallback failure (no name extracted) returns 422
- Test assign to trip updates `TripId`
- Test list with `unassigned=true` filter
- Follow naming convention: `SaveLocationAsync_WithRawContent_CallsLlmExtraction`

---

## Scope

### In scope (v1)

- `ILocationService` + `LocationsController` with 3 endpoints
- Content script with structured data scraping
- 3 site-specific parsers (Google Maps, Yelp, TripAdvisor)
- LLM fallback for unrecognized pages
- Minimal popup UI
- Extension options page (API base URL)
- `CachedLocation` schema additions (`Address`, `SourceUrl`, `TripId`) + EF migration
- CORS policy for extension origin
- Unit tests for `LocationService`

### Out of scope

- API key authentication (localhost-only for now)
- Editing extracted data in the popup
- Browser notifications
- Firefox support
- Auto-creating calendar events on save
- Rating/review data extraction
- Location deduplication
