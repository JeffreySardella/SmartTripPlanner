namespace SmartTripPlanner.Api.Models;

public class CachedLocation
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public required string Category { get; set; }
    public string? SourceUrl { get; set; }
    public int? TripId { get; set; }
    public Trip? Trip { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
