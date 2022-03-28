namespace Netcorext.Mediator;

public interface IConsumerRunner
{
    Task InvokeAsync(IEnumerable<ServiceMap> services, CancellationToken cancellationToken = default);
}