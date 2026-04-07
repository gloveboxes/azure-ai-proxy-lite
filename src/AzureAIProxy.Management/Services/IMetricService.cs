namespace AzureAIProxy.Management.Services;

public interface IMetricService
{
    Task<List<EventRegistrations>> GetAllEventsAsync();

    Task<Event?> GetEventForReportAsync(string eventId);

    Task<List<EventChartData>> GetActiveRegistrationsAsync(string eventId);

    Task<(int attendeeCount, int requestCount)> GetAttendeeMetricsAsync(string eventId);

    Task<List<EventMetricsData>> GetEventMetricsAsync(string eventId);
}
