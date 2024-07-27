namespace Netcorext.Mediator;

public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
    Task<string> PublishAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);
    Task<string> PublishAsync<TResult>(IRequest<TResult> request, bool respond = false, CancellationToken cancellationToken = default);
}
