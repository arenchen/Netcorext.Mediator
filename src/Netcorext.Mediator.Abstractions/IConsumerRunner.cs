namespace Netcorext.Mediator;

public interface IConsumerRunner
{
    Task InvokeAsync(CancellationToken cancellationToken = default);
}