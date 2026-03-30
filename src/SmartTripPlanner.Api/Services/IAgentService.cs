namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public interface IAgentService
{
    Task<AgentResult> RunAsync(string userRequest, int maxIterations = 10);
    Task<AgentResult> RunAsync(string userRequest, Action<AgentProgress> onProgress, int maxIterations = 10);
}

public class AgentProgress
{
    public int Iteration { get; set; }
    public int MaxIterations { get; set; }
    public string Status { get; set; } = "";
    public string? ToolName { get; set; }
    public double ElapsedSec { get; set; }
}
