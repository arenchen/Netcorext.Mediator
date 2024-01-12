using Microsoft.Extensions.Logging;

namespace Netcorext.Mediator.Pipelines;

public class LoggingPipeline : IPipeline
{
    private readonly LoggingOptions _options;
    private readonly ILogger<LoggingPipeline> _logger;

    public LoggingPipeline(LoggingOptions options, ILogger<LoggingPipeline> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<TResult?> InvokeAsync<TResult>(IRequest<TResult> request, PipelineDelegate<TResult> next, CancellationToken cancellationToken = default)
    {
        var type = request.GetType();

        try
        {
            if (_options.EnableLog.HasValue && _options.EnableLog.Value.HasFlag(LoggingOptions.LogMode.Request))
                _logger.LogInformation("Request starting '{@TypeFullName}': {@Request}", type.FullName, request);

            var result = await next(request, cancellationToken);

            if (_options.EnableLog.HasValue && _options.EnableLog.Value.HasFlag(LoggingOptions.LogMode.Response))
                _logger.LogInformation("Request finished '{@TypeFullName}': {@Response}", type.FullName, result);

            return result;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Request failed '{@TypeFullName}': {@Request}", type.FullName, request);

            throw;
        }
    }
}
