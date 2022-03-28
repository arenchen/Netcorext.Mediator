namespace Netcorext.Mediator;

public class MediatorOptions
{
    public MediatorOptions(IServiceProvider serviceProvider, ServiceMap[] serviceMaps)
    {
        ServiceProvider = serviceProvider;
        ServiceMaps = serviceMaps;
    }

    public IServiceProvider ServiceProvider { get; }
    public ServiceMap[] ServiceMaps { get; }
}