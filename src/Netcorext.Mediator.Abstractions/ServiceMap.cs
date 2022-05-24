using Microsoft.Extensions.DependencyInjection;

namespace Netcorext.Mediator;

public sealed class ServiceMap
{
    public Type Service { get; set; }
    public Type Interface { get; set; }
    public Type Implementation { get; set; }
    public ServiceLifetime ServiceLifetime { get; set; }
    public int ArgumentsCount { get; set; }
}