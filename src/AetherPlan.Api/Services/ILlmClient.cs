namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface ILlmClient
{
    Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools = null);
}
