using FreeRedis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Netcorext.Mediator;
using Netcorext.Mediator.Queuing;
using Netcorext.Mediator.Queuing.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtension
{
    public static MediatorBuilder AddRedisQueuing(this MediatorBuilder builder)
    {
        return AddRedisQueuing(builder, null);
    }

    public static MediatorBuilder AddRedisQueuing(this MediatorBuilder builder, Action<IServiceProvider, RedisOptions>? configure)
    {
        builder.Services.TryAddSingleton(provider =>
                                         {
                                             var options = new RedisOptions
                                                           {
                                                               ConnectionString = "0.0.0.0:6379,writeBuffer=102400,syncTimeout=30000,min poolSize=10"
                                                           };

                                             configure?.Invoke(provider, options);

                                             return options;
                                         });

        builder.Services.TryAddSingleton<RedisClient>(provider =>
                                                      {
                                                          var options = provider.GetRequiredService<RedisOptions>();

                                                          return new RedisClientConnection(options.ConnectionString).Client;
                                                      });

        builder.Services.AddOrReplace<IQueuing, RedisQueuing>(ServiceLifetime.Singleton);

        builder.Services.AddOrReplace<IConsumerRunner, RedisConsumerRunner>();

        return builder;
    }
}