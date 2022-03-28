using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Netcorext.Mediator;
using Netcorext.Mediator.Internals;
using Netcorext.Mediator.Pipelines;
using Netcorext.Mediator.Queuing;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static MediatorBuilder AddMediator(this IServiceCollection services)
    {
        return AddMediator(services, null);
    }

    public static MediatorBuilder AddMediator(this IServiceCollection services, Action<IServiceProvider, MediatorOptions>? configure, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        return AddMediator(services, null, configure, serviceLifetime);
    }

    public static MediatorBuilder AddMediator(this IServiceCollection services, Type[]? types, Action<IServiceProvider, MediatorOptions>? configure, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var builder = new MediatorBuilder(services, configure, serviceLifetime);

        builder.Services.AddTransient<IQueuing, DefaultQueuing>();
        builder.Services.AddTransient<IDispatcher, Dispatcher>();
        builder.Services.AddTransient<IConsumerRunner, DefaultConsumerRunner>();
        builder.Services.AddHostedService<ConsumerWorker>();

        return builder.AddServices(types);
    }

    public static MediatorBuilder AddServices(this MediatorBuilder builder, params Type[]? types)
    {
        var serviceMaps = FindServices(types).ToArray();

        if (serviceMaps == null || !serviceMaps.Any())
            throw new ArgumentNullException(nameof(serviceMaps));

        foreach (var map in serviceMaps)
        {
            if (builder.ServiceMaps.Any(t => t.Implementation == map.Implementation)) continue;

            builder.Services.TryAdd(new ServiceDescriptor(map.Interface, map.Implementation, builder.ServiceLifetime));
            builder.ServiceMaps.Add(map);
        }

        serviceMaps = builder.ServiceMaps.ToArray();

        builder.Services.AddOrReplace(typeof(MediatorOptions), provider =>
                                                               {
                                                                   var options = new MediatorOptions(provider, serviceMaps);

                                                                   builder.Configure?.Invoke(provider, options);

                                                                   return options;
                                                               }, ServiceLifetime.Singleton);

        return builder;
    }

    public static MediatorBuilder AddPerformancePipeline(this MediatorBuilder builder, Action<IServiceProvider, PerformanceOptions>? configure = default)
    {
        builder.Services.TryAddSingleton(provider =>
                                         {
                                             var opt = new PerformanceOptions();

                                             configure?.Invoke(provider, opt);

                                             return opt;
                                         });

        builder.AddPipeline<PerformancePipeline>();

        return builder;
    }

    public static MediatorBuilder AddValidatorPipeline(this MediatorBuilder builder)
    {
        builder.Services.AddValidatorsFromAssembly(Assembly.GetEntryAssembly(), builder.ServiceLifetime);
        builder.AddPipeline<ValidatorPipeline>();

        return builder;
    }

    public static MediatorBuilder AddPipeline<TPipeline>(this MediatorBuilder builder) where TPipeline : class, IPipeline
    {
        builder.Services.AddTransient<IPipeline, TPipeline>();

        return builder;
    }

    private static IEnumerable<ServiceMap> FindServices(params Type[]? types)
    {
        var handlerTypes = Assembly.GetEntryAssembly()
                                   .GetTypes()
                                   .Cast<TypeInfo>()
                                   .Select(t => new
                                                {
                                                    Implementation = t,
                                                    Interface = t.ImplementedInterfaces
                                                                 .FirstOrDefault(t2 => t2.IsGenericType
                                                                                    && (
                                                                                           t2.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                                                                                        || t2.GetGenericTypeDefinition() == typeof(IResponseHandler<,>)
                                                                                       ))
                                                })
                                   .Where(t => t.Interface != null)
                                   .Select(t => new ServiceMap
                                                {
                                                    Implementation = t.Implementation,
                                                    Interface = t.Interface!,
                                                    Service = t.Interface!.GenericTypeArguments.First()
                                                });

        if (types != null && types.Any())
            handlerTypes = handlerTypes.Join(types, map => map.Service, type => type, (map, _) => map);

        return handlerTypes;
    }
}