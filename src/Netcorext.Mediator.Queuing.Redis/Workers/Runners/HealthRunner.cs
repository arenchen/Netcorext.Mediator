using FreeRedis;
using Microsoft.Extensions.Logging;
using Netcorext.Worker;

namespace Netcorext.Mediator.Queuing.Redis.Runners;

internal class HealthRunner : IWorkerRunner<ConsumerWorker>
{
    private readonly RedisOptions _options;
    private readonly ILogger<HealthRunner> _logger;
    private readonly RedisClient _redis;

    public HealthRunner(IQueuing queuing, RedisOptions options, ILogger<HealthRunner> logger)
    {
        var redisQueuing = (RedisQueuing)queuing;
        _redis = redisQueuing.Redis;
        _options = options;
        _logger = logger;
    }
    public async Task InvokeAsync(ConsumerWorker worker, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _redis.Echo("Health");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
            }

            await Task.Delay(_options.HealthCheckInterval ?? RedisOptions.DEFAULT_HEALTH_CHECK_INTERVAL);
        }
    }


    public void Dispose()
    { }
}
