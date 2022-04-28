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
    public static MediatorBuilder AddMediator(this IServiceCollection services, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        return AddMediator(services, null, null, serviceLifetime);
    }

    public static MediatorBuilder AddMediator(this IServiceCollection services, Action<IServiceProvider, MediatorOptions>? configure, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        return AddMediator(services, null, configure, serviceLifetime);
    }

    public static MediatorBuilder AddMediator(this IServiceCollection services, Type[]? types, Action<IServiceProvider, MediatorOptions>? configure, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var builder = new MediatorBuilder(services, configure);

        builder.Services.AddTransient<IQueuing, DefaultQueuing>();
        builder.Services.AddTransient<IDispatcher, Dispatcher>();
        builder.Services.AddTransient<IConsumerRunner, DefaultConsumerRunner>();
        builder.Services.AddHostedService<ConsumerWorker>();

        return builder.AddServices(serviceLifetime, types);
    }

    public static MediatorBuilder AddServices(this MediatorBuilder builder, ServiceLifetime serviceLifetime = ServiceLifetime.Transient, params Type[]? types)
    {
        var serviceMaps = FindServices(serviceLifetime, types).ToArray();

        if (serviceMaps == null || !serviceMaps.Any()) throw new ArgumentNullException(nameof(serviceMaps));

        foreach (var map in serviceMaps)
        {
            builder.ServiceMaps.Add(map);
            builder.Services.AddOrReplace(map.Interface, map.Implementation, map.ServiceLifetime);
        }

        serviceMaps = builder.ServiceMaps.ToArray();

        builder.Services.AddOrReplace(typeof(MediatorOptions), provider =>
                                                               {
                                                                   var options = new MediatorOptions(serviceMaps);

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
        builder.Services.AddValidatorsFromAssembly(Assembly.GetEntryAssembly(), ServiceLifetime.Transient);
        builder.AddPipeline<ValidatorPipeline>();

        return builder;
    }

    public static MediatorBuilder AddPipeline<TPipeline>(this MediatorBuilder builder) where TPipeline : class, IPipeline
    {
        builder.Services.AddTransient<IPipeline, TPipeline>();

        return builder;
    }

    public static MediatorBuilder AddRequestPipeline<TPipeline, TRequest, TResult>(this MediatorBuilder builder) where TPipeline : class, IRequestPipeline<TRequest, TResult>
                                                                                                                 where TRequest : class, IRequest<TResult>
    {
        builder.Services.AddTransient<IRequestPipeline<TRequest, TResult>, TPipeline>();

        return builder;
    }

    private static IEnumerable<ServiceMap> FindServices(ServiceLifetime serviceLifetime = ServiceLifetime.Transient, params Type[]? types)
    {
        var handlerTypes = Assembly.GetEntryAssembly()?
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
                                                    Service = t.Interface!.GenericTypeArguments.First(),
                                                    ServiceLifetime = serviceLifetime
                                                }) ?? Array.Empty<ServiceMap>();

        if (types != null && types.Any()) handlerTypes = handlerTypes.Join(types, map => map.Service, type => type, (map, _) => map);

        return handlerTypes;
    }
}