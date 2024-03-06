using System.Security.Claims;

namespace Netcorext.Mediator.Queuing;

public sealed class Message
{
    public string? ServiceType { get; set; }
    public string? PayloadType { get; set; }
    public byte[]? Payload { get; set; }
    public string? Error { get; set; }
    public string? RefererType { get; set; }
    public byte[]? Referer { get; set; }
    public string? GroupName { get; set; }
    public string? MachineName { get; set; }
    public DateTimeOffset CreationDate { get; set; }
    public string? Authorization { get; set; }
    public string? RequestId { get; set; }
}
