using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Netcorext.Serialization;

namespace Netcorext.Mediator.Queuing.Redis.Utilities;

internal static class StreamDataSender
{
    public static async Task<object?> SendAsync(IServiceProvider serviceProvider, RedisQueuing queuing, StreamData stream, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider;
        var serializer = provider.GetRequiredService<ISerializer>();
        var redis = queuing.Redis;
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        
        try
        {
            var dispatcherInvokeMethodInfo = dispatcher.GetType().GetMethod(Constants.DISPATCHER_INVOKE, BindingFlags.Public | BindingFlags.Instance)!;

            if (stream.Data == null || !stream.Data.Any()) return null;

            var rawMessage = await serializer.DeserializeAsync<Message>(stream.Data, cancellationToken);

            if (rawMessage == null) return null;

            var serviceType = (TypeInfo)Type.GetType(rawMessage.ServiceType!)!;

            var resultType = serviceType.ImplementedInterfaces.First(t => t.GetGenericTypeDefinition() == typeof(IRequest<>));

            var proxyInvokeAsync = dispatcherInvokeMethodInfo.MakeGenericMethod(resultType.GenericTypeArguments[0]);

            object?[]? args = null;

            var payloadType = rawMessage.PayloadType == null ? null : Type.GetType(rawMessage.PayloadType);
            var payload = payloadType == null || rawMessage.Payload == null ? null : await serializer.DeserializeAsync(rawMessage.Payload, payloadType, cancellationToken);

            var refererType = rawMessage.RefererType == null ? null : Type.GetType(rawMessage.RefererType);
            var referer = refererType == null || rawMessage.Referer == null ? null : await serializer.DeserializeAsync(rawMessage.Referer, refererType, cancellationToken);

            if (rawMessage.Referer != null) args = new[] { payload, rawMessage.Error };

            var task = (Task?)proxyInvokeAsync.Invoke(dispatcher, new[] { referer ?? payload, cancellationToken, args });

            await task!.ConfigureAwait(false);

            var resultProperty = task.GetType().GetProperty(Constants.TASK_RESULT);

            var result = resultProperty?.GetValue(task);

            return result;
        }
        finally
        {
            await redis.XAckAsync(stream.Key, queuing.Options.GroupName, stream.StreamId);
        }
    }
}