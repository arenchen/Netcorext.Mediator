namespace Netcorext.Mediator.Pipelines;

public interface IRequestPipeline<in TRequest, TResult> where TRequest : class, IRequest<TResult>
{
    Task<TResult?> InvokeAsync(TRequest request, PipelineDelegate<TResult> next, CancellationToken cancellationToken = default);
}