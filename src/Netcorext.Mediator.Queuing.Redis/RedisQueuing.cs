using System.Diagnostics;
using FreeRedis;
using Microsoft.Extensions.Logging;
using Netcorext.Extensions.Redis.Utilities;
using Netcorext.Mediator.Queuing.Redis.Helpers;
using Netcorext.Serialization;

namespace Netcorext.Mediator.Queuing.Redis;

internal class RedisQueuing : IQueuing, IDisposable
{
    private IDisposable? _subscriber;
    private readonly ILogger<RedisQueuing> _logger;
    private readonly string _communicationChannel;

    public RedisQueuing(RedisOptions options, ISerializer serializer, ILogger<RedisQueuing> logger)
    {
        _logger = logger;
        
        Options = options;
        Serializer = serializer;
        Redis = new RedisClientConnection<RedisClient>(() => new RedisClient(options.ConnectionString)
                                                             {
                                                                 Serialize = serializer.Serialize,
                                                                 Deserialize = serializer.Deserialize,
                                                                 DeserializeRaw = serializer.Deserialize
                                                             }).Client;
        
        _communicationChannel = KeyHelper.Concat(Options.Prefix, Options.CommunicationChannel);
    }

    internal RedisClient Redis { get; }
    internal ISerializer Serializer { get; }
    internal RedisOptions Options { get; }

    public async Task<string> PublishAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        var streamKey = KeyHelper.GetStreamKey(Options.Prefix, request.GetType().AssemblyQualifiedName!);

        var message = new Message
                      {
                          ServiceType = request.GetType().AssemblyQualifiedName,
                          PayloadType = request.GetType().AssemblyQualifiedName,
                          Payload = await Serializer.SerializeToUtf8BytesAsync(request, cancellationToken),
                          GroupName = Options.GroupName,
                          MachineName = Options.MachineName,
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
            var content = await Serializer.SerializeToUtf8BytesAsync(message, cancellationToken);

            if (content == null) throw new ArgumentNullException(nameof(content));

            var values = new Dictionary<string, object>
                         {
                             { nameof(StreamData.Key), key },
                             { nameof(StreamData.Timestamp), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                             { nameof(StreamData.Data), content }
                         };

            var streamId = await Redis.XAddAsync(key, Options.StreamMaxSize ?? 0L, "*", values);

            await Redis.PublishAsync(_communicationChannel, key);

            return streamId;
        }
        finally
        {
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > Options.SlowCommandTimes)
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

            if (stopwatch.ElapsedMilliseconds > Options.SlowCommandTimes)
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