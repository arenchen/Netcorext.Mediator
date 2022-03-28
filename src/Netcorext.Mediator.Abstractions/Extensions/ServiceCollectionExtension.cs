using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddOrReplace<TService, TImplementation>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient) where TService : class
    {
        return services.AddOrReplace(typeof(TService), typeof(TImplementation), lifetime);
    }

    public static IServiceCollection AddOrReplace(this IServiceCollection services, Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        services.RemoveAll(serviceType);

        var descriptor = new ServiceDescriptor(serviceType, implementationType, lifetime);

        services.Add(descriptor);

        return services;
    }

    public static IServiceCollection AddOrReplace(this IServiceCollection services, Type serviceType, Func<IServiceProvider, object> factory, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        services.RemoveAll(serviceType);

        var descriptor = new ServiceDescriptor(serviceType, factory, lifetime);

        services.Add(descriptor);

        return services;
    }

    public static IServiceCollection AddOrReplace(this IServiceCollection services, Type serviceType, object instance, ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        services.RemoveAll(serviceType);

        var descriptor = new ServiceDescriptor(serviceType, instance);

        services.Add(descriptor);

        return services;
    }
}