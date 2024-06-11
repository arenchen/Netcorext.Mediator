namespace Netcorext.Mediator.Queuing;

public interface IQueuing
{
    Task<string> PublishAsync<TResult>(IRequest<TResult> request, bool respond = false, CancellationToken cancellationToken = default);
    Task SubscribeAsync(string[] channels, Action<string, object> handler, CancellationToken cancellationToken = default);
}
