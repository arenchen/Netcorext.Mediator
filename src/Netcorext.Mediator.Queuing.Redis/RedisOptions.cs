using System.Reflection;

namespace Netcorext.Mediator.Queuing.Redis;

public class RedisOptions
{
    public const string DEFAULT_COMMUNICATION_CHANNEL = "notification";
    public const int DEFAULT_SLOW_COMMAND_TIMES = 2 * 1000;
    public const int DEFAULT_STREAM_IDLE_TIME = 5 * 1000;
    public const int DEFAULT_STREAM_BATCH_SIZE = 50;
    public const int DEFAULT_HEALTH_CHECK_INTERVAL = 30 * 1000;

    public string GroupName { get; } = Assembly.GetEntryAssembly()?.GetName().Name!;
    public bool GroupNewestId { get; set; }
    public string MachineName { get; set; } = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
    public string ConnectionString { get; set; } = null!;
    public string? Prefix { get; set; }
    public string CommunicationChannel { get; set; } = DEFAULT_COMMUNICATION_CHANNEL;
    public int SlowCommandTimes { get; set; } = DEFAULT_SLOW_COMMAND_TIMES;
    public int? StreamIdleTime { get; set; } = DEFAULT_STREAM_IDLE_TIME;
    public int? StreamBatchSize { get; set; } = DEFAULT_STREAM_BATCH_SIZE;
    public int? StreamMaxSize { get; set; }
    public int? HealthCheckInterval { get; set; } = DEFAULT_HEALTH_CHECK_INTERVAL;
}