namespace Netcorext.Mediator.Pipelines;

public delegate Task<TResult> PipelineDelegate<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default);