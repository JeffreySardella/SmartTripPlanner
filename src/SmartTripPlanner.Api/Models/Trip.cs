namespace SmartTripPlanner.Api.Models;

public class Trip
{
    public int Id { get; set; }
    public required string Destination { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TripEvent> Events { get; set; } = [];
}
