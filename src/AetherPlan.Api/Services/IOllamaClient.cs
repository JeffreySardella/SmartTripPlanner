namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface IOllamaClient
{
    Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools);
}
