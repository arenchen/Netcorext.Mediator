namespace Netcorext.Mediator.Pipelines;

public delegate Task RequestPipelineDelegate<in TRequest>(TRequest request, CancellationToken cancellationToken = default);