# Google Calendar OAuth Design

## Goal

Wire up Google Calendar OAuth 2.0 user consent flow so CalendarService works at runtime.

## Architecture

A `GoogleCalendarFactory` static helper handles the OAuth 2.0 flow at startup. It loads `client_secret.json` from the project content root, runs the browser-based consent flow (one time), and stores the token to a `.tokens` directory (gitignored). The resulting `Google.Apis.Calendar.v3.CalendarService` instance gets registered in DI so `CalendarService` receives a non-null `CalendarApi`.

## Components

### NuGet Package

- `Google.Apis.Auth` — provides `GoogleWebAuthorizationBroker`, `UserCredential`, `FileDataStore`

### GoogleCalendarFactory (new)

- Location: `src/AetherPlan.Api/Services/GoogleCalendarFactory.cs`
- Static class with `CreateAsync(string credentialPath, string tokenDir)` method
- Reads `client_secret.json` via `GoogleWebAuthorizationBroker.AuthorizeAsync`
- Scope: `CalendarService.Scope.Calendar`
- Uses `FileDataStore` to persist tokens in `.tokens/`
- Returns a configured `CalendarApi` instance

### Program.cs Changes

- Register `CalendarApi` via async factory method calling `GoogleCalendarFactory`
- If `client_secret.json` is missing, log warning and skip (CalendarApi stays null)
- CalendarService constructor already handles null CalendarApi gracefully

### Config

- `appsettings.json`: add `GoogleCalendar.CredentialPath` (default: `client_secret.json`)
- `.gitignore`: add `.tokens/` directory

## Data Flow

```
App starts → GoogleCalendarFactory loads client_secret.json
    → GoogleWebAuthorizationBroker opens browser (first run only)
    → User consents → Token stored to .tokens/
    → CalendarApi created and injected into DI
    → CalendarService receives real CalendarApi
    → Agent tool calls work end-to-end
```

## Error Handling

- Missing `client_secret.json`: log warning, CalendarApi registered as null, CalendarService throws on use (existing behavior)
- Expired token: `GoogleWebAuthorizationBroker` auto-refreshes via stored refresh token
- User denies consent: log error, CalendarApi stays null

## Testing

- Existing CalendarServiceTests unaffected (use TestableCalendarService override)
- New test: verify GoogleCalendarFactory handles missing credential file gracefully
- No integration test changes needed
