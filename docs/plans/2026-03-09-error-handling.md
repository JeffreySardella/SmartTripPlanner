# Error Handling Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add resilience so tool failures, Ollama outages, and bad LLM arguments are handled gracefully instead of crashing.

**Architecture:** Services throw on failure. OllamaClient throws typed `OllamaUnavailableException`. AgentService catches tool errors and feeds them back to Ollama as error objects. TripController catches all exceptions and returns structured HTTP responses.

**Tech Stack:** .NET 8, xUnit, NSubstitute (no new dependencies)

---

## Task 0: OllamaUnavailableException

**Files:**
- Create: `src/SmartTripPlanner.Api/Exceptions/OllamaUnavailableException.cs`
- Modify: `src/SmartTripPlanner.Api/Services/OllamaClient.cs`
- Create: `src/SmartTripPlanner.Tests/Services/OllamaClientErrorTests.cs`

**Step 1: Write the failing test**

```csharp
// src/SmartTripPlanner.Tests/Services/OllamaClientErrorTests.cs
namespace SmartTripPlanner.Tests.Services;

using System.Net;
using SmartTripPlanner.Api.Exceptions;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;

public class OllamaClientErrorTests
{
    [Fact]
    public async Task ChatAsync_ConnectionRefused_ThrowsOllamaUnavailableException()
    {
        var handler = new FaultyHttpHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "test-model");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await Assert.ThrowsAsync<OllamaUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_Timeout_ThrowsOllamaUnavailableException()
    {
        var handler = new FaultyHttpHandler(new TaskCanceledException("Request timed out"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "test-model");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await Assert.ThrowsAsync<OllamaUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_ServerError_ThrowsOllamaUnavailableException()
    {
        var handler = new StatusCodeHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "test-model");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await Assert.ThrowsAsync<OllamaUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }
}

internal class FaultyHttpHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw exception;
    }
}

internal class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/SmartTripPlanner.Tests --filter "FullyQualifiedName~OllamaClientErrorTests" -v minimal
```

Expected: FAIL — `OllamaUnavailableException` does not exist.

**Step 3: Create the exception**

```csharp
// src/SmartTripPlanner.Api/Exceptions/OllamaUnavailableException.cs
namespace SmartTripPlanner.Api.Exceptions;

public class OllamaUnavailableException : Exception
{
    public OllamaUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
```

**Step 4: Add error handling to OllamaClient.ChatAsync**

Wrap the existing body in try/catch:

```csharp
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

    HttpResponseMessage response;
    try
    {
        response = await httpClient.PostAsync("/api/chat", content);
    }
    catch (HttpRequestException ex)
    {
        throw new OllamaUnavailableException(
            "Cannot connect to Ollama. Is it running on " + httpClient.BaseAddress + "?", ex);
    }
    catch (TaskCanceledException ex)
    {
        throw new OllamaUnavailableException(
            "Ollama request timed out. The model may be loading or the request was too complex.", ex);
    }

    if (!response.IsSuccessStatusCode)
    {
        var errorBody = await response.Content.ReadAsStringAsync();
        throw new OllamaUnavailableException(
            $"Ollama returned {(int)response.StatusCode}: {errorBody}");
    }

    var responseJson = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize Ollama response");
}
```

Add `using SmartTripPlanner.Api.Exceptions;` at the top of OllamaClient.cs.

**Step 5: Run tests to verify they pass**

```bash
dotnet test src/SmartTripPlanner.Tests --filter "FullyQualifiedName~OllamaClientErrorTests" -v minimal
```

Expected: All 3 tests PASS.

**Step 6: Commit**

```bash
git add src/SmartTripPlanner.Api/Exceptions/ src/SmartTripPlanner.Api/Services/OllamaClient.cs src/SmartTripPlanner.Tests/Services/OllamaClientErrorTests.cs
git commit -m "feat: add OllamaUnavailableException and error handling in OllamaClient"
```

---

## Task 1: AgentService Tool Error Handling

**Files:**
- Modify: `src/SmartTripPlanner.Api/Services/AgentService.cs`
- Create: `src/SmartTripPlanner.Tests/Services/AgentServiceErrorTests.cs`

**Step 1: Write the failing tests**

```csharp
// src/SmartTripPlanner.Tests/Services/AgentServiceErrorTests.cs
namespace SmartTripPlanner.Tests.Services;

using SmartTripPlanner.Api.Exceptions;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Microsoft.Extensions.Logging;

public class AgentServiceErrorTests
{
    private readonly IOllamaClient _ollamaClient = Substitute.For<IOllamaClient>();
    private readonly ICalendarService _calendarService = Substitute.For<ICalendarService>();
    private readonly ITravelService _travelService = Substitute.For<ITravelService>();
    private readonly AgentService _sut;

    public AgentServiceErrorTests()
    {
        var logger = Substitute.For<ILogger<AgentService>>();
        _sut = new AgentService(_ollamaClient, _calendarService, _travelService, logger);
    }

    [Fact]
    public async Task RunAsync_ToolThrows_FeedsErrorBackToOllama()
    {
        // First call: Ollama requests get_calendar_view
        // Second call (after error fed back): Ollama gives text response
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
                    Message = new OllamaMessage { Role = "assistant", Content = "Sorry, calendar is unavailable." },
                    Done = true
                });

        _calendarService.GetCalendarViewAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .ThrowsAsync(new InvalidOperationException("Google Calendar API not configured"));

        var result = await _sut.RunAsync("Check my calendar");

        // Agent should NOT crash — it should feed the error to Ollama and get a response
        Assert.Equal("Sorry, calendar is unavailable.", result);
    }

    [Fact]
    public async Task RunAsync_OllamaUnavailable_ReturnsUserFriendlyMessage()
    {
        _ollamaClient.ChatAsync(Arg.Any<List<OllamaMessage>>(), Arg.Any<List<OllamaTool>?>())
            .ThrowsAsync(new OllamaUnavailableException("Cannot connect to Ollama"));

        var result = await _sut.RunAsync("Plan a trip");

        Assert.Contains("Ollama", result);
        Assert.Contains("unavailable", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_BadToolArguments_FeedsParseErrorToOllama()
    {
        // Ollama sends invalid date format
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
                                    ["start"] = "not-a-date",
                                    ["end"] = "also-not-a-date"
                                }
                            }
                        }]
                    },
                    Done = true
                },
                new OllamaChatResponse
                {
                    Message = new OllamaMessage { Role = "assistant", Content = "Let me try valid dates." },
                    Done = true
                });

        var result = await _sut.RunAsync("Check calendar");

        // Should not crash — bad args get caught and fed back as error
        Assert.Equal("Let me try valid dates.", result);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/SmartTripPlanner.Tests --filter "FullyQualifiedName~AgentServiceErrorTests" -v minimal
```

Expected: FAIL — RunAsync currently throws instead of catching.

**Step 3: Update AgentService.RunAsync with error handling**

```csharp
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

        OllamaChatResponse response;
        try
        {
            response = await ollamaClient.ChatAsync(messages, tools);
        }
        catch (OllamaUnavailableException ex)
        {
            logger.LogError(ex, "Ollama is unavailable");
            return $"Ollama is unavailable: {ex.Message}";
        }

        var message = response.Message;

        if (message.ToolCalls is null || message.ToolCalls.Count == 0)
        {
            return message.Content ?? string.Empty;
        }

        messages.Add(message);

        foreach (var toolCall in message.ToolCalls)
        {
            logger.LogInformation("Executing tool: {ToolName}", toolCall.Function.Name);

            object result;
            try
            {
                result = await ExecuteToolAsync(toolCall);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tool {ToolName} failed", toolCall.Function.Name);
                result = new { error = $"{toolCall.Function.Name} failed: {ex.Message}" };
            }

            messages.Add(new OllamaMessage
            {
                Role = "tool",
                Content = JsonSerializer.Serialize(result)
            });
        }
    }

    return "Agent reached max iterations without completing. Please try a more specific request.";
}
```

Add `using SmartTripPlanner.Api.Exceptions;` at the top of AgentService.cs.

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/SmartTripPlanner.Tests --filter "FullyQualifiedName~AgentServiceErrorTests" -v minimal
```

Expected: All 3 tests PASS.

**Step 5: Verify all existing tests still pass**

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: All 22+ tests PASS.

**Step 6: Commit**

```bash
git add src/SmartTripPlanner.Api/Services/AgentService.cs src/SmartTripPlanner.Tests/Services/AgentServiceErrorTests.cs
git commit -m "feat: add error handling in AgentService for tool failures and Ollama outages"
```

---

## Task 2: TripController Error Responses

**Files:**
- Modify: `src/SmartTripPlanner.Api/Controllers/TripController.cs`
- Create: `src/SmartTripPlanner.Tests/Controllers/TripControllerTests.cs`

**Step 1: Write the failing tests**

```csharp
// src/SmartTripPlanner.Tests/Controllers/TripControllerTests.cs
namespace SmartTripPlanner.Tests.Controllers;

using SmartTripPlanner.Api.Controllers;
using SmartTripPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class TripControllerTests
{
    private readonly IAgentService _agentService = Substitute.For<IAgentService>();
    private readonly TripController _sut;

    public TripControllerTests()
    {
        _sut = new TripController(_agentService);
    }

    [Fact]
    public async Task PlanTrip_Success_ReturnsOkWithResponse()
    {
        _agentService.RunAsync(Arg.Any<string>(), Arg.Any<int>())
            .Returns("Here is your trip plan.");

        var result = await _sut.PlanTrip(new TripRequest { Prompt = "Plan Tokyo" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task PlanTrip_UnexpectedException_Returns500()
    {
        _agentService.RunAsync(Arg.Any<string>(), Arg.Any<int>())
            .ThrowsAsync(new Exception("Something broke"));

        var result = await _sut.PlanTrip(new TripRequest { Prompt = "Plan Tokyo" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }
}
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test src/SmartTripPlanner.Tests --filter "FullyQualifiedName~TripControllerTests" -v minimal
```

Expected: The 500 test FAILS because TripController currently has no try/catch.

**Step 3: Update TripController with error handling**

```csharp
// src/SmartTripPlanner.Api/Controllers/TripController.cs
namespace SmartTripPlanner.Api.Controllers;

using SmartTripPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class TripController(IAgentService agentService, ILogger<TripController> logger) : ControllerBase
{
    [HttpPost("plan")]
    public async Task<IActionResult> PlanTrip([FromBody] TripRequest request)
    {
        try
        {
            var result = await agentService.RunAsync(request.Prompt);
            return Ok(new { response = result });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process trip request");
            return StatusCode(500, new { error = "An internal error occurred. Please try again." });
        }
    }
}

public class TripRequest
{
    public required string Prompt { get; set; }
}
```

Note: The controller now takes `ILogger<TripController>` via primary constructor. Update the test to provide it:

```csharp
public TripControllerTests()
{
    var logger = Substitute.For<ILogger<TripController>>();
    _sut = new TripController(_agentService, logger);
}
```

**Step 4: Run tests to verify they pass**

```bash
dotnet test src/SmartTripPlanner.Tests --filter "FullyQualifiedName~TripControllerTests" -v minimal
```

Expected: All 2 tests PASS.

**Step 5: Commit**

```bash
git add src/SmartTripPlanner.Api/Controllers/TripController.cs src/SmartTripPlanner.Tests/Controllers/TripControllerTests.cs
git commit -m "feat: add error handling in TripController with structured error responses"
```

---

## Task 3: Full Verification

**Step 1: Run all tests**

```bash
dotnet test SmartTripPlanner.sln -v minimal
```

Expected: All 27+ tests pass (19 existing + 3 OllamaClient error + 3 AgentService error + 2 TripController).

**Step 2: Verify clean build**

```bash
dotnet build SmartTripPlanner.sln
```

Expected: 0 errors, 0 warnings.

**Step 3: Commit any fixes if needed**

```bash
git add -A
git commit -m "fix: resolve any remaining build issues"
```

---

## Summary

| Task | Component | New Tests |
|------|-----------|-----------|
| 0 | OllamaUnavailableException + OllamaClient error handling | 3 |
| 1 | AgentService tool error handling + Ollama outage handling | 3 |
| 2 | TripController structured error responses | 2 |
| 3 | Full verification | - |
