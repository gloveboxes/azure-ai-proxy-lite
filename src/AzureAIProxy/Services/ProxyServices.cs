namespace AzureAIProxy.Services;

public static class ServicesExtensions
{
    public static IServiceCollection AddProxyServices(
        this IServiceCollection services,
        bool useMockProxy
    )
    {
        services
            .AddSingleton<IEventLookupService, EventLookupService>()
            .AddScoped<ICatalogService, CatalogService>()
            .AddScoped<IAuthorizeService, AuthorizeService>()
            .AddScoped<IMetricService, MetricService>()
            .AddScoped<IAttendeeService, AttendeeService>()
            .AddScoped<IEventService, EventService>()
            .AddScoped<IFoundryAgentService, FoundryAgentService>();

        // Background metric writer (singleton since it owns the Channel)
        services.AddSingleton<MetricBackgroundService>();
        services.AddSingleton<IMetricChannel>(sp => sp.GetRequiredService<MetricBackgroundService>());
        services.AddHostedService(sp => sp.GetRequiredService<MetricBackgroundService>());

        // In-memory rate limiting with background flush (singleton)
        services.AddSingleton<RateLimitService>();
        services.AddSingleton<IRateLimitService>(sp => sp.GetRequiredService<RateLimitService>());
        services.AddHostedService(sp => sp.GetRequiredService<RateLimitService>());

        if (useMockProxy)
            services.AddScoped<IProxyService, MockProxyService>();
        else
            services.AddScoped<IProxyService, ProxyService>();

        return services;
    }
}
