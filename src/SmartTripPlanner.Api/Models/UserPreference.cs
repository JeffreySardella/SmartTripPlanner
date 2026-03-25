namespace SmartTripPlanner.Api.Models;

public class UserPreference
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
}
