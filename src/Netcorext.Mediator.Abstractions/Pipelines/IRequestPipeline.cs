namespace Netcorext.Mediator.Pipelines;

public interface IRequestPipeline<TRequest> where TRequest : class, IRequest
{
    Task InvokeAsync(TRequest request, RequestPipelineDelegate<TRequest> next, CancellationToken cancellationToken = default);
}