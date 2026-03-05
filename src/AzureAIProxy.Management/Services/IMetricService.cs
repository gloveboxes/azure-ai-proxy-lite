namespace AzureAIProxy.Management.Services;

public interface IMetricService
{
    Task<List<EventRegistrations>> GetAllEventsAsync();

    Task<List<EventChartData>> GetActiveRegistrationsAsync(string eventId);

    Task<(int attendeeCount, int requestCount)> GetAttendeeMetricsAsync(string eventId);

    Task<List<EventMetricsData>> GetEventMetricsAsync(string eventId);
}
