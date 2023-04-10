using FreeRedis;
using Microsoft.Extensions.Logging;
using Netcorext.Extensions.Linq;
using Netcorext.Mediator.Queuing.Redis.Extensions;
using Netcorext.Mediator.Queuing.Redis.Helpers;
using Netcorext.Mediator.Queuing.Redis.Utilities;
using Netcorext.Serialization;
using Netcorext.Worker;

namespace Netcorext.Mediator.Queuing.Redis.Runners;

internal class PendingStreamRunner : IWorkerRunner<ConsumerWorker>
{
    private static bool _isDetecting;

    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorOptions _mediatorOptions;
    private readonly RedisQueuing _queuing;
    private readonly RedisClient _redis;
    private readonly RedisOptions _options;
    private readonly ISerializer _serializer;
    private readonly ILogger<PendingStreamRunner> _logger;

    public PendingStreamRunner(IServiceProvider serviceProvider, MediatorOptions mediatorOptions, IQueuing queuing, RedisOptions options, ISerializer serializer, ILogger<PendingStreamRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _mediatorOptions = mediatorOptions;
        _queuing = (RedisQueuing)queuing;
        _redis = _queuing.Redis;
        _options = options;
        _serializer = serializer;
        _logger = logger;
    }

    public async Task InvokeAsync(ConsumerWorker worker, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_options.StreamIdleTime ?? RedisOptions.DEFAULT_STREAM_IDLE_TIME, cancellationToken);

            if (_isDetecting) continue;

            _isDetecting = true;

            try
            {
                var tasks = new List<Task>();

                foreach (var service in _mediatorOptions.ServiceMaps)
                {
                    var key = KeyHelper.Concat(_options.Prefix,
                                               service.Interface.GetGenericTypeDefinition() == typeof(IResponseHandler<,>) ? _options.GroupName : string.Empty,
                                               service.Service.FullName!);

                    tasks.Add(ClaimPendingStreamAsync(key, cancellationToken));
                }

                await Task.WhenAll(tasks.ToArray());
            }
            finally
            {
                _isDetecting = false;
            }
        }
    }

    private async Task ClaimPendingStreamAsync(string streamKey, CancellationToken cancellationToken = default)
    {
        var nextId = "-";

        while (!cancellationToken.IsCancellationRequested)
        {
            var pendingResult = (await _redis.XPendingAsync(streamKey, _options.GroupName, nextId, "+", _options.StreamBatchSize ?? RedisOptions.DEFAULT_STREAM_BATCH_SIZE))
                               .Where(t => t.idle > _options.StreamIdleTime)
                               .ToArray();

            if (!pendingResult.Any()) break;

            var pendingIds = pendingResult.Select(t => t.id).ToArray();

            nextId = "(" + pendingResult.Last().id;

            var entries = (await _redis.XClaimAsync(streamKey, _options.GroupName, _options.MachineName, _options.StreamIdleTime ?? RedisOptions.DEFAULT_STREAM_IDLE_TIME, pendingIds))
                         .Select(t => t.ToStreamData())
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
                                            MachineName = _options.MachineName
                                        }
                                      : new Message
                                        {
                                            ServiceType = rawMessage.ServiceType,
                                            RefererType = rawMessage.RefererType,
                                            Referer = rawMessage.Referer,
                                            GroupName = _options.GroupName,
                                            MachineName = _options.MachineName
                                        };

                    try
                    {
                        var result = await StreamDataSender.SendAsync(_serviceProvider, _queuing, entry, cancellationToken);

                        if (rawMessage.Referer != null) continue;

                        message.PayloadType = result?.GetType().AssemblyQualifiedName;
                        message.Payload = await _serializer.SerializeToUtf8BytesAsync(result, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "{Message}", e.Message);

                        message.Error = e.ToString();
                    }

                    message.CreationDate = DateTimeOffset.UtcNow;

                    var publishKey = KeyHelper.GetStreamKey(KeyHelper.Concat(_options.Prefix, rawMessage.GroupName), rawMessage.ServiceType!);

                    await _queuing.PublishAsync(publishKey, message, cancellationToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "{Message}", e.Message);
                }
            }
        }

        var consumers = await _redis.XInfoConsumersAsync(streamKey, _options.GroupName);

        var pendingConsumers = consumers.Where(t => t.name != _options.MachineName && t.idle > _options.StreamIdleTime)
                                        .ToArray();

        if (pendingConsumers.Any())
            pendingConsumers.ForEach(async t => await _redis.XGroupDelConsumerAsync(streamKey, _options.GroupName, t.name));
    }

    public void Dispose()
    {
        _redis.Dispose();
    }
}