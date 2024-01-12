namespace Netcorext.Mediator.Pipelines;

public class LoggingOptions
{
    public LogMode? EnableLog { get; set; }

    [Flags]
    public enum LogMode
    {
        None,
        Request,
        Response,
        Both = Request | Response
    }
}
