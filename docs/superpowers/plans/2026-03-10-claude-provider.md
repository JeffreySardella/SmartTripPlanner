# Claude API Provider Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support Claude API as an alternative LLM backend alongside Ollama, selectable via configuration.

**Architecture:** Config-based provider switch. A shared `ILlmClient` interface replaces `IOllamaClient`. Program.cs reads `Llm:Provider` from appsettings and registers either `OllamaClient` or `ClaudeClient`. AgentService is unaware of which backend is active.

**Tech Stack:** .NET 8, ASP.NET Core, System.Text.Json, xUnit, NSubstitute

**Spec:** `docs/superpowers/specs/2026-03-10-claude-provider-design.md`

**Deviation from spec:** The spec mentions using the `Anthropic` NuGet package (official C# SDK). This plan deliberately uses raw `HttpClient` instead, for consistency with the existing `OllamaClient` pattern. This avoids introducing an SDK dependency, keeps full control of JSON serialization (System.Text.Json only per project convention), and reuses the existing `FakeHttpHandler` test infrastructure. The `AetherPlan.Api.csproj` does NOT add the `Anthropic` NuGet package.

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `Models/LlmModels.cs` | Create (replaces `OllamaModels.cs`) | Shared LLM message/tool/response types |
| `Models/OllamaModels.cs` | Delete | Replaced by LlmModels.cs |
| `Services/ILlmClient.cs` | Create | Provider-agnostic LLM interface |
| `Services/IOllamaClient.cs` | Delete | Replaced by ILlmClient |
| `Services/OllamaClient.cs` | Modify | Implement `ILlmClient`, use `Llm*` types |
| `Services/ClaudeClient.cs` | Create | Claude API via HttpClient |
| `Services/AgentService.cs` | Modify | Use `ILlmClient` + `Llm*` types + pass ToolCallId |
| `Exceptions/LlmUnavailableException.cs` | Create (replaces `OllamaUnavailableException.cs`) | Generic LLM error |
| `Exceptions/OllamaUnavailableException.cs` | Delete | Replaced |
| `Tools/ToolDefinitions.cs` | Modify | Use `LlmTool`/`LlmFunction` types |
| `Program.cs` | Modify | Provider switch + config restructure |
| `appsettings.json` | Modify | Restructure under `Llm` section |
| `AetherPlan.Api.csproj` | Modify | Add `InternalsVisibleTo` for tests |
| `Tests/Services/ClaudeClientTests.cs` | Create | Claude translation + HTTP tests |
| All existing test files | Modify | Use `ILlmClient` + `Llm*` types |

---

## Chunk 1: Rename Refactoring

### Task 0: Rename Model Types

**Files:**
- Create: `src/AetherPlan.Api/Models/LlmModels.cs`
- Delete: `src/AetherPlan.Api/Models/OllamaModels.cs`
- Modify: `src/AetherPlan.Api/Services/OllamaClient.cs`
- Modify: `src/AetherPlan.Api/Services/AgentService.cs`
- Modify: `src/AetherPlan.Api/Tools/ToolDefinitions.cs`
- Modify: `src/AetherPlan.Tests/Services/AgentServiceTests.cs`
- Modify: `src/AetherPlan.Tests/Services/AgentServiceErrorTests.cs`
- Modify: `src/AetherPlan.Tests/Services/AgentServicePersistenceTests.cs`
- Modify: `src/AetherPlan.Tests/Services/OllamaClientTests.cs`
- Modify: `src/AetherPlan.Tests/Services/OllamaClientErrorTests.cs`
- Modify: `src/AetherPlan.Tests/Tools/ToolDefinitionsTests.cs`

- [ ] **Step 1: Create `LlmModels.cs` with renamed types**

Create `src/AetherPlan.Api/Models/LlmModels.cs`:

```csharp
namespace AetherPlan.Api.Models;

using System.Text.Json.Serialization;

public class LlmChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<LlmMessage> Messages { get; set; }

    [JsonPropertyName("tools")]
    public List<LlmTool>? Tools { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class LlmMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<LlmToolCall>? ToolCalls { get; set; }

    [JsonIgnore]
    public string? ToolCallId { get; set; }
}

public class LlmTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required LlmFunction Function { get; set; }
}

public class LlmFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("parameters")]
    public required object Parameters { get; set; }
}

public class LlmToolCall
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public required LlmFunctionCall Function { get; set; }
}

public class LlmFunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public required Dictionary<string, object> Arguments { get; set; }
}

public class LlmChatResponse
{
    [JsonPropertyName("message")]
    public required LlmMessage Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
```

Notes:
- `LlmMessage.ToolCallId` is `[JsonIgnore]` — used internally by ClaudeClient for tool result correlation, not serialized to Ollama.
- `LlmToolCall.Id` is nullable with `WhenWritingNull` — Ollama leaves it null, Claude populates it.

- [ ] **Step 2: Update all references across codebase**

Apply these find/replace operations across all `.cs` files in `src/`:

| Find | Replace |
|------|---------|
| `OllamaChatRequest` | `LlmChatRequest` |
| `OllamaMessage` | `LlmMessage` |
| `OllamaTool` | `LlmTool` |
| `OllamaFunction` | `LlmFunction` |
| `OllamaToolCall` | `LlmToolCall` |
| `OllamaFunctionCall` | `LlmFunctionCall` |
| `OllamaChatResponse` | `LlmChatResponse` |

Files to update (every file that imports `AetherPlan.Api.Models` and uses these types):
- `src/AetherPlan.Api/Services/OllamaClient.cs`
- `src/AetherPlan.Api/Services/AgentService.cs`
- `src/AetherPlan.Api/Tools/ToolDefinitions.cs`
- `src/AetherPlan.Tests/Services/AgentServiceTests.cs`
- `src/AetherPlan.Tests/Services/AgentServiceErrorTests.cs`
- `src/AetherPlan.Tests/Services/AgentServicePersistenceTests.cs`
- `src/AetherPlan.Tests/Services/OllamaClientTests.cs`
- `src/AetherPlan.Tests/Services/OllamaClientErrorTests.cs`
- `src/AetherPlan.Tests/Tools/ToolDefinitionsTests.cs`

- [ ] **Step 3: Delete `OllamaModels.cs`**

Delete: `src/AetherPlan.Api/Models/OllamaModels.cs`

- [ ] **Step 4: Verify tests pass**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: All 42 tests PASS

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename Ollama model types to Llm prefix

Rename all shared LLM types from Ollama* to Llm* prefix to support
multiple LLM providers. Add ToolCallId and Id fields for Claude
tool result correlation."
```

---

### Task 1: Replace IOllamaClient with ILlmClient

**Files:**
- Create: `src/AetherPlan.Api/Services/ILlmClient.cs`
- Delete: `src/AetherPlan.Api/Services/IOllamaClient.cs`
- Modify: `src/AetherPlan.Api/Services/OllamaClient.cs`
- Modify: `src/AetherPlan.Api/Services/AgentService.cs`
- Modify: `src/AetherPlan.Api/Program.cs`
- Modify: All test files that mock `IOllamaClient`

- [ ] **Step 1: Create `ILlmClient.cs`**

Create `src/AetherPlan.Api/Services/ILlmClient.cs`:

```csharp
namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface ILlmClient
{
    Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools = null);
}
```

- [ ] **Step 2: Update OllamaClient to implement ILlmClient**

In `src/AetherPlan.Api/Services/OllamaClient.cs`, change the class declaration from:

```csharp
public class OllamaClient(HttpClient httpClient, string model) : IOllamaClient
```

to:

```csharp
public class OllamaClient(HttpClient httpClient, string model) : ILlmClient
```

- [ ] **Step 3: Update AgentService constructor**

In `src/AetherPlan.Api/Services/AgentService.cs`, change from:

```csharp
public class AgentService(
    IOllamaClient ollamaClient,
    ICalendarService calendarService,
```

to:

```csharp
public class AgentService(
    ILlmClient llmClient,
    ICalendarService calendarService,
```

Also change the field reference in `RunAsync` from `ollamaClient.ChatAsync` to `llmClient.ChatAsync`.

And update the catch block message from `"Ollama is unavailable: {ex.Message}"` to `"LLM service is unavailable: {ex.Message}"`.

Also update the log message from `"Ollama is unavailable"` to `"LLM service is unavailable"`.

- [ ] **Step 4: Update Program.cs registration**

In `src/AetherPlan.Api/Program.cs`, change from:

```csharp
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>((httpClient, sp) =>
```

to:

```csharp
builder.Services.AddHttpClient<ILlmClient, OllamaClient>((httpClient, sp) =>
```

- [ ] **Step 5: Update all test mocks**

In every test file that uses `Substitute.For<IOllamaClient>()`, change to `Substitute.For<ILlmClient>()`:

- `src/AetherPlan.Tests/Services/AgentServiceTests.cs`:
  - `private readonly IOllamaClient _ollamaClient = Substitute.For<IOllamaClient>();` → `private readonly ILlmClient _llmClient = Substitute.For<ILlmClient>();`
  - `_sut = new AgentService(_ollamaClient, ...)` → `_sut = new AgentService(_llmClient, ...)`
  - All `_ollamaClient.ChatAsync(...)` → `_llmClient.ChatAsync(...)`

- `src/AetherPlan.Tests/Services/AgentServiceErrorTests.cs`: Same mock/field replacements. Also update the assertion in `RunAsync_OllamaUnavailable_ReturnsUserFriendlyMessage` from `Assert.Contains("Ollama", result)` to `Assert.Contains("LLM", result)` to match the updated error message. Rename the test method to `RunAsync_LlmUnavailable_ReturnsUserFriendlyMessage`.

- `src/AetherPlan.Tests/Services/AgentServicePersistenceTests.cs`:
  - `var ollamaClient = Substitute.For<IOllamaClient>();` → `var llmClient = Substitute.For<ILlmClient>();`
  - `var sut = new AgentService(ollamaClient, ...)` → `var sut = new AgentService(llmClient, ...)`
  - `ollamaClient.ChatAsync(...)` → `llmClient.ChatAsync(...)`

- [ ] **Step 6: Delete `IOllamaClient.cs`**

Delete: `src/AetherPlan.Api/Services/IOllamaClient.cs`

- [ ] **Step 7: Verify tests pass**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: All 42 tests PASS

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: replace IOllamaClient with ILlmClient

Create provider-agnostic ILlmClient interface. OllamaClient now
implements ILlmClient. AgentService depends on ILlmClient."
```

---

### Task 2: Rename Exception

**Files:**
- Create: `src/AetherPlan.Api/Exceptions/LlmUnavailableException.cs`
- Delete: `src/AetherPlan.Api/Exceptions/OllamaUnavailableException.cs`
- Modify: `src/AetherPlan.Api/Services/OllamaClient.cs`
- Modify: `src/AetherPlan.Api/Services/AgentService.cs`
- Modify: `src/AetherPlan.Tests/Services/AgentServiceErrorTests.cs`
- Modify: `src/AetherPlan.Tests/Services/OllamaClientErrorTests.cs`

- [ ] **Step 1: Create `LlmUnavailableException.cs`**

Create `src/AetherPlan.Api/Exceptions/LlmUnavailableException.cs`:

```csharp
namespace AetherPlan.Api.Exceptions;

public class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
```

- [ ] **Step 2: Delete `OllamaUnavailableException.cs`**

Delete: `src/AetherPlan.Api/Exceptions/OllamaUnavailableException.cs`

- [ ] **Step 3: Update all references**

Find/replace `OllamaUnavailableException` → `LlmUnavailableException` in:
- `src/AetherPlan.Api/Services/OllamaClient.cs` (3 throw sites)
- `src/AetherPlan.Api/Services/AgentService.cs` (1 catch block)
- `src/AetherPlan.Tests/Services/AgentServiceErrorTests.cs` (1 reference)
- `src/AetherPlan.Tests/Services/OllamaClientErrorTests.cs` (3 Assert.ThrowsAsync)

Note: The find/replace in `OllamaClientErrorTests.cs` will also rename the test method names themselves (e.g., `ChatAsync_ConnectionRefused_ThrowsOllamaUnavailableException` → `ChatAsync_ConnectionRefused_ThrowsLlmUnavailableException`). This is intentional.

- [ ] **Step 4: Verify tests pass**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: All 42 tests PASS

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename OllamaUnavailableException to LlmUnavailableException"
```

---

### Task 3: Restructure Configuration

**Files:**
- Modify: `src/AetherPlan.Api/appsettings.json`
- Modify: `src/AetherPlan.Api/Program.cs`

- [ ] **Step 1: Update `appsettings.json`**

Replace the `"Ollama"` section with the new `"Llm"` structure in `src/AetherPlan.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=AetherPlan.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Llm": {
    "Provider": "ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen3.5:35b-a3b-q4_K_M"
    },
    "Claude": {
      "Model": "claude-sonnet-4-6"
    }
  },
  "GoogleCalendar": {
    "CredentialPath": "client_secret.json",
    "TokenDirectory": ".tokens"
  }
}
```

- [ ] **Step 2: Update Program.cs config reads**

In `src/AetherPlan.Api/Program.cs`, update the Ollama config paths from:

```csharp
httpClient.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
...
var model = builder.Configuration["Ollama:Model"] ?? "qwen3.5:35b-a3b-q4_K_M";
```

to:

```csharp
httpClient.BaseAddress = new Uri(builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434");
...
var model = builder.Configuration["Llm:Ollama:Model"] ?? "qwen3.5:35b-a3b-q4_K_M";
```

- [ ] **Step 3: Verify tests pass**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: All 42 tests PASS

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: restructure config under Llm section

Move Ollama config to Llm:Ollama, add Llm:Provider and Llm:Claude
sections in preparation for provider switch."
```

---

## Chunk 2: Claude Provider

### Task 4: Write ClaudeClient Tests (TDD Red Phase)

**Files:**
- Modify: `src/AetherPlan.Api/AetherPlan.Api.csproj`
- Create: `src/AetherPlan.Tests/Services/ClaudeClientTests.cs`

- [ ] **Step 1: Add InternalsVisibleTo**

In `src/AetherPlan.Api/AetherPlan.Api.csproj`, add inside the `<Project>` element:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="AetherPlan.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Create ClaudeClientTests.cs**

Note: `FakeHttpHandler` is already defined in `OllamaClientTests.cs`, and `FaultyHttpHandler`/`StatusCodeHandler` are defined in `OllamaClientErrorTests.cs`. These are `internal` classes in the same test assembly and namespace, so they are accessible from `ClaudeClientTests.cs` without redeclaration.

Create `src/AetherPlan.Tests/Services/ClaudeClientTests.cs`:

```csharp
namespace AetherPlan.Tests.Services;

using System.Net;
using System.Text.Json;
using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;

public class ClaudeClientTests
{
    // --- Static helper tests ---

    [Fact]
    public void ExtractSystemPrompt_WithSystemMessage_ReturnsContent()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "Hello" }
        };

        var result = ClaudeClient.ExtractSystemPrompt(messages);

        Assert.Equal("You are helpful.", result);
    }

    [Fact]
    public void ExtractSystemPrompt_NoSystemMessage_ReturnsNull()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Hello" }
        };

        var result = ClaudeClient.ExtractSystemPrompt(messages);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertTools_MapsToClaudeFormat()
    {
        var tools = new List<LlmTool>
        {
            new()
            {
                Function = new LlmFunction
                {
                    Name = "get_weather",
                    Description = "Get weather for a location",
                    Parameters = new
                    {
                        type = "object",
                        properties = new { location = new { type = "string" } },
                        required = new[] { "location" }
                    }
                }
            }
        };

        var result = ClaudeClient.ConvertTools(tools);

        Assert.Single(result);
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("get_weather", root.GetProperty("name").GetString());
        Assert.Equal("Get weather for a location", root.GetProperty("description").GetString());
        Assert.True(root.TryGetProperty("input_schema", out var schema));
        Assert.Equal("object", schema.GetProperty("type").GetString());
    }

    [Fact]
    public void ConvertMessages_UserOnly_MapsDirectly()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = "System prompt" },
            new() { Role = "user", Content = "Hello" }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result); // system message excluded
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("Hello", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void ConvertMessages_AssistantWithToolCalls_CreatesToolUseBlocks()
    {
        var messages = new List<LlmMessage>
        {
            new()
            {
                Role = "assistant",
                Content = "Let me check.",
                ToolCalls = [new LlmToolCall
                {
                    Id = "toolu_abc",
                    Function = new LlmFunctionCall
                    {
                        Name = "get_weather",
                        Arguments = new Dictionary<string, object> { ["location"] = "Paris" }
                    }
                }]
            }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result);
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("assistant", root.GetProperty("role").GetString());
        var content = root.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_abc", content[1].GetProperty("id").GetString());
        Assert.Equal("get_weather", content[1].GetProperty("name").GetString());
    }

    [Fact]
    public void ConvertMessages_AssistantWithNullContentAndToolCalls_CreatesOnlyToolUseBlock()
    {
        var messages = new List<LlmMessage>
        {
            new()
            {
                Role = "assistant",
                Content = null,
                ToolCalls = [new LlmToolCall
                {
                    Id = "toolu_xyz",
                    Function = new LlmFunctionCall
                    {
                        Name = "search_area",
                        Arguments = new Dictionary<string, object> { ["area"] = "Tokyo" }
                    }
                }]
            }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result);
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength()); // No text block, only tool_use
        Assert.Equal("tool_use", content[0].GetProperty("type").GetString());
    }

    [Fact]
    public void ConvertMessages_ToolResults_MergedIntoUserMessage()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "tool", Content = "{\"temp\":72}", ToolCallId = "toolu_1" },
            new() { Role = "tool", Content = "{\"temp\":65}", ToolCallId = "toolu_2" }
        };

        var result = ClaudeClient.ConvertMessages(messages);

        Assert.Single(result); // Two tool messages merged into one user message
        var json = JsonSerializer.Serialize(result[0]);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("user", root.GetProperty("role").GetString());
        var content = root.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("tool_result", content[0].GetProperty("type").GetString());
        Assert.Equal("toolu_1", content[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_result", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_2", content[1].GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public void ParseResponse_TextOnly_ReturnsChatResponse()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [{ "type": "text", "text": "Hello!" }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        Assert.Equal("assistant", response.Message.Role);
        Assert.Equal("Hello!", response.Message.Content);
        Assert.Null(response.Message.ToolCalls);
        Assert.True(response.Done);
    }

    [Fact]
    public void ParseResponse_ToolUse_ReturnsToolCalls()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "text", "text": "Let me check." },
                { "type": "tool_use", "id": "toolu_abc", "name": "get_weather", "input": { "location": "Paris" } }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 20 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        Assert.Equal("Let me check.", response.Message.Content);
        Assert.NotNull(response.Message.ToolCalls);
        Assert.Single(response.Message.ToolCalls);
        Assert.Equal("toolu_abc", response.Message.ToolCalls[0].Id);
        Assert.Equal("get_weather", response.Message.ToolCalls[0].Function.Name);
        Assert.Equal("Paris", response.Message.ToolCalls[0].Function.Arguments["location"].ToString());
        Assert.True(response.Done); // Claude tool_use responses are still "done" (AgentService checks ToolCalls, not Done)
    }

    [Fact]
    public void ParseResponse_MultipleToolUse_ReturnsAllToolCalls()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "tool_use", "id": "toolu_1", "name": "get_calendar_view", "input": { "start": "2026-03-10", "end": "2026-03-12" } },
                { "type": "tool_use", "id": "toolu_2", "name": "search_area", "input": { "area": "Tokyo" } }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 30 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        Assert.NotNull(response.Message.ToolCalls);
        Assert.Equal(2, response.Message.ToolCalls.Count);
        Assert.Equal("get_calendar_view", response.Message.ToolCalls[0].Function.Name);
        Assert.Equal("search_area", response.Message.ToolCalls[1].Function.Name);
    }

    [Fact]
    public void ParseResponse_NumericInput_ParsesCorrectly()
    {
        var json = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [
                { "type": "tool_use", "id": "toolu_1", "name": "validate_travel", "input": { "from_lat": 35.6762, "from_lon": 139.6503 } }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 10, "output_tokens": 20 }
        }
        """;

        var response = ClaudeClient.ParseResponse(json);

        var args = response.Message.ToolCalls![0].Function.Arguments;
        Assert.Equal(35.6762, Convert.ToDouble(args["from_lat"]));
        Assert.Equal(139.6503, Convert.ToDouble(args["from_lon"]));
    }

    // --- HTTP integration tests ---

    [Fact]
    public async Task ChatAsync_ValidResponse_ReturnsParsedResponse()
    {
        var claudeResponse = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [{ "type": "text", "text": "Here is your plan." }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 10, "output_tokens": 5 }
        }
        """;

        var handler = new FakeHttpHandler(claudeResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-api-key");

        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "Plan a trip" }
        };

        var response = await client.ChatAsync(messages, tools: null);

        Assert.Equal("Here is your plan.", response.Message.Content);
        Assert.True(response.Done);
    }

    [Fact]
    public async Task ChatAsync_SendsRequiredHeaders()
    {
        var claudeResponse = """
        {
            "id": "msg_123",
            "type": "message",
            "role": "assistant",
            "content": [{ "type": "text", "text": "OK" }],
            "stop_reason": "end_turn",
            "usage": { "input_tokens": 1, "output_tokens": 1 }
        }
        """;

        var handler = new HeaderCapturingHandler(claudeResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "sk-test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };
        await client.ChatAsync(messages, tools: null);

        Assert.Equal("sk-test-key", handler.CapturedHeaders!["x-api-key"]);
        Assert.Equal("2023-06-01", handler.CapturedHeaders["anthropic-version"]);
    }

    [Fact]
    public async Task ChatAsync_ConnectionError_ThrowsLlmUnavailableException()
    {
        var handler = new FaultyHttpHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "test" } };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_Timeout_ThrowsLlmUnavailableException()
    {
        var handler = new FaultyHttpHandler(new TaskCanceledException("Timed out"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "test" } };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_ServerError_ThrowsLlmUnavailableException()
    {
        var handler = new StatusCodeHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var client = new ClaudeClient(httpClient, "claude-sonnet-4-6", "test-key");

        var messages = new List<LlmMessage> { new() { Role = "user", Content = "test" } };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }
}

internal class HeaderCapturingHandler(string responseJson) : HttpMessageHandler
{
    public Dictionary<string, string>? CapturedHeaders { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedHeaders = new Dictionary<string, string>();
        foreach (var header in request.Headers)
        {
            CapturedHeaders[header.Key] = string.Join(",", header.Value);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
```

- [ ] **Step 3: Verify tests fail (ClaudeClient doesn't exist yet)**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: FAIL — `ClaudeClient` type does not exist. New tests fail, existing 42 tests still pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: add ClaudeClient unit tests (red phase)

TDD red phase — tests for Claude API message translation, tool
mapping, response parsing, header verification, and error handling."
```

---

### Task 5: Implement ClaudeClient (TDD Green Phase)

**Files:**
- Create: `src/AetherPlan.Api/Services/ClaudeClient.cs`
- Modify: `src/AetherPlan.Api/Services/AgentService.cs` (pass ToolCallId)

- [ ] **Step 1: Create `ClaudeClient.cs`**

Create `src/AetherPlan.Api/Services/ClaudeClient.cs`:

```csharp
namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;

public class ClaudeClient(HttpClient httpClient, string model, string apiKey) : ILlmClient
{
    public async Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 4096,
            ["messages"] = ConvertMessages(messages)
        };

        var systemPrompt = ExtractSystemPrompt(messages);
        if (!string.IsNullOrEmpty(systemPrompt))
            requestBody["system"] = systemPrompt;

        if (tools is { Count: > 0 })
            requestBody["tools"] = ConvertTools(tools);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
            request.Content = content;
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            response = await httpClient.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException("Cannot connect to Claude API.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LlmUnavailableException("Claude API request timed out.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new LlmUnavailableException(
                $"Claude API returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return ParseResponse(responseJson);
    }

    internal static string? ExtractSystemPrompt(List<LlmMessage> messages)
    {
        var systemMessages = messages.Where(m => m.Role == "system").ToList();
        if (systemMessages.Count == 0) return null;
        return string.Join("\n", systemMessages.Select(m => m.Content));
    }

    internal static List<object> ConvertMessages(List<LlmMessage> messages)
    {
        var result = new List<object>();
        var nonSystem = messages.Where(m => m.Role != "system").ToList();

        var i = 0;
        while (i < nonSystem.Count)
        {
            var msg = nonSystem[i];

            if (msg.Role == "assistant" && msg.ToolCalls is { Count: > 0 })
            {
                var contentBlocks = new List<object>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contentBlocks.Add(new { type = "text", text = msg.Content });

                foreach (var tc in msg.ToolCalls)
                {
                    contentBlocks.Add(new
                    {
                        type = "tool_use",
                        id = tc.Id ?? $"toolu_{Guid.NewGuid():N}",
                        name = tc.Function.Name,
                        input = tc.Function.Arguments
                    });
                }

                result.Add(new { role = "assistant", content = contentBlocks });
                i++;
            }
            else if (msg.Role == "tool")
            {
                // Collect consecutive tool messages into one user message
                var toolResults = new List<object>();
                while (i < nonSystem.Count && nonSystem[i].Role == "tool")
                {
                    var toolMsg = nonSystem[i];
                    toolResults.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = toolMsg.ToolCallId ?? "",
                        content = toolMsg.Content ?? ""
                    });
                    i++;
                }

                result.Add(new { role = "user", content = toolResults });
            }
            else
            {
                result.Add(new { role = msg.Role, content = msg.Content ?? "" });
                i++;
            }
        }

        return result;
    }

    internal static List<object> ConvertTools(List<LlmTool> tools)
    {
        return tools.Select(t => (object)new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            input_schema = t.Function.Parameters
        }).ToList();
    }

    internal static LlmChatResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("content", out var content))
        {
            return new LlmChatResponse
            {
                Message = new LlmMessage { Role = "assistant", Content = null },
                Done = true
            };
        }

        string? textContent = null;
        List<LlmToolCall>? toolCalls = null;

        foreach (var block in content.EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();

            if (type == "text")
            {
                textContent = (textContent ?? "") + block.GetProperty("text").GetString();
            }
            else if (type == "tool_use")
            {
                toolCalls ??= [];
                var args = new Dictionary<string, object>();
                var input = block.GetProperty("input");
                foreach (var prop in input.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText()
                    };
                }

                toolCalls.Add(new LlmToolCall
                {
                    Id = block.GetProperty("id").GetString(),
                    Function = new LlmFunctionCall
                    {
                        Name = block.GetProperty("name").GetString()!,
                        Arguments = args
                    }
                });
            }
        }

        return new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = textContent,
                ToolCalls = toolCalls
            },
            Done = true
        };
    }
}
```

- [ ] **Step 2: Update AgentService to pass ToolCallId**

In `src/AetherPlan.Api/Services/AgentService.cs`, update the tool result message creation in `RunAsync` from:

```csharp
messages.Add(new LlmMessage
{
    Role = "tool",
    Content = JsonSerializer.Serialize(result)
});
```

to:

```csharp
messages.Add(new LlmMessage
{
    Role = "tool",
    Content = JsonSerializer.Serialize(result),
    ToolCallId = toolCall.Id
});
```

This passes the tool call ID through. `toolCall.Id` is null for Ollama responses (Ollama doesn't set tool call IDs) and populated for Claude responses. The AgentService change is provider-agnostic — it always assigns `toolCall.Id`, which is harmless when null.

- [ ] **Step 3: Verify all tests pass**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: ALL tests PASS (42 existing + 16 new ClaudeClient tests = 58 total)

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add ClaudeClient with message/tool translation

Implement ClaudeClient as ILlmClient using HttpClient to call the
Claude API. Translates between internal LlmMessage format and Claude
API format (system prompt extraction, tool_use/tool_result blocks,
message role mapping). Pass ToolCallId through AgentService."
```

---

### Task 6: Provider Switch in Program.cs

**Files:**
- Modify: `src/AetherPlan.Api/Program.cs`

- [ ] **Step 1: Replace the hardcoded OllamaClient registration with provider switch**

In `src/AetherPlan.Api/Program.cs`, replace the entire `AddHttpClient<ILlmClient, OllamaClient>` block with:

```csharp
var llmProvider = builder.Configuration["Llm:Provider"]?.ToLowerInvariant() ?? "ollama";

if (llmProvider == "claude")
{
    var claudeApiKey = builder.Configuration["Claude:ApiKey"]
        ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");

    if (string.IsNullOrEmpty(claudeApiKey))
    {
        Log.Error("Claude provider selected but no API key found. Set Claude:ApiKey via dotnet user-secrets or CLAUDE_API_KEY environment variable.");
        throw new InvalidOperationException("Claude API key not configured.");
    }

    var claudeModel = builder.Configuration["Llm:Claude:Model"] ?? "claude-sonnet-4-6";
    Log.Information("LLM Provider: Claude ({Model})", claudeModel);

    builder.Services.AddHttpClient<ILlmClient, ClaudeClient>((httpClient, sp) =>
    {
        httpClient.BaseAddress = new Uri("https://api.anthropic.com");
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        return new ClaudeClient(httpClient, claudeModel, claudeApiKey);
    });
}
else
{
    var ollamaBaseUrl = builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
    var ollamaModel = builder.Configuration["Llm:Ollama:Model"] ?? "qwen3.5:35b-a3b-q4_K_M";
    Log.Information("LLM Provider: Ollama ({Model}) at {BaseUrl}", ollamaModel, ollamaBaseUrl);

    builder.Services.AddHttpClient<ILlmClient, OllamaClient>((httpClient, sp) =>
    {
        httpClient.BaseAddress = new Uri(ollamaBaseUrl);
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        return new OllamaClient(httpClient, ollamaModel);
    });
}
```

- [ ] **Step 2: Verify tests pass**

Run: `dotnet test src/AetherPlan.Tests/AetherPlan.Tests.csproj`
Expected: ALL tests PASS

- [ ] **Step 3: Verify the app builds and starts (smoke test)**

Run: `dotnet build src/AetherPlan.Api/AetherPlan.Api.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add config-based LLM provider switch

Program.cs reads Llm:Provider from config and registers either
OllamaClient or ClaudeClient. Claude API key resolved from
dotnet user-secrets (Claude:ApiKey) or CLAUDE_API_KEY env var.
Defaults to Ollama when provider is unset."
```

---

## Usage Notes for Implementer

### Switching to Claude

1. Set the API key:
   ```bash
   cd src/AetherPlan.Api
   dotnet user-secrets set "Claude:ApiKey" "sk-ant-your-key-here"
   ```
   Or set environment variable: `CLAUDE_API_KEY=sk-ant-your-key-here`

2. Update `appsettings.json` (or use environment override):
   ```json
   { "Llm": { "Provider": "claude" } }
   ```

3. Optionally change the model:
   ```json
   { "Llm": { "Claude": { "Model": "claude-opus-4-6" } } }
   ```

### What Does NOT Change

- AgentService loop logic
- Tool definitions (content)
- CalendarService, TravelService, PersistenceService
- Blazor UI
- EF Core / SQLite
