using System.Collections.Concurrent;
using System.Reflection;
using Netcorext.Mediator.Pipelines;
using Netcorext.Mediator.Queuing;

namespace Netcorext.Mediator;

public class Dispatcher : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> MethodCache = new();
    private static readonly ConcurrentDictionary<Type, Type> PipelineTypeCache = new();

    private readonly IQueuing _queuing;
    private readonly IEnumerable<IPipeline> _pipelines;
    private readonly IServiceProvider _serviceProvider;
    private readonly Type _voidTaskResult = Type.GetType("System.Threading.Tasks.VoidTaskResult")!;
    private readonly Dictionary<Type, ServiceMap> _serviceMapDictionary;

    public Dispatcher(IServiceProvider serviceProvider, IQueuing queuing, IEnumerable<IPipeline> pipelines, MediatorOptions options)
    {
        _serviceProvider = serviceProvider;
        _queuing = queuing;
        _pipelines = pipelines;
        _serviceMapDictionary = options.ServiceMaps.ToDictionary(k => k.Service, v => v);
    }

    public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(request, cancellationToken)!;
    }

    public async Task<string> PublishAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        return await PublishAsync(request, false, cancellationToken);
    }

    public async Task<string> PublishAsync<TResult>(IRequest<TResult> request, bool respond = false, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        return await _queuing.PublishAsync(request, respond, cancellationToken);
    }

    public async Task<TResult?> InvokeAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default, params object?[]? parameters)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var args = new List<object?> { request };

        parameters = args.Concat(parameters ?? Enumerable.Empty<object?>())
                         .Append(cancellationToken)
                         .ToArray();

        var requestType = request.GetType();

        if (!_serviceMapDictionary.TryGetValue(requestType, out var map))
            throw new KeyNotFoundException($"ServiceMap not found for requestType: {requestType}");

        var handlerType = map.Interface;

        if (handlerType == null)
            throw new InvalidOperationException("Service handler not found");

        var handler = _serviceProvider.GetRequiredService(handlerType);

        var method = MethodCache.GetOrAdd(handlerType, t => t.GetMethod(Constants.HANDLER_METHOD, BindingFlags.Public | BindingFlags.Instance));

        var pipelineType = PipelineTypeCache.GetOrAdd(requestType, t => typeof(IRequestPipeline<,>).MakeGenericType(t, typeof(TResult)));

        var pipelineMethodInfo = pipelineType.GetMethod("InvokeAsync");

        var result = _serviceProvider.GetServices(pipelineType)
                                     .Select(CreateServicePipelineFunc)
                                     .Reverse()
                                     .Concat(_pipelines.Where(ShouldIncludePipeline).Select(CreatePipelineFunc).Reverse())
                                     .Aggregate((PipelineDelegate<TResult>)Pipeline, (current, next) => next(current));

        return await result.Invoke(request, cancellationToken);

        Func<PipelineDelegate<TResult>, PipelineDelegate<TResult>> CreateServicePipelineFunc(object? service)
        {
            return pipe => async (msg, token) =>
                               await (Task<TResult>)pipelineMethodInfo?.Invoke(service, new object[] { msg, pipe, token })!;
        }

        Func<PipelineDelegate<TResult>, PipelineDelegate<TResult>> CreatePipelineFunc(IPipeline pipeline)
        {
            return pipe => async (msg, token) => await pipeline.InvokeAsync(msg, pipe, token);
        }

        bool ShouldIncludePipeline(IPipeline pipeline)
        {
            return handlerType.GetGenericTypeDefinition() != typeof(IResponseHandler<,>) ||
                   (handlerType.GetGenericTypeDefinition() == typeof(IResponseHandler<,>) && pipeline.GetType() != typeof(ValidatorPipeline));
        }

        async Task<TResult?> Pipeline(IRequest<TResult> req, CancellationToken ct)
        {
            var task = (Task)method?.Invoke(handler, parameters)!;

            await task.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty(Constants.TASK_RESULT);

            var value = resultProperty!.GetValue(task);

            return value == null || value.GetType() == _voidTaskResult ? default : (TResult)value;
        }
    }
}
