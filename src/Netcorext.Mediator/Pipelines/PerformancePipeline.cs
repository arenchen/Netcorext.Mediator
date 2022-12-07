using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Netcorext.Mediator.Pipelines;

public class PerformancePipeline : IPipeline
{
    private readonly PerformanceOptions _options;
    private readonly ILogger<PerformancePipeline> _logger;

    public PerformancePipeline(PerformanceOptions options, ILogger<PerformancePipeline> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<TResult?> InvokeAsync<TResult>(IRequest<TResult> request, PipelineDelegate<TResult> next, CancellationToken cancellationToken = default)
    {
        var stopwatch = new Stopwatch();

        var type = request.GetType();

        try
        {
            stopwatch.Start();

            return await next(request, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > _options.SlowCommandTimes)
                _logger.LogWarning("'{TypeFullName}' processing too slow, elapsed: {StopwatchElapsed}", type.FullName, stopwatch.Elapsed);
        }
    }
}