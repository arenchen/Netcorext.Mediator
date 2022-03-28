namespace Netcorext.Mediator.Queuing;

public sealed class Message
{
    public string? ServiceType { get; set; }
    public string? PayloadType { get; set; }
    public object? Payload { get; set; }
    public string? Error { get; set; }
    public string? RefererType { get; set; }
    public object? Referer { get; set; }
    public string? GroupName { get; set; }
    public string? MachineName { get; set; }
    public DateTimeOffset CreationDate { get; set; }
}