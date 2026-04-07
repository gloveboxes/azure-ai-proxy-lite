using AzureAIProxy.Management.Services;

namespace AzureAIProxy.Management;

public static class ManagementServiceExtensions
{
    public static IServiceCollection AddManagementServices(this IServiceCollection services)
    {
        services.AddHttpClient<ICacheInvalidationService, CacheInvalidationService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IModelService, ModelService>();
        services.AddScoped<IMetricService, MetricService>();
        services.AddScoped<IBackupService, BackupService>();
        return services;
    }
}
