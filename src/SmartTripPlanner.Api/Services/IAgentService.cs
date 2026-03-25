namespace SmartTripPlanner.Api.Services;

public interface IAgentService
{
    Task<string> RunAsync(string userRequest, int maxIterations = 10);
}
