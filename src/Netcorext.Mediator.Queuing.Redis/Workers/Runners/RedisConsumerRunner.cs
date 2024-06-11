using FreeRedis;
using Microsoft.Extensions.Logging;
using Netcorext.Extensions.Threading;
using Netcorext.Mediator.Queuing.Redis.Extensions;
using Netcorext.Mediator.Queuing.Redis.Helpers;
using Netcorext.Mediator.Queuing.Redis.Utilities;
using Netcorext.Serialization;
using Netcorext.Worker;

namespace Netcorext.Mediator.Queuing.Redis.Runners;

internal class RedisConsumerRunner : IWorkerRunner<ConsumerWorker>
{
    private static KeyCountLocker _locker = null!;

    private readonly RedisQueuing _queuing;
    private readonly RedisClient _redis;
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorOptions _mediatorOptions;
    private readonly RedisOptions _options;
    private readonly ISerializer _serializer;
    private readonly ILogger<RedisConsumerRunner> _logger;

    public RedisConsumerRunner(IServiceProvider serviceProvider, MediatorOptions mediatorOptions, IQueuing queuing, RedisOptions options, ISerializer serializer, ILogger<RedisConsumerRunner> logger)
    {
        _locker = new KeyCountLocker(maximum: options.WorkerTaskLimit, logger: logger);

        _queuing = (RedisQueuing)queuing;
        _redis = _queuing.Redis;
        _serviceProvider = serviceProvider;
        _mediatorOptions = mediatorOptions;
        _options = options;
        _serializer = serializer;
        _logger = logger;
    }

    public async Task InvokeAsync(ConsumerWorker worker, CancellationToken cancellationToken = default)
    {
        var channels = await _redis.RegisterConsumerAsync(_mediatorOptions.ServiceMaps, _options.Prefix, _options.GroupName, _options.MachineName, _options.GroupNewestId);

        await _queuing.SubscribeAsync(channels.ToArray(), (s, o) =>
                                                          {
                                                              ReadStreamAsync(o.ToString()!, cancellationToken)
                                                                 .GetAwaiter()
                                                                 .GetResult();
                                                          }, cancellationToken);
    }

    private async Task ReadStreamAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _locker.IncrementAsync(key, cancellationToken))
            {
                _logger.LogWarning("Worker task limit exceeded");
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var entries = (await _redis.XReadGroupAsync(_options.GroupName,
                                                            _options.MachineName,
                                                            _options.StreamBatchSize ?? RedisOptions.DEFAULT_STREAM_BATCH_SIZE,
                                                            0,
                                                            false,
                                                            new Dictionary<string, string> { { key, ">" } }
                                                           ))
                             .SelectMany(t => t.ToStreamData())
                             .ToArray();

                if (entries.Length == 0) break;

                foreach (var entry in entries)
                {
                    try
                    {
                        if (entry.Data == null || !entry.Data.Any()) continue;

                        var rawMessage = await _serializer.DeserializeAsync<Message>(entry.Data, cancellationToken);

                        if (rawMessage == null) continue;

                        var message = rawMessage.Referer == null
                                          ? new Message
                                            {
                                                ServiceType = rawMessage.ServiceType,
                                                RefererType = rawMessage.PayloadType,
                                                Referer = rawMessage.Payload,
                                                GroupName = _options.GroupName,
                                                MachineName = _options.MachineName,
                                                Authorization = rawMessage.Authorization,
                                                RequestId = rawMessage.RequestId
                                            }
                                          : new Message
                                            {
                                                ServiceType = rawMessage.ServiceType,
                                                RefererType = rawMessage.RefererType,
                                                Referer = rawMessage.Referer,
                                                GroupName = _options.GroupName,
                                                MachineName = _options.MachineName,
                                                Authorization = rawMessage.Authorization,
                                                RequestId = rawMessage.RequestId
                                            };

                        try
                        {
                            var result = await StreamDataSender.SendAsync(_serviceProvider, _queuing, entry, cancellationToken);

                            if (!rawMessage.Respond.HasValue || !rawMessage.Respond.Value || rawMessage.Referer != null) continue;

                            message.PayloadType = result?.GetType().AssemblyQualifiedName;
                            message.Payload = await _serializer.SerializeToUtf8BytesAsync(result, cancellationToken);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "{Message}", e.Message);

                            message.Error = e.ToString();
                        }

                        message.CreationDate = DateTimeOffset.UtcNow;

                        var streamKey = KeyHelper.GetStreamKey(KeyHelper.Concat(_options.Prefix, rawMessage.GroupName), rawMessage.ServiceType!);

                        await _queuing.PublishAsync(streamKey, message, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "{Message}", e.Message);
                    }
                }
            }

            await _locker.DecrementAsync(key, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{Message}", e.Message);

            await _locker.DecrementAsync(key, cancellationToken);
        }
    }

    public void Dispose()
    { }
}
