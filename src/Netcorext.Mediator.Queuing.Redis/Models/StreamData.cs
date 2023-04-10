namespace Netcorext.Mediator.Queuing.Redis;

internal class StreamData
{
    public string? Key { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public string? StreamId { get; set; }
    public string? Data { get; set; }
}