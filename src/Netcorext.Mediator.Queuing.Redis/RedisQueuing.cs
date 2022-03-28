using System.Diagnostics;
using System.Text.Json;
using FreeRedis;
using Microsoft.Extensions.Logging;
using Netcorext.Mediator.Queuing.Redis.Extensions;
using Netcorext.Mediator.Queuing.Redis.Helpers;

namespace Netcorext.Mediator.Queuing.Redis;

public class RedisQueuing : IQueuing, IDisposable
{
    private readonly RedisOptions _options;
    private readonly ILogger<RedisQueuing> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _communicationChannel;
    private List<IDisposable> _subscribes = new List<IDisposable>();

    public RedisQueuing(RedisClient redis, RedisOptions options, ILogger<RedisQueuing> logger)
    {
        RedisClient = redis;
        _options = options;
        _logger = logger;
        _communicationChannel = KeyHelper.Concat(_options.Prefix, _options.CommunicationChannel);
    }

    internal RedisClient RedisClient { get; }

    public Task<string> PublishAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        var streamKey = KeyHelper.GetStreamKey(_options.Prefix, request.GetType().AssemblyQualifiedName!);

        var message = new Message
                      {
                          ServiceType = request.GetType().AssemblyQualifiedName,
                          PayloadType = request.GetType().AssemblyQualifiedName,
                          Payload = request,
                          GroupName = _options.GroupName,
                          MachineName = _options.MachineName,
                          CreationDate = DateTimeOffset.UtcNow
                      };

        return PublishAsync(streamKey, message, cancellationToken);
    }

    internal Task<string> PublishAsync(string key, Message message, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
                        {
                            var stopwatch = new Stopwatch();

                            stopwatch.Start();

                            try
                            {
                                var fieldValues = message.ToDictionary(_options);

                                var streamId = RedisClient.XAdd(key, 0L, "*", fieldValues);

                                RedisClient.Publish(_communicationChannel, key);

                                return streamId;
                            }
                            finally
                            {
                                stopwatch.Stop();

                                if (stopwatch.ElapsedMilliseconds > _options.SlowCommandTimes)
                                    _logger.LogWarning($"'{nameof(PublishAsync)}' processing too slow, elapsed: {stopwatch.Elapsed}");
                            }
                        }, cancellationToken);
    }

    public Task SubscribeAsync(string[] channels, Action<string, object> handler, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
                        {
                            var stopwatch = new Stopwatch();

                            stopwatch.Start();

                            try
                            {
                                _subscribes.Add(RedisClient.Subscribe(_communicationChannel, (c, s) =>
                                                                                             {
                                                                                                 if (channels.Any(t => t.Equals(s.ToString(), StringComparison.OrdinalIgnoreCase)))
                                                                                                 {
                                                                                                     handler.Invoke(c, s);
                                                                                                 }
                                                                                             }));
                            }
                            finally
                            {
                                stopwatch.Stop();

                                if (stopwatch.ElapsedMilliseconds > _options.SlowCommandTimes)
                                    _logger.LogWarning($"'{nameof(SubscribeAsync)}' processing too slow, elapsed: {stopwatch.Elapsed}");
                            }
                        }, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var disposable in _subscribes)
        {
            disposable.Dispose();
        }

        _subscribes.Clear();
        RedisClient?.Dispose();
    }
}