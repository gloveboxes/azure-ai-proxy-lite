using AzureAIProxy.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AzureAIProxy.Tests.Proxy;

public class ProxyServicesRegistrationTests
{
    [Fact]
    public void AddProxyServices_UseMockProxyTrue_RegistersMockProxyService()
    {
        var services = new ServiceCollection();

        services.AddProxyServices(useMockProxy: true);

        var registration = services.Last(d => d.ServiceType == typeof(IProxyService));
        Assert.Equal(typeof(MockProxyService), registration.ImplementationType);
    }

    [Fact]
    public void AddProxyServices_UseMockProxyFalse_RegistersRealProxyService()
    {
        var services = new ServiceCollection();

        services.AddProxyServices(useMockProxy: false);

        var registration = services.Last(d => d.ServiceType == typeof(IProxyService));
        Assert.Equal(typeof(ProxyService), registration.ImplementationType);
    }
}
