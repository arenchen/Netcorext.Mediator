using Microsoft.Extensions.DependencyInjection;

namespace Netcorext.Mediator;

public sealed class MediatorBuilder
{
    public MediatorBuilder(IServiceCollection services, Action<IServiceProvider, MediatorOptions>? configure)
    {
        Services = services;
        Configure = configure;
        ServiceMaps = new List<ServiceMap>();
    }

    public IServiceCollection Services { get; }
    public Action<IServiceProvider, MediatorOptions>? Configure { get; }
    public List<ServiceMap> ServiceMaps { get; }
}