using System.Diagnostics;
using FreeRedis;
using Microsoft.Extensions.Logging;
using Netcorext.Mediator.Queuing.Redis.Helpers;
using Netcorext.Serialization;

namespace Netcorext.Mediator.Queuing.Redis;

internal class RedisQueuing : IQueuing, IDisposable
{
    private IDisposable? _subscriber;
    private readonly RedisOptions _options;
    private readonly ISerializer _serializer;
    private readonly ILogger<RedisQueuing> _logger;
    private readonly string _communicationChannel;

    public RedisQueuing(RedisClient redis, RedisOptions options, ISerializer serializer, ILogger<RedisQueuing> logger)
    {
        Redis = redis;

        _options = options;
        _serializer = serializer;
        _logger = logger;
        _communicationChannel = KeyHelper.Concat(_options.Prefix, _options.CommunicationChannel);
    }

    internal RedisClient Redis { get; }

    public async Task<string> PublishAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        var streamKey = KeyHelper.GetStreamKey(_options.Prefix, request.GetType().AssemblyQualifiedName!);

        var message = new Message
                      {
                          ServiceType = request.GetType().AssemblyQualifiedName,
                          PayloadType = request.GetType().AssemblyQualifiedName,
                          Payload = await _serializer.SerializeToUtf8BytesAsync(request, cancellationToken),
                          GroupName = _options.GroupName,
                          MachineName = _options.MachineName,
                          CreationDate = DateTimeOffset.UtcNow
                      };

        return await PublishAsync(streamKey, message, cancellationToken);
    }

    internal async Task<string> PublishAsync(string key, Message message, CancellationToken cancellationToken = default)
    {
        var stopwatch = new Stopwatch();

        stopwatch.Start();

        try
        {
            var content = await _serializer.SerializeToUtf8BytesAsync(message, cancellationToken);

            if (content == null) throw new ArgumentNullException(nameof(content));

            var values = new Dictionary<string, object>
                         {
                             { nameof(StreamData.Key), key },
                             { nameof(StreamData.Timestamp), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                             { nameof(StreamData.Data), content }
                         };

            var streamId = await Redis.XAddAsync(key, _options.StreamMaxSize ?? 0L, "*", values);

            await Redis.PublishAsync(_communicationChannel, key);

            return streamId;
        }
        finally
        {
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > _options.SlowCommandTimes)
                _logger.LogWarning("'{Name}' processing too slow, elapsed: {StopwatchElapsed}", nameof(PublishAsync), stopwatch.Elapsed);
        }
    }

    public Task SubscribeAsync(string[] channels, Action<string, object> handler, CancellationToken cancellationToken = default)
    {
        var stopwatch = new Stopwatch();

        stopwatch.Start();

        try
        {
            _subscriber?.Dispose();

            _subscriber = Redis.Subscribe(_communicationChannel, (c, m) =>
                                                                 {
                                                                     if (channels.Any(t => t == m.ToString()))
                                                                         handler(c, m);
                                                                 });
        }
        finally
        {
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > _options.SlowCommandTimes)
                _logger.LogWarning("'{Name}' processing too slow, elapsed: {StopwatchElapsed}", nameof(SubscribeAsync), stopwatch.Elapsed);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscriber?.Dispose();

        Redis.Dispose();
    }
}