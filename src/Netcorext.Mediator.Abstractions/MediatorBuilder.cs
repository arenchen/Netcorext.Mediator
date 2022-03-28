using Microsoft.Extensions.DependencyInjection;

namespace Netcorext.Mediator;

public sealed class MediatorBuilder
{
    public MediatorBuilder(IServiceCollection services, Action<IServiceProvider, MediatorOptions>? configure, ServiceLifetime serviceLifetime)
    {
        Services = services;
        Configure = configure;
        ServiceLifetime = serviceLifetime;
        ServiceMaps = new List<ServiceMap>();
    }

    public IServiceCollection Services { get; }
    public Action<IServiceProvider, MediatorOptions>? Configure { get; }
    public ServiceLifetime ServiceLifetime { get; }
    public List<ServiceMap> ServiceMaps { get; }
}