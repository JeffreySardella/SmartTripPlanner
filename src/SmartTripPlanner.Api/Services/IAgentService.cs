namespace SmartTripPlanner.Api.Services;

public interface IAgentService
{
    Task<string> RunAsync(string userRequest, int maxIterations = 10);
    Task<string> RunAsync(string userRequest, Action<AgentProgress> onProgress, int maxIterations = 10);
}

public class AgentProgress
{
    public int Iteration { get; set; }
    public int MaxIterations { get; set; }
    public string Status { get; set; } = "";
    public string? ToolName { get; set; }
    public double ElapsedSec { get; set; }
}
