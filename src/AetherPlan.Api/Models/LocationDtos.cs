namespace AetherPlan.Api.Models;

public class SaveLocationRequest
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Category { get; set; }
    public string? SourceUrl { get; set; }
    public string? RawPageContent { get; set; }
}

public class AssignLocationRequest
{
    public required int TripId { get; set; }
}
