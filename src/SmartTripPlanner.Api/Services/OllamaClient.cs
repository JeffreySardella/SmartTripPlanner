namespace SmartTripPlanner.Api.Services;

using System.Text.Json;
using SmartTripPlanner.Api.Exceptions;
using SmartTripPlanner.Api.Models;

public class OllamaClient(HttpClient httpClient, string model) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools)
    {
        var request = new LlmChatRequest
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
            throw new LlmUnavailableException(
                "Cannot connect to Ollama. Is it running on " + httpClient.BaseAddress + "?", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LlmUnavailableException(
                "Ollama request timed out. The model may be loading or the request was too complex.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new LlmUnavailableException(
                $"Ollama returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<LlmChatResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Ollama response");
    }
}
