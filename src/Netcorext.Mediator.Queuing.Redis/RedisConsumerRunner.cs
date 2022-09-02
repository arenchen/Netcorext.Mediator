using System.Reflection;
using FreeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netcorext.Mediator.Queuing.Redis.Extensions;
using Netcorext.Mediator.Queuing.Redis.Helpers;

namespace Netcorext.Mediator.Queuing.Redis;

internal class RedisConsumerRunner : IConsumerRunner
{
    private readonly RedisQueuing _queuing;
    private readonly RedisClient _redis;
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorOptions _mediatorOptions;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisConsumerRunner> _logger;

    public RedisConsumerRunner(IServiceProvider serviceProvider, MediatorOptions mediatorOptions, IQueuing queuing, RedisOptions options, ILogger<RedisConsumerRunner> logger)
    {
        _queuing = (RedisQueuing)queuing;
        _redis = _queuing.RedisClient;
        _serviceProvider = serviceProvider;
        _mediatorOptions = mediatorOptions;
        _options = options;
        _logger = logger;
        _redis.Connected += (sender, args) => _logger.LogTrace("{Log}", args.Pool.StatisticsFullily);
        _redis.Unavailable += (sender, args) => _logger.LogTrace("{Log}", args.Pool.UnavailableException?.Message);
        _redis.Notice += (_, args) => _logger.LogTrace("{Log}", args.Log);
    }

    public async Task InvokeAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();
        var channels = new List<string>();

        foreach (var service in _mediatorOptions.ServiceMaps)
        {
            var key = KeyHelper.Concat(_options.Prefix,
                                       service.Interface.GetGenericTypeDefinition() == typeof(IResponseHandler<,>) ? _options.GroupName : string.Empty,
                                       service.Service.FullName!);

            if (!_redis.Exists(key) || _redis.XInfoGroups(key).All(t => t.name != _options.GroupName)) _redis.XGroupCreate(key, _options.GroupName, _options.GroupNewestId ? "$" : "0", true);

            if (!string.IsNullOrWhiteSpace(_options.MachineName) && _redis.XInfoConsumers(key, _options.GroupName).All(t => t.name != _options.MachineName)) _redis.XGroupCreateConsumer(key, _options.GroupName, _options.MachineName);

            var pendingResult = _redis.XPending(key, _options.GroupName, "-", "+", _options.StreamBatchSize ?? RedisOptions.DEFAULT_STREAM_BATCH_SIZE)
                                      .Where(t => t.idle > (_options.StreamIdleTime ?? RedisOptions.DEFAULT_STREAM_IDLE_TIME))
                                      .ToArray();

            var hasPending = false;

            while (pendingResult.Any())
            {
                var pendingIds = pendingResult.Select(t => t.id).ToArray();
                var lastId = pendingIds.Last();
                lastId = lastId.Split("-")[0];
                var nextId = lastId + "-9999";
                pendingIds = _redis.XClaimJustId(key, _options.GroupName, _options.MachineName, _options.StreamIdleTime ?? RedisOptions.DEFAULT_STREAM_IDLE_TIME, pendingIds);

                if (pendingIds.Any())
                {
                    hasPending = true;
                }

                pendingResult = _redis.XPending(key, _options.GroupName, nextId, "+", _options.StreamBatchSize ?? RedisOptions.DEFAULT_STREAM_BATCH_SIZE)
                                      .Where(t => t.idle > (_options.StreamIdleTime ?? RedisOptions.DEFAULT_STREAM_IDLE_TIME))
                                      .ToArray();
            }

            if (hasPending) tasks.Add(ReadStreamAsync(key, "0", cancellationToken));

            channels.Add(key);
        }

        tasks.Add(_queuing.SubscribeAsync(channels.ToArray(), (s, o) => ReadStreamAsync(o.ToString(), ">", cancellationToken), cancellationToken));

        await Task.WhenAll(tasks.ToArray());
    }

    private async Task ReadStreamAsync(string key, string offset = ">", CancellationToken cancellationToken = default)
    {
        var entries = _redis.XReadGroup(_options.GroupName,
                                        _options.MachineName,
                                        _options.StreamBatchSize ?? 0,
                                        _options.StreamBlockTimeout ?? 0,
                                        false,
                                        new Dictionary<string, string> { { key, offset } })
                            .SelectMany(t => t.entries.Select(t2 => new StreamMessage
                                                                    {
                                                                        Key = t.key,
                                                                        StreamId = t2.id,
                                                                        Message = t2.ToMessage(_options)
                                                                    }))
                            .ToArray();

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Message == null) continue;

                var message = entry.Message.Referer == null
                                  ? new Message
                                    {
                                        ServiceType = entry.Message.ServiceType,
                                        RefererType = entry.Message.PayloadType,
                                        Referer = entry.Message.Payload,
                                        GroupName = _options.GroupName,
                                        MachineName = _options.MachineName
                                    }
                                  : new Message
                                    {
                                        ServiceType = entry.Message.ServiceType,
                                        RefererType = entry.Message.RefererType,
                                        Referer = entry.Message.Referer,
                                        GroupName = _options.GroupName,
                                        MachineName = _options.MachineName
                                    };

                try
                {
                    var result = await SendAsync(entry, cancellationToken);

                    if (entry.Message.Referer != null) return;

                    message.PayloadType = result.GetType().AssemblyQualifiedName;
                    message.Payload = result;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "{E}", e);
                    
                    message.Error = e.ToString();
                }

                message.CreationDate = DateTimeOffset.UtcNow;

                var streamKey = KeyHelper.GetStreamKey(KeyHelper.Concat(_options.Prefix, entry.Message.GroupName), entry.Message.ServiceType);

                await _queuing.PublishAsync(streamKey, message, cancellationToken);
            }
            finally { }
        }
    }

    private async Task<object?> SendAsync(StreamMessage stream, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var provider = scope.ServiceProvider;
            var dispatcher = provider.GetRequiredService<IDispatcher>();
            var dispatcherInvokeMethodInfo = dispatcher.GetType().GetMethod(Constants.DISPATCHER_INVOKE, BindingFlags.Public | BindingFlags.Instance)!;

            var serviceType = (TypeInfo)Type.GetType(stream.Message.ServiceType);

            var resultType = serviceType!.ImplementedInterfaces.First(t => t.GetGenericTypeDefinition() == typeof(IRequest<>));

            var proxyInvokeAsync = dispatcherInvokeMethodInfo.MakeGenericMethod(resultType.GenericTypeArguments[0]);

            object?[]? args = null;

            if (stream.Message.Referer != null) args = new[] { stream.Message.Payload, stream.Message.Error };

            var task = (Task?)proxyInvokeAsync.Invoke(dispatcher, new[] { stream.Message.Referer ?? stream.Message.Payload, cancellationToken, args });

            await task!.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty(Constants.TASK_RESULT);

            var result = resultProperty?.GetValue(task);

            return result;
        }
        finally
        {
            _redis.XAck(stream.Key, _options.GroupName, stream.StreamId);
        }
    }
}