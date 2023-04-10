namespace Netcorext.Mediator;

public class MediatorOptions
{
    public MediatorOptions(ServiceMap[] serviceMaps)
    {
        ServiceMaps = serviceMaps;
    }

    public ServiceMap[] ServiceMaps { get; }
}