namespace Netcorext.Mediator.Queuing.Redis;

internal class StreamMessage
{
    public string Key { get; set; }
    public string StreamId { get; set; }
    public Message? Message { get; set; }
}