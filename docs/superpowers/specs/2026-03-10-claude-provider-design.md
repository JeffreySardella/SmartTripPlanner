# Claude API Provider Design Spec

**Goal:** Support Claude API as an alternative LLM backend alongside Ollama, selectable via configuration.

**Architecture:** Config-based provider switch. A shared `ILlmClient` interface replaces `IOllamaClient`. Program.cs reads `Llm:Provider` from appsettings and registers either `OllamaClient` or `ClaudeClient`. AgentService is unaware of which backend is active.

## Provider Selection

```
appsettings.json: "Llm": { "Provider": "ollama" | "claude" }

                 ┌──────────┐
AgentService --> | ILlmClient |
                 └─────┬────┘
                       │
              ┌────────┴────────┐
              │                 │
        OllamaClient     ClaudeClient
        (localhost)      (api.anthropic.com)
```

Program.cs reads `Llm:Provider` and registers the matching implementation as a singleton. Invalid or missing provider values default to `ollama`.

## ILlmClient Interface

Replaces `IOllamaClient`. Same method signature:

```csharp
public interface ILlmClient
{
    Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools = null);
}
```

Internal message/tool models stay the same shape (renamed from `Ollama*` to `Llm*` prefix). Both clients translate to/from their respective API formats internally.

## Model Renaming

| Old Name | New Name |
|----------|----------|
| `IOllamaClient` | `ILlmClient` |
| `OllamaMessage` | `LlmMessage` |
| `OllamaTool` / `OllamaFunction` | `LlmTool` / `LlmFunction` |
| `OllamaToolCall` / `OllamaFunctionCall` | `LlmToolCall` / `LlmFunctionCall` |
| `OllamaChatRequest` | `LlmChatRequest` |
| `OllamaChatResponse` | `LlmChatResponse` |
| `OllamaUnavailableException` | `LlmUnavailableException` |

Files referencing these names update accordingly (AgentService, tests, tool definitions, exception handler).

## ClaudeClient

Uses the `Anthropic` NuGet package (official C# SDK). Responsibilities:

1. **Translate outbound:** Convert `LlmMessage` list + `LlmTool` list into Anthropic SDK request format (system prompt extracted from messages, tools mapped to Claude tool schema)
2. **Call API:** `POST https://api.anthropic.com/v1/messages` via SDK
3. **Translate inbound:** Convert Claude response (content blocks with `text` / `tool_use` types) back to `LlmChatResponse` with `LlmToolCall` list
4. **Error handling:** Map Anthropic SDK exceptions to `LlmUnavailableException`

### Tool Format Translation

Internal tool format (JSON Schema with `properties`, `required`) maps directly to Claude's tool input schema. ClaudeClient serializes `LlmTool` → Claude `Tool` objects.

### Message Role Mapping

| Internal Role | Claude Role | Notes |
|--------------|-------------|-------|
| `system` | Extracted to `system` parameter | Not sent as a message |
| `user` | `user` | Direct mapping |
| `assistant` | `assistant` | Tool calls become `tool_use` content blocks |
| `tool` | `user` with `tool_result` content block | Claude expects tool results as user messages |

## Configuration

```json
{
  "Llm": {
    "Provider": "ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen3.5:35b-a3b-q4_K_M"
    },
    "Claude": {
      "Model": "claude-sonnet-4-6"
    }
  }
}
```

### API Key Storage

Claude API key resolution order:
1. `dotnet user-secrets` — `Claude:ApiKey`
2. Environment variable — `CLAUDE_API_KEY`

If neither is set and provider is `claude`, log an error and throw at startup.

## OllamaClient Changes

- Rename class file but keep HTTP logic identical
- Implements `ILlmClient` instead of `IOllamaClient`
- Uses renamed model types (`LlmMessage`, `LlmTool`, etc.)
- Reads config from `Llm:Ollama:BaseUrl` and `Llm:Ollama:Model`

## AgentService Changes

- Constructor: `IOllamaClient` → `ILlmClient`
- All `OllamaMessage` → `LlmMessage`, etc.
- No logic changes — the agent loop is provider-agnostic

## Test Changes

- All mocks: `Substitute.For<IOllamaClient>()` → `Substitute.For<ILlmClient>()`
- All model types: `OllamaMessage` → `LlmMessage`, etc.
- Add `ClaudeClientTests` with unit tests for format translation
- Existing AgentService/error tests unchanged in logic

## Files Changed

| File | Action |
|------|--------|
| `Services/ILlmClient.cs` | Create (new interface) |
| `Models/OllamaModels.cs` → `Models/LlmModels.cs` | Rename file + all types |
| `Services/OllamaClient.cs` | Update to implement `ILlmClient`, read from `Llm:Ollama` config |
| `Services/ClaudeClient.cs` | Create (new implementation) |
| `Exceptions/OllamaUnavailableException.cs` → `LlmUnavailableException.cs` | Rename |
| `Services/AgentService.cs` | Update types |
| `Tools/ToolDefinitions.cs` | Update types |
| `Program.cs` | Provider switch logic, config restructure |
| `appsettings.json` | Restructure under `Llm` section |
| `SmartTripPlanner.Api.csproj` | Add `Anthropic` NuGet |
| All test files | Update mocked interface + model types |
| `ClaudeClientTests.cs` | Create (new tests) |

## What Does NOT Change

- AgentService loop logic
- Tool definitions (content)
- CalendarService, TravelService, PersistenceService
- Blazor UI
- EF Core / SQLite
