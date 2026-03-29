namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Logging;
using CalendarApi = Google.Apis.Calendar.v3.CalendarService;

public class CalendarService : ICalendarService
{
    private readonly CalendarApi? _calendarApi;
    private readonly ILogger<CalendarService>? _logger;

    public CalendarService(CalendarApi? calendarService, ILogger<CalendarService>? logger = null)
    {
        _calendarApi = calendarService;
        _logger = logger;
    }

    public string TargetCalendarId { get; set; } = "primary";
    public bool IsConfigured => _calendarApi is not null;

    public async Task<List<CalendarInfo>> ListCalendarsAsync()
    {
        if (_calendarApi is null) return [];

        var list = await _calendarApi.CalendarList.List().ExecuteAsync();
        return (list.Items ?? []).Select(c => new CalendarInfo
        {
            Id = c.Id,
            Name = c.Summary ?? c.Id,
            IsPrimary = c.Primary ?? false
        }).ToList();
    }

    public async Task<CalendarInfo> CreateCalendarAsync(string name)
    {
        if (_calendarApi is null) throw new InvalidOperationException("Google Calendar API not configured");

        var calendar = new Calendar { Summary = name };
        var created = await _calendarApi.Calendars.Insert(calendar).ExecuteAsync();

        _logger?.LogInformation("Created calendar {CalendarId}: {Name}", created.Id, name);
        return new CalendarInfo { Id = created.Id, Name = name };
    }

    public async Task DeleteCalendarAsync(string calendarId)
    {
        if (_calendarApi is null) throw new InvalidOperationException("Google Calendar API not configured");
        if (calendarId == "primary") throw new InvalidOperationException("Cannot delete primary calendar");

        await _calendarApi.Calendars.Delete(calendarId).ExecuteAsync();
        _logger?.LogInformation("Deleted calendar {CalendarId}", calendarId);
    }

    public async Task<CalendarUserProfile?> GetUserProfileAsync()
    {
        if (_calendarApi is null) return null;

        try
        {
            var calendarList = await _calendarApi.CalendarList.Get("primary").ExecuteAsync();
            return new CalendarUserProfile
            {
                Name = calendarList.Summary ?? "",
                Email = calendarList.Id ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get user profile");
            return null;
        }
    }

    public async Task<List<FreeBusyBlock>> GetCalendarViewAsync(DateTime start, DateTime end)
    {
        var events = await FetchEventsAsync(start, end);
        var blocks = new List<FreeBusyBlock>();

        var sorted = events.OrderBy(e => e.Start.DateTimeDateTimeOffset).ToList();

        var cursor = start;
        foreach (var evt in sorted)
        {
            var evtStart = evt.Start.DateTimeDateTimeOffset?.DateTime ?? start;
            var evtEnd = evt.End.DateTimeDateTimeOffset?.DateTime ?? end;

            if (cursor < evtStart)
            {
                blocks.Add(new FreeBusyBlock { Start = cursor, End = evtStart, IsBusy = false });
            }

            blocks.Add(new FreeBusyBlock { Start = evtStart, End = evtEnd, IsBusy = true });
            cursor = evtEnd > cursor ? evtEnd : cursor;
        }

        if (cursor < end)
        {
            blocks.Add(new FreeBusyBlock { Start = cursor, End = end, IsBusy = false });
        }

        return blocks;
    }

    public async Task<string> CreateEventAsync(string summary, string location,
        DateTime start, DateTime end, string? description = null)
    {
        if (_calendarApi is null) throw new InvalidOperationException("Google Calendar API not configured");

        var calendarEvent = new Event
        {
            Summary = summary,
            Location = location,
            Description = description,
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start) },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end) }
        };

        var request = _calendarApi.Events.Insert(calendarEvent, TargetCalendarId);
        var created = await request.ExecuteAsync();

        _logger?.LogInformation("Created event {EventId} on calendar {CalendarId}: {Summary}",
            created.Id, TargetCalendarId, summary);
        return created.Id;
    }

    public async Task<List<string>> PushItineraryAsync(List<TripEvent> events)
    {
        var ids = new List<string>();
        foreach (var evt in events)
        {
            var id = await CreateEventAsync(evt.Summary, evt.Location, evt.Start, evt.End);
            ids.Add(id);
        }
        return ids;
    }

    protected virtual async Task<IList<Event>> FetchEventsAsync(DateTime start, DateTime end)
    {
        if (_calendarApi is null)
        {
            _logger?.LogWarning("Calendar not configured — returning empty events");
            return new List<Event>();
        }

        var request = _calendarApi.Events.List(TargetCalendarId);
        request.TimeMinDateTimeOffset = new DateTimeOffset(start);
        request.TimeMaxDateTimeOffset = new DateTimeOffset(end);
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var result = await request.ExecuteAsync();
        return result.Items ?? (IList<Event>)new List<Event>();
    }
}
