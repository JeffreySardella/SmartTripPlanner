namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public interface ILlmClient
{
    Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools = null);
}
