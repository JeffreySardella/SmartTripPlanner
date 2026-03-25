namespace SmartTripPlanner.Api.Models;

public class TravelValidation
{
    public double DistanceKm { get; set; }
    public double EstimatedMinutes { get; set; }
    public double AvailableMinutes { get; set; }
    public bool IsFeasible { get; set; }
}
