using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Netcorext.Mediator.Helpers;
using Netcorext.Mediator.Pipelines;
using Netcorext.Mediator.Queuing;

namespace Netcorext.Mediator;

public class Dispatcher : IDispatcher
{
    private readonly IQueuing _queuing;
    private readonly IEnumerable<IPipeline> _pipelines;
    private readonly MediatorOptions _options;
    private readonly Type _voidTaskResult = Type.GetType("System.Threading.Tasks.VoidTaskResult");

    public Dispatcher(IQueuing queuing, IEnumerable<IPipeline> pipelines, MediatorOptions options)
    {
        _queuing = queuing;
        _pipelines = pipelines;
        _options = options;
    }

    public Task<TResult?> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(request, cancellationToken);
    }

    public async Task<string> PublishAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
    {
        return await _queuing.PublishAsync(request, cancellationToken);
    }

    public async Task<TResult?> InvokeAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default, params object?[]? parameters)
    {
        PipelineDelegate<TResult?> pipeline = async (req, ct) =>
                                              {
                                                  var args = new List<object?> { req };
                                                  if (parameters != null && parameters.Any()) args.AddRange(parameters);
                                                  args.Add(cancellationToken);
                                                  parameters = args.ToArray();

                                                  var handlerType = ServiceHandlerHelper.FindHandler(_options.ServiceMaps, parameters);

                                                  if (handlerType == null) throw new ArgumentNullException(nameof(handlerType), "Service handler not found");

                                                  //var genericType = handlerType.MakeGenericType(req.GetType(), typeof(TResult));

                                                  var handler = _options.ServiceProvider.GetRequiredService(handlerType);

                                                  var method = handlerType.GetMethod(Constants.HANDLER_METHOD, BindingFlags.Public | BindingFlags.Instance);

                                                  var task = (Task)method?.Invoke(handler, parameters)!;

                                                  await task.ConfigureAwait(false);

                                                  var resultProperty = task.GetType().GetProperty(Constants.TASK_RESULT);

                                                  var result = resultProperty!.GetValue(task);

                                                  return result == null || result.GetType() == _voidTaskResult ? default : (TResult)result;
                                              };

        var result = _pipelines.Select(t => new Func<PipelineDelegate<TResult?>, PipelineDelegate<TResult?>>(pipe => async (msg, token) => await t.InvokeAsync(msg, pipe, token)))
                               .Reverse()
                               .Aggregate(pipeline, (current, next) => next(current));

        return await result.Invoke(request, cancellationToken);
    }
}