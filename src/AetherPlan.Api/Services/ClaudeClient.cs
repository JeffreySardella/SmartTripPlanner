namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;

public class ClaudeClient(HttpClient httpClient, string model, string apiKey) : ILlmClient
{
    public async Task<LlmChatResponse> ChatAsync(List<LlmMessage> messages, List<LlmTool>? tools)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 4096,
            ["messages"] = ConvertMessages(messages)
        };

        var systemPrompt = ExtractSystemPrompt(messages);
        if (!string.IsNullOrEmpty(systemPrompt))
            requestBody["system"] = systemPrompt;

        if (tools is { Count: > 0 })
            requestBody["tools"] = ConvertTools(tools);

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
            request.Content = content;
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            response = await httpClient.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException("Cannot connect to Claude API.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LlmUnavailableException("Claude API request timed out.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new LlmUnavailableException(
                $"Claude API returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        return ParseResponse(responseJson);
    }

    internal static string? ExtractSystemPrompt(List<LlmMessage> messages)
    {
        var systemMessages = messages.Where(m => m.Role == "system").ToList();
        if (systemMessages.Count == 0) return null;
        return string.Join("\n", systemMessages.Select(m => m.Content));
    }

    internal static List<object> ConvertMessages(List<LlmMessage> messages)
    {
        var result = new List<object>();
        var nonSystem = messages.Where(m => m.Role != "system").ToList();

        var i = 0;
        while (i < nonSystem.Count)
        {
            var msg = nonSystem[i];

            if (msg.Role == "assistant" && msg.ToolCalls is { Count: > 0 })
            {
                var contentBlocks = new List<object>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contentBlocks.Add(new { type = "text", text = msg.Content });

                foreach (var tc in msg.ToolCalls)
                {
                    contentBlocks.Add(new
                    {
                        type = "tool_use",
                        id = tc.Id ?? $"toolu_{Guid.NewGuid():N}",
                        name = tc.Function.Name,
                        input = tc.Function.Arguments
                    });
                }

                result.Add(new { role = "assistant", content = contentBlocks });
                i++;
            }
            else if (msg.Role == "tool")
            {
                // Collect consecutive tool messages into one user message
                var toolResults = new List<object>();
                while (i < nonSystem.Count && nonSystem[i].Role == "tool")
                {
                    var toolMsg = nonSystem[i];
                    toolResults.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = toolMsg.ToolCallId ?? "",
                        content = toolMsg.Content ?? ""
                    });
                    i++;
                }

                result.Add(new { role = "user", content = toolResults });
            }
            else
            {
                result.Add(new { role = msg.Role, content = msg.Content ?? "" });
                i++;
            }
        }

        return result;
    }

    internal static List<object> ConvertTools(List<LlmTool> tools)
    {
        return tools.Select(t => (object)new
        {
            name = t.Function.Name,
            description = t.Function.Description,
            input_schema = t.Function.Parameters
        }).ToList();
    }

    internal static LlmChatResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("content", out var content))
        {
            return new LlmChatResponse
            {
                Message = new LlmMessage { Role = "assistant", Content = null },
                Done = true
            };
        }

        string? textContent = null;
        List<LlmToolCall>? toolCalls = null;

        foreach (var block in content.EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();

            if (type == "text")
            {
                textContent = (textContent ?? "") + block.GetProperty("text").GetString();
            }
            else if (type == "tool_use")
            {
                toolCalls ??= [];
                var args = new Dictionary<string, object>();
                var input = block.GetProperty("input");
                foreach (var prop in input.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString()!,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetRawText()
                    };
                }

                toolCalls.Add(new LlmToolCall
                {
                    Id = block.GetProperty("id").GetString(),
                    Function = new LlmFunctionCall
                    {
                        Name = block.GetProperty("name").GetString()!,
                        Arguments = args
                    }
                });
            }
        }

        return new LlmChatResponse
        {
            Message = new LlmMessage
            {
                Role = "assistant",
                Content = textContent,
                ToolCalls = toolCalls
            },
            Done = true
        };
    }
}
