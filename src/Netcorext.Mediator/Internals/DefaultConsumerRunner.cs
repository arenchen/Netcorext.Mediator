namespace Netcorext.Mediator.Internals;

internal class DefaultConsumerRunner : IConsumerRunner
{
    public Task InvokeAsync(IEnumerable<ServiceMap> services, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}