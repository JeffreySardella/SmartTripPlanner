# SmartTripPlanner

Local smart trip planner that uses an AI agent to research destinations, check your Google Calendar for availability, validate travel times, and push optimized itineraries directly to your calendar.

Runs entirely on your hardware — no cloud AI services required.

## How It Works

```
You: "Plan a 3-day trip to Tokyo next month"
        │
        ▼
┌─────────────────────┐
│  Agent Loop          │  ASP.NET Core + Blazor Server
│  (AgentService)      │
├─────────────────────┤
│  1. Check calendar   │  → Google Calendar API
│  2. Research area    │  → LLM knowledge
│  3. Validate travel  │  → Haversine distance calc
│  4. Create events    │  → Google Calendar + SQLite
└────────┬────────────┘
         │ HTTP localhost:11434
         ▼
┌─────────────────────┐
│  Ollama              │  Qwen 3.5 35B-A3B (local)
│  (tool-calling)      │
└─────────────────────┘
```

The agent loop sends your request to a local LLM with tool definitions. The LLM decides which tools to call (check calendar, validate travel times, create events), the app executes them, feeds results back, and repeats until the itinerary is complete.

## Features

- **Chat UI** — Blazor Server interface at `/` for natural language trip planning
- **Trip History** — Browse past trips and their events at `/trips`
- **Google Calendar Integration** — Reads free/busy slots and creates events
- **Travel Validation** — Haversine formula estimates travel feasibility between locations
- **Local LLM** — Ollama with Qwen 3.5 35B-A3B MoE model, no API keys needed
- **REST API** — `POST /api/trip/plan`, `GET /api/trip`, `GET /api/trip/{id}`
- **Location Cache** — Cache-first area search with 30-day TTL, auto-caches locations from trip events
- **SQLite Persistence** — Trip history and cached locations stored locally via EF Core

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Ollama](https://ollama.com/) with a tool-calling capable model
- AMD GPU with ROCm **or** NVIDIA GPU with CUDA (for local LLM inference)
- Google Cloud project with Calendar API enabled (optional — for calendar features)

## Quick Start

### 1. Clone and build

```bash
git clone https://github.com/JeffreySardella/SmartTripPlanner.git
cd SmartTripPlanner
dotnet build SmartTripPlanner.sln
```

### 2. Install Ollama and pull the model

```bash
# Install Ollama (Linux/WSL)
curl -fsSL https://ollama.com/install.sh | sh

# Or download from https://ollama.com for Windows/Mac

# Pull the MoE model (~21GB VRAM)
ollama pull qwen3.5:35b-a3b-q4_K_M
```

If you have less VRAM, use a smaller quant:
```bash
ollama pull qwen3.5:35b-a3b-q4_K_S
```

Then update `Llm:Ollama:Model` in `src/SmartTripPlanner.Api/appsettings.json` to match.

### 3. Set up the database

```bash
# Install EF Core tool if you don't have it
dotnet tool install --global dotnet-ef

# Apply migrations to create the SQLite database
dotnet ef database update --project src/SmartTripPlanner.Api
```

### 4. Set up Google Calendar (optional)

Without this step, the app runs but calendar features are disabled. The agent will still respond and plan trips, but won't read or write to Google Calendar.

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a project and enable the **Google Calendar API**
3. Go to **Credentials** > **Create Credentials** > **OAuth client ID**
4. Choose **Desktop application** as the application type
5. Download the JSON file and save it as `client_secret.json` in the project root

On first run, your browser will open for OAuth consent. The token is stored locally in `.tokens/` and auto-refreshes — you only need to authorize once.

### 5. Run

```bash
# Make sure Ollama is running first
ollama serve

# In another terminal, start SmartTripPlanner
dotnet run --project src/SmartTripPlanner.Api
```

Open **http://localhost:5197** in your browser. You'll see the chat interface — type something like "Plan a weekend trip to Portland" and the agent will start working.

### 6. Run tests

```bash
dotnet test SmartTripPlanner.sln
```

78 unit tests covering all services, the agent loop, and API endpoints. Tests don't require Ollama or Google Calendar.

## Project Structure

```
SmartTripPlanner/
├── src/
│   ├── SmartTripPlanner.Api/
│   │   ├── Components/          # Blazor Server UI
│   │   │   ├── Pages/
│   │   │   │   ├── Home.razor   # Chat interface (/)
│   │   │   │   └── Trips.razor  # Trip history (/trips)
│   │   │   └── Layout/          # App shell and nav
│   │   ├── Controllers/         # REST API endpoints
│   │   ├── Services/
│   │   │   ├── AgentService     # Ollama agent loop
│   │   │   ├── CalendarService  # Google Calendar CRUD
│   │   │   ├── TravelService    # Haversine distance calc
│   │   │   ├── PersistenceService # SQLite data access
│   │   │   └── OllamaClient    # HTTP client for Ollama
│   │   ├── Models/              # Domain models and DTOs
│   │   ├── Data/                # EF Core DbContext + migrations
│   │   ├── Tools/               # LLM tool definitions
│   │   └── Program.cs
│   └── SmartTripPlanner.Tests/        # xUnit test suite
├── docs/plans/                  # Design and implementation plans
├── CLAUDE.md                    # Project spec for AI assistants
└── SmartTripPlanner.sln
```

## Configuration

All config is in `src/SmartTripPlanner.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=SmartTripPlanner.db"
  },
  "Llm": {
    "Provider": "ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen3.5:35b-a3b-q4_K_M"
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionStrings:DefaultConnection` | `Data Source=SmartTripPlanner.db` | SQLite database path |
| `Llm:Provider` | `ollama` | LLM provider (`ollama` or `claude`) |
| `Llm:Ollama:BaseUrl` | `http://localhost:11434` | Ollama API endpoint |
| `Llm:Ollama:Model` | `qwen3.5:35b-a3b-q4_K_M` | Model to use (any tool-calling model works) |

## API Endpoints

The REST API coexists with the Blazor UI. Use it for scripting or external integrations.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/trip/plan` | Send a natural language trip request. Body: `{ "prompt": "..." }` |
| `GET` | `/api/trip` | List all saved trips |
| `GET` | `/api/trip/{id}` | Get a trip with its events |

Example:
```bash
curl -X POST http://localhost:5197/api/trip/plan \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Plan a day trip to Seattle this Saturday"}'
```

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 8 / ASP.NET Core |
| UI | Blazor Server |
| LLM | Ollama (local inference) |
| Model | Qwen 3.5 35B-A3B (MoE, q4_K_M) |
| Calendar | Google Calendar API v3 |
| Database | SQLite + EF Core |
| Logging | Serilog |
| Testing | xUnit + NSubstitute |

## Hardware

Tested on AMD Radeon RX 7900 XTX (24GB VRAM) with ROCm. The Qwen 3.5 35B-A3B model at q4_K_M uses ~21GB VRAM. Ollama auto-detects ROCm.

For NVIDIA GPUs, Ollama uses CUDA automatically. Any GPU with 16GB+ VRAM should work with a smaller quantization.

## Troubleshooting

**Ollama connection refused:** Make sure Ollama is running (`ollama serve`) before starting the app.

**Out of VRAM:** Switch to a smaller quant (`q4_K_S` or `q3_K_M`) in appsettings.json.

**Calendar features not working:** Check that `client_secret.json` exists in the project root and that you've completed the OAuth consent flow.

**Build errors on .NET 10 SDK:** The project targets .NET 8. If you have .NET 10 installed, it should still work via the `net8.0` target framework. If not, install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## License

MIT
