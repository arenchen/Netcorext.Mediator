using Microsoft.Extensions.DependencyInjection;

namespace Netcorext.Mediator;

public sealed class ServiceMap
{
    public Type Service { get; set; } = null!;
    public Type Interface { get; set; } = null!;
    public Type Implementation { get; set; } = null!;
    public ServiceLifetime ServiceLifetime { get; set; }
    public int ArgumentsCount { get; set; }
}