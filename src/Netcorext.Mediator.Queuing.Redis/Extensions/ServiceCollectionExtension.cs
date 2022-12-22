using FreeRedis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Netcorext.Extensions.Redis.Utilities;
using Netcorext.Mediator;
using Netcorext.Mediator.Queuing;
using Netcorext.Mediator.Queuing.Redis;
using Netcorext.Serialization;

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

        builder.Services.TryAddSingleton<RedisClient>(provider =>
                                                      {
                                                          var options = provider.GetRequiredService<RedisOptions>();

                                                          var serializer = provider.GetRequiredService<ISerializer>();

                                                          return new RedisClientConnection<RedisClient>(() => new RedisClient(options.ConnectionString)
                                                                                                              {
                                                                                                                  Serialize = serializer.Serialize,
                                                                                                                  Deserialize = serializer.Deserialize,
                                                                                                                  DeserializeRaw = serializer.Deserialize
                                                                                                              }).Client;
                                                      });

        builder.Services.AddOrReplace<IQueuing, RedisQueuing>(ServiceLifetime.Singleton);

        builder.Services.AddOrReplace<IConsumerRunner, RedisConsumerRunner>(ServiceLifetime.Singleton);

        return builder;
    }
}