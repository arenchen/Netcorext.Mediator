namespace Netcorext.Mediator.Queuing.Redis;

internal class StreamMessage
{
    public string Key { get; set; } = null!;
    public string StreamId { get; set; } = null!;
    public Message? Message { get; set; }
}