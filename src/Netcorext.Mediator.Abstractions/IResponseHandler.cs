namespace Netcorext.Mediator;

public interface IResponseHandler<in TRequest, in TResult>
    where TRequest : IRequest<TResult?>
{
    Task Handle(TRequest request, TResult? result, string? error, CancellationToken cancellationToken = default);
}