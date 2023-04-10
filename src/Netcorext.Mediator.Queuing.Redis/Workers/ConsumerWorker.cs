using Microsoft.Extensions.Logging;
using Netcorext.Mediator.Queuing.Redis;
using Netcorext.Worker;

namespace Netcorext.Mediator;

internal class ConsumerWorker : BackgroundWorker
{
    private readonly RedisOptions _options;
    private readonly IEnumerable<IWorkerRunner<ConsumerWorker>> _runners;
    private readonly ILogger<ConsumerWorker> _logger;
    private int _retryCount;

    public ConsumerWorker(RedisOptions options, IEnumerable<IWorkerRunner<ConsumerWorker>> runners, ILogger<ConsumerWorker> logger)
    {
        _options = options;
        _runners = runners;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_runners.Any())
            return;

        var ct = new CancellationTokenSource();

        var tasks = _runners.Select(t => t.InvokeAsync(this, ct.Token))
                            .ToArray();

        try
        {
            await Task.WhenAll(tasks);
            
            _retryCount = 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "${Message}", e.Message);

            ct.Cancel();

            _retryCount++;
            
            if (_retryCount <= _options.RetryLimit)
                await ExecuteAsync(cancellationToken);
        }
    }

    public override void Dispose()
    {
        foreach (var disposable in _runners)
        {
            disposable.Dispose();
        }

        base.Dispose();
    }
}