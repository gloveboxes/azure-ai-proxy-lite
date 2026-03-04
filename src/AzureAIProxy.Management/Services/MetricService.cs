using System.Data;
using Microsoft.EntityFrameworkCore;

namespace AzureAIProxy.Management.Services;

public class EventMetricsData
{
    public string EventId { get; set; } = null!;
    public DateTime DateStamp { get; set; }
    public string Resource { get; set; } = null!;
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public long Requests { get; set; }
}

public class EventChartData
{
    public DateTime DateStamp { get; set; }
    public long Count { get; set; }
}


public class MetricService(IDbContextFactory<AzureAIProxyDbContext> dbFactory) : IMetricService
{
    public async Task<List<EventMetricsData>> GetEventMetricsAsync(string eventId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MetricViews
            .Where(mv => mv.EventId == eventId)
            .GroupBy(mv => new { mv.EventId, mv.DateStamp, mv.Resource })
            .OrderByDescending(g => g.Count())
            .Select(g => new EventMetricsData
            {
                EventId = g.Key.EventId,
                DateStamp = g.Key.DateStamp,
                Resource = g.Key.Resource,
                PromptTokens = g.Sum(x => x.PromptTokens),
                CompletionTokens = g.Sum(x => x.CompletionTokens),
                TotalTokens = g.Sum(x => x.TotalTokens),
                Requests = g.Count()
            }).ToListAsync();
    }

    public (int attendeeCount, int requestCount) GetAttendeeMetricsAsync(string eventId)
    {
        using var db = dbFactory.CreateDbContext();
        var userCount = db.EventAttendees
            .Where(ea => ea.EventId == eventId)
            .Count();

        var requestCount = db.Metrics
            .Where(m => m.EventId == eventId)
            .Count();

        return (userCount, requestCount);
    }

    public async Task<List<EventRegistrations>> GetAllEventsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Events
            .GroupJoin(db.EventAttendees,
                e => e.EventId,
                a => a.EventId,
                (e, ea) => new { Event = e, Attendees = ea })
            .SelectMany(
                x => x.Attendees.DefaultIfEmpty(),
                (x, a) => new { x.Event, Attendee = a })
            .GroupBy(
                x => new
                {
                    x.Event.EventId,
                    x.Event.EventCode,
                    x.Event.OrganizerName,
                    x.Event.StartTimestamp,
                    x.Event.EndTimestamp
                })
            .Select(g => new
            {
                g.Key.EventId,
                g.Key.EventCode,
                g.Key.OrganizerName,
                g.Key.StartTimestamp,
                g.Key.EndTimestamp,
                RegistrationCount = g.Count(a => a.Attendee != null && a.Attendee.ApiKey != null)
            }).Select(x => new EventRegistrations
            {
                EventId = x.EventId,
                EventName = x.EventCode,
                OrganizerName = x.OrganizerName,
                StartDate = x.StartTimestamp,
                EndDate = x.EndTimestamp,
                Registered = x.RegistrationCount
            }).ToListAsync();
    }

    public async Task<List<EventChartData>> GetActiveRegistrationsAsync(string eventId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ActiveAttendeeGrowthViews
            .Where(a => a.EventId == eventId)
            .Select(a => new { a.DateStamp, a.Attendees })
            .Select(x => new EventChartData
            {
                DateStamp = x.DateStamp,
                Count = (int)x.Attendees
            })
            .ToListAsync();
    }
}
