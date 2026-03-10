# Phase 3a: Error Handling Design

## Goal
Add resilience to the agent loop so tool failures and Ollama outages are handled gracefully instead of crashing.

## Approach
- Services throw on failure (no interface changes)
- AgentService catches tool execution errors and feeds them back to Ollama as tool results
- OllamaClient throws typed exceptions for connectivity issues
- TripController returns structured error responses
- Safe argument parsing in ExecuteToolAsync

## Scope
- OllamaUnavailableException typed exception
- Try/catch in ExecuteToolAsync with error objects sent to LLM
- Try/catch in AgentService.RunAsync for Ollama failures
- Try/catch in TripController for structured HTTP error responses
- Safe parsing helpers for LLM-provided arguments
- 4+ new tests
