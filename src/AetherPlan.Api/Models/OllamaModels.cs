namespace AetherPlan.Api.Models;

using System.Text.Json.Serialization;

public class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<OllamaMessage> Messages { get; set; }

    [JsonPropertyName("tools")]
    public List<OllamaTool>? Tools { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

public class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required OllamaFunction Function { get; set; }
}

public class OllamaFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("parameters")]
    public required object Parameters { get; set; }
}

public class OllamaToolCall
{
    [JsonPropertyName("function")]
    public required OllamaFunctionCall Function { get; set; }
}

public class OllamaFunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public required Dictionary<string, object> Arguments { get; set; }
}

public class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public required OllamaMessage Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
