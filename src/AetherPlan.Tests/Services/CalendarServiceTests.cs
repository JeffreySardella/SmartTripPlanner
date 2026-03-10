namespace AetherPlan.Tests.Services;

using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using Google.Apis.Calendar.v3.Data;

public class CalendarServiceTests
{
    [Fact]
    public async Task GetCalendarViewAsync_ReturnsFreeBusyBlocks()
    {
        var service = new TestableCalendarService(new List<Event>
        {
            new() { Summary = "Meeting", Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(new DateTime(2026, 3, 10, 9, 0, 0)) },
                     End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(new DateTime(2026, 3, 10, 10, 0, 0)) } }
        });

        var blocks = await service.GetCalendarViewAsync(
            new DateTime(2026, 3, 10, 8, 0, 0),
            new DateTime(2026, 3, 10, 12, 0, 0));

        Assert.Contains(blocks, b => b.IsBusy && b.Start.Hour == 9);
        Assert.Contains(blocks, b => !b.IsBusy);
    }

    [Fact]
    public async Task GetCalendarViewAsync_NoEvents_AllFree()
    {
        var service = new TestableCalendarService(new List<Event>());

        var blocks = await service.GetCalendarViewAsync(
            new DateTime(2026, 3, 10, 8, 0, 0),
            new DateTime(2026, 3, 10, 12, 0, 0));

        Assert.All(blocks, b => Assert.False(b.IsBusy));
    }
}

/// <summary>
/// Test double that bypasses Google API auth and returns canned events.
/// </summary>
internal class TestableCalendarService : CalendarService
{
    private readonly List<Event> _events;

    public TestableCalendarService(List<Event> events) : base(calendarService: null!)
    {
        _events = events;
    }

    protected override Task<IList<Event>> FetchEventsAsync(DateTime start, DateTime end)
    {
        IList<Event> result = _events
            .Where(e => e.Start.DateTimeDateTimeOffset?.DateTime >= start && e.End.DateTimeDateTimeOffset?.DateTime <= end)
            .ToList();
        return Task.FromResult(result);
    }
}
