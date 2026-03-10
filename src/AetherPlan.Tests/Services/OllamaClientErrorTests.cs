namespace AetherPlan.Tests.Services;

using System.Net;
using AetherPlan.Api.Exceptions;
using AetherPlan.Api.Models;
using AetherPlan.Api.Services;

public class OllamaClientErrorTests
{
    [Fact]
    public async Task ChatAsync_ConnectionRefused_ThrowsOllamaUnavailableException()
    {
        var handler = new FaultyHttpHandler(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "test-model");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await Assert.ThrowsAsync<OllamaUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_Timeout_ThrowsOllamaUnavailableException()
    {
        var handler = new FaultyHttpHandler(new TaskCanceledException("Request timed out"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "test-model");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await Assert.ThrowsAsync<OllamaUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }

    [Fact]
    public async Task ChatAsync_ServerError_ThrowsOllamaUnavailableException()
    {
        var handler = new StatusCodeHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var client = new OllamaClient(httpClient, "test-model");

        var messages = new List<OllamaMessage>
        {
            new() { Role = "user", Content = "test" }
        };

        await Assert.ThrowsAsync<OllamaUnavailableException>(
            () => client.ChatAsync(messages, tools: null));
    }
}

internal class FaultyHttpHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw exception;
    }
}

internal class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
