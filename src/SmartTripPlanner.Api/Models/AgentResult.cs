namespace SmartTripPlanner.Api.Models;

public enum AgentResultType
{
    TextResponse,
    TripConfirmation
}

public class AgentResult
{
    public AgentResultType Type { get; init; }
    public string? TextContent { get; init; }
    public TripConfirmation? Confirmation { get; init; }

    public static AgentResult Text(string content) => new()
    {
        Type = AgentResultType.TextResponse,
        TextContent = content
    };

    public static AgentResult Confirm(TripConfirmation confirmation) => new()
    {
        Type = AgentResultType.TripConfirmation,
        Confirmation = confirmation
    };
}
