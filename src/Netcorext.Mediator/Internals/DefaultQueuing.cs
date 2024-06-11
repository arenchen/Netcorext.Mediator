using Netcorext.Mediator.Queuing;

namespace Netcorext.Mediator.Internals;

internal class DefaultQueuing : IQueuing
{
    public Task<string> PublishAsync<TResult>(IRequest<TResult> request, bool respond = false, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task SubscribeAsync(string[] channels, Action<string, object> handler, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
