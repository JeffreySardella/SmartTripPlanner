namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface IOllamaClient
{
    Task<OllamaChatResponse> ChatAsync(List<OllamaMessage> messages, List<OllamaTool>? tools);
}
