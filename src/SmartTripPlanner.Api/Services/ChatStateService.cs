namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public class ChatStateService
{
    public List<ChatMessage> Messages { get; } = [];
    public TripConfirmation? PendingConfirmation { get; set; }
    public bool IsLoading { get; set; }

    public void Clear()
    {
        Messages.Clear();
        PendingConfirmation = null;
        IsLoading = false;
    }
}

public record ChatMessage(string Role, string Content);
