namespace Netcorext.Mediator.Internals;

internal class DefaultConsumerRunner : IConsumerRunner
{
    public Task InvokeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}