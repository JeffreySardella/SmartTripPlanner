namespace SmartTripPlanner.Api.Models;

public class TripConfirmation
{
    public string Destination { get; set; } = "";
    public string? Dates { get; set; }
    public string? Pace { get; set; }
    public int Travelers { get; set; } = 1;
    public string? Budget { get; set; }
    public List<string> Interests { get; set; } = [];
    public string? Dietary { get; set; }
    public string? Accessibility { get; set; }
    public List<string> MustSee { get; set; } = [];
    public List<string> Avoid { get; set; } = [];
}
