using FreeRedis;
using Netcorext.Mediator.Queuing.Redis.Helpers;

namespace Netcorext.Mediator.Queuing.Redis.Extensions;

internal static class RedisExtension
{
    public static async Task<IEnumerable<string>> RegisterConsumerAsync(this RedisClient redis, IEnumerable<ServiceMap> serviceMaps, string? prefix, string groupName, string machineName, bool groupNewestId)
    {
        var keys = new List<string>();
            
        foreach (var service in serviceMaps)
        {
            var key = KeyHelper.Concat(prefix,
                                       service.Interface.GetGenericTypeDefinition() == typeof(IResponseHandler<,>) ? groupName : string.Empty,
                                       service.Service.FullName!);

            if (!await redis.ExistsAsync(key) || (await redis.XInfoGroupsAsync(key)).All(x => x.name != groupName))
                await redis.XGroupCreateAsync(key, groupName, groupNewestId ? "$" : "0", true);

            var consumers = await redis.XInfoConsumersAsync(key, groupName);

            if (consumers.All(x => x.name != machineName))
                await redis.XGroupCreateConsumerAsync(key, groupName, machineName);

            keys.Add(key);
        }

        return keys;
    }
}