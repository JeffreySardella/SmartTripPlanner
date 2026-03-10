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
