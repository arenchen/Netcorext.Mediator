using FreeRedis;

namespace Netcorext.Mediator.Queuing.Redis;

public class RedisClientConnection
{
    private static Lazy<RedisClient>? _lazyRedisClient;
    private static readonly object Locker = new object();

    public RedisClientConnection(string connectionString)
    {
        lock (Locker)
        {
            _lazyRedisClient ??= new Lazy<RedisClient>(() => new RedisClient(connectionString));
        }
    }

    public RedisClient Client => _lazyRedisClient!.Value;
}