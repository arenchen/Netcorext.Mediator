using System.Collections.Concurrent;
using System.Reflection;
using FreeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netcorext.Extensions.Linq;
using Netcorext.Mediator.Queuing.Redis.Extensions;
using Netcorext.Mediator.Queuing.Redis.Helpers;
using Netcorext.Serialization;

namespace Netcorext.Mediator.Queuing.Redis;

internal class RedisConsumerRunner : IConsumerRunner
{
    private readonly RedisQueuing _queuing;
    private readonly RedisClient _redis;
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorOptions _mediatorOptions;
    private readonly RedisOptions _options;
    private readonly ISerializer _serializer;
    private readonly ILogger<RedisConsumerRunner> _logger;
    private static readonly ConcurrentDictionary<string, string> ReadStreamDictionary = new();
    private static bool _isDetecting;

    public RedisConsumerRunner(IServiceProvider serviceProvider, MediatorOptions mediatorOptions, IQueuing queuing, RedisOptions options, ISerializer serializer, ILogger<RedisConsumerRunner> logger)
    {
        _queuing = (RedisQueuing)queuing;
        _redis = _queuing.Redis;
        _serviceProvider = serviceProvider;
        _mediatorOptions = mediatorOptions;
        _options = options;
        _serializer = serializer;
        _logger = logger;
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

            if (!await _redis.ExistsAsync(key) || (await _redis.XInfoGroupsAsync(key)).All(x => x.name != _options.GroupName))
                await _redis.XGroupCreateAsync(key, _options.GroupName, _options.GroupNewestId ? "$" : "0", true);

            var consumers = await _redis.XInfoConsumersAsync(key, _options.GroupName);

            if (consumers.All(x => x.name != _options.MachineName))
                await _redis.XGroupCreateConsumerAsync(key, _options.GroupName, _options.MachineName);

            channels.Add(key);
        }

        tasks.Add(_queuing.SubscribeAsync(channels.ToArray(), async (s, o) =>
                                                              {
                                                                  try
                                                                  {
                                                                      await ReadStreamAsync(o.ToString()!, cancellationToken);
                                                                  }
                                                                  catch
                                                                  {
                                                                      // ignored
                                                                  }
                                                              }, cancellationToken));

        tasks.Add(DetectPendingStreamAsync(cancellationToken));
        tasks.Add(HealthCheckAsync(cancellationToken));

        try
        {
            await Task.WhenAll(tasks.ToArray());
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{Message}", e);

            await InvokeAsync(cancellationToken);
        }
    }

    private async Task ReadStreamAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!ReadStreamDictionary.TryAdd(key, ">")) return;

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
                            var result = await SendAsync(entry, cancellationToken);

                            if (rawMessage.Referer != null) continue;

                            message.PayloadType = result?.GetType().AssemblyQualifiedName;
                            message.Payload = await _serializer.SerializeToUtf8BytesAsync(result, cancellationToken);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "{Message}", e);

                            message.Error = e.ToString();
                        }

                        message.CreationDate = DateTimeOffset.UtcNow;

                        var streamKey = KeyHelper.GetStreamKey(KeyHelper.Concat(_options.Prefix, rawMessage.GroupName), rawMessage.ServiceType!);

                        await _queuing.PublishAsync(streamKey, message, cancellationToken);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            ReadStreamDictionary.TryRemove(key, out _);
        }
        catch (Exception e)
        {
            ReadStreamDictionary.TryRemove(key, out _);

            _logger.LogError(e, "{Message}", e);
        }
    }

    private async Task<object?> SendAsync(StreamData stream, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var provider = scope.ServiceProvider;
            var dispatcher = provider.GetRequiredService<IDispatcher>();
            var dispatcherInvokeMethodInfo = dispatcher.GetType().GetMethod(Constants.DISPATCHER_INVOKE, BindingFlags.Public | BindingFlags.Instance)!;

            if (stream.Data == null || !stream.Data.Any()) return null;

            var rawMessage = await _serializer.DeserializeAsync<Message>(stream.Data, cancellationToken);

            if (rawMessage == null) return null;

            var serviceType = (TypeInfo)Type.GetType(rawMessage.ServiceType!)!;

            var resultType = serviceType!.ImplementedInterfaces.First(t => t.GetGenericTypeDefinition() == typeof(IRequest<>));

            var proxyInvokeAsync = dispatcherInvokeMethodInfo.MakeGenericMethod(resultType.GenericTypeArguments[0]);

            object?[]? args = null;

            var payloadType = rawMessage.PayloadType == null ? null : Type.GetType(rawMessage.PayloadType);
            var payload = payloadType == null || rawMessage.Payload == null ? null : await _serializer.DeserializeAsync(rawMessage.Payload, payloadType, cancellationToken);

            var refererType = rawMessage.RefererType == null ? null : Type.GetType(rawMessage.RefererType);
            var referer = refererType == null || rawMessage.Referer == null ? null : await _serializer.DeserializeAsync(rawMessage.Referer, refererType, cancellationToken);

            if (rawMessage.Referer != null) args = new[] { payload, rawMessage.Error };

            var task = (Task?)proxyInvokeAsync.Invoke(dispatcher, new[] { referer ?? payload, cancellationToken, args });

            await task!.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty(Constants.TASK_RESULT);

            var result = resultProperty?.GetValue(task);

            return result;
        }
        finally
        {
            await _redis.XAckAsync(stream.Key, _options.GroupName, stream.StreamId);
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
                        var result = await SendAsync(entry, cancellationToken);

                        if (rawMessage.Referer != null) continue;

                        message.PayloadType = result?.GetType().AssemblyQualifiedName;
                        message.Payload = await _serializer.SerializeToUtf8BytesAsync(result, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "{Message}", e);

                        message.Error = e.ToString();
                    }

                    message.CreationDate = DateTimeOffset.UtcNow;

                    var publishKey = KeyHelper.GetStreamKey(KeyHelper.Concat(_options.Prefix, rawMessage.GroupName), rawMessage.ServiceType!);

                    await _queuing.PublishAsync(publishKey, message, cancellationToken);
                }
                catch
                {
                    // ignored
                }
            }
        }

        var consumers = await _redis.XInfoConsumersAsync(streamKey, _options.GroupName);

        var pendingConsumers = consumers.Where(t => t.name != _options.MachineName && t.idle > _options.StreamIdleTime)
                                        .ToArray();

        if (pendingConsumers.Any())
            pendingConsumers.ForEach(async t => await _redis.XGroupDelConsumerAsync(streamKey, _options.GroupName, t.name));
    }

    private async Task DetectPendingStreamAsync(CancellationToken cancellationToken = default)
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

    private async Task HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Run(() =>
                           {
                               var pong = _redis.Echo("PONG");

                               _logger.LogDebug("{Message}", pong);
                           }, cancellationToken);

            await Task.Delay(_options.HealthCheckInterval ?? RedisOptions.DEFAULT_HEALTH_CHECK_INTERVAL, cancellationToken);
        }
    }
}