using Microsoft.Extensions.DependencyInjection.Extensions;
using Netcorext.Mediator;
using Netcorext.Mediator.Queuing;
using Netcorext.Mediator.Queuing.Redis;
using Netcorext.Mediator.Queuing.Redis.Runners;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static MediatorBuilder AddRedisQueuing(this MediatorBuilder builder)
    {
        return AddRedisQueuing(builder, null);
    }

    public static MediatorBuilder AddRedisQueuing(this MediatorBuilder builder, Action<IServiceProvider, RedisOptions>? configure)
    {
        builder.Services.TryAddSystemJsonSerializer();

        builder.Services.TryAddSingleton<RedisOptions>(provider =>
                                                       {
                                                           var options = new RedisOptions
                                                                         {
                                                                             ConnectionString = "0.0.0.0:6379"
                                                                         };

                                                           configure?.Invoke(provider, options);

                                                           return options;
                                                       });

        builder.Services.AddOrReplace<IQueuing, RedisQueuing>(ServiceLifetime.Singleton);

        builder.Services.AddWorkerRunner<ConsumerWorker, RedisConsumerRunner>();
        builder.Services.AddWorkerRunner<ConsumerWorker, PendingStreamRunner>();
        builder.Services.AddHostedService<ConsumerWorker>();

        return builder;
    }
}
