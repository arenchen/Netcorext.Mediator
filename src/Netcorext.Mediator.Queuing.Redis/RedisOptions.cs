using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netcorext.Mediator.Queuing.Redis;

public class RedisOptions
{
    public const string DEFAULT_COMMUNICATION_CHANNEL = "notification";
    public const int DEFAULT_SLOW_COMMAND_TIMES = 2 * 1000;
    public const int DEFAULT_STREAM_BATCH_SIZE = 50;
    public const int DEFAULT_STREAM_BLOCK_TIMEOUT = 0;
    public const int DEFAULT_HEALTH_CHECK_INTERVAL = 10 * 1000;

    public string GroupName { get; } = Assembly.GetEntryAssembly()?.GetName().Name;
    public string MachineName { get; set; } = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
    public string ConnectionString { get; set; }
    public string Prefix { get; set; }
    public string CommunicationChannel { get; set; } = DEFAULT_COMMUNICATION_CHANNEL;
    public int SlowCommandTimes { get; set; } = DEFAULT_SLOW_COMMAND_TIMES;
    public int? StreamBatchSize { get; set; } = DEFAULT_STREAM_BATCH_SIZE;
    public int? StreamBlockTimeout { get; set; } = DEFAULT_STREAM_BLOCK_TIMEOUT;
    public int? StreamMaxSize { get; set; }
    public int? HealthCheckInterval { get; set; } = DEFAULT_HEALTH_CHECK_INTERVAL;

    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions
                                                                       {
                                                                           PropertyNameCaseInsensitive = true,
                                                                           DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                                                                           WriteIndented = false,
                                                                           PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                           ReferenceHandler = ReferenceHandler.IgnoreCycles
                                                                       };
}