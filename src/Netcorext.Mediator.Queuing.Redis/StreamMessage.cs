namespace Netcorext.Mediator.Queuing.Redis;

public class StreamMessage
{
    public string Key { get; set; }
    public string StreamId { get; set; }
    public Message? Message { get; set; }
}