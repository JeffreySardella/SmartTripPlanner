namespace SmartTripPlanner.Api.Models;

public class TripEvent
{
    public int Id { get; set; }
    public int TripId { get; set; }
    public required string Summary { get; set; }
    public required string Location { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string? CalendarEventId { get; set; }
    public Trip? Trip { get; set; }
}
