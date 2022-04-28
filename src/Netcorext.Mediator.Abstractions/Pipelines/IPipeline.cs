namespace Netcorext.Mediator.Pipelines;

public interface IPipeline 
{
    Task<TResult?> InvokeAsync<TResult>(IRequest<TResult> request, PipelineDelegate<TResult> next, CancellationToken cancellationToken = default);
}