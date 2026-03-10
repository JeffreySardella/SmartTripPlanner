namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Models;

public class OllamaClient(HttpClient httpClient, string model) : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        var response = await httpClient.PostAsync("/api/chat", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Ollama response");
    }
}
