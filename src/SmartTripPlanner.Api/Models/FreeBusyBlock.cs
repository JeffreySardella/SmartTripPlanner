namespace SmartTripPlanner.Api.Models;

public class FreeBusyBlock
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsBusy { get; set; }
}
