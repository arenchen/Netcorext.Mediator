using Microsoft.Extensions.Hosting;

namespace Netcorext.Mediator.Internals;

internal class ConsumerWorker : IHostedService, IDisposable
{
    private readonly MediatorOptions _options;
    private readonly IConsumerRunner _runner;
    private Task _executingTask = null!;
    private CancellationTokenSource _cancellationToken = new CancellationTokenSource();

    public ConsumerWorker(MediatorOptions options, IConsumerRunner runner)
    {
        _options = options;
        _runner = runner;
    }

    private Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return _runner.InvokeAsync(_options.ServiceMaps, cancellationToken);
    }

    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            _cancellationToken.Dispose();
            _cancellationToken = new CancellationTokenSource();
        }

        _executingTask = ExecuteAsync(_cancellationToken.Token);

        return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
    }

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask == null)
        {
            return;
        }

        try
        {
            _cancellationToken.Cancel();
        }
        finally
        {
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public virtual void Dispose()
    {
        _cancellationToken.Cancel();
    }
}