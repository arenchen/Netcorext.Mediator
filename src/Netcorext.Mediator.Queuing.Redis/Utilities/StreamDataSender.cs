using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Netcorext.Contracts;
using Netcorext.Serialization;
using Serilog.Context;

namespace Netcorext.Mediator.Queuing.Redis.Utilities;

internal static class StreamDataSender
{
    public static async Task<object?> SendAsync(IServiceProvider serviceProvider, RedisQueuing queuing, StreamData stream, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var provider = scope.ServiceProvider;
        var contextState = provider.GetRequiredService<IContextState>();
        var serializer = provider.GetRequiredService<ISerializer>();
        var redis = queuing.Redis;
        var dispatcher = provider.GetRequiredService<IDispatcher>();

        try
        {
            var dispatcherInvokeMethodInfo = dispatcher.GetType().GetMethod(Constants.DISPATCHER_INVOKE, BindingFlags.Public | BindingFlags.Instance)!;

            if (stream.Data == null || !stream.Data.Any()) return null;

            var rawMessage = await serializer.DeserializeAsync<Message>(stream.Data, cancellationToken);

            if (rawMessage == null) return null;

            contextState.User = GetClaimsPrincipal(rawMessage.Authorization);
            contextState.RequestId = rawMessage.RequestId;

            LogContext.PushProperty("XRequestId", rawMessage.RequestId);

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

    public static ClaimsPrincipal? GetClaimsPrincipal(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var buffer = new Span<byte>(new byte[token.Length]);

        if (Convert.TryFromBase64String(token, buffer, out _))
            return GetBasicClaimsIdentity(token).ClaimsPrincipal;

        return GetBearerClaimsIdentity(token).ClaimsPrincipal;
    }

    private static (bool IsValid, ClaimsPrincipal? ClaimsPrincipal) GetBasicClaimsIdentity(string token)
    {
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));

        var client = raw.Split(":", StringSplitOptions.RemoveEmptyEntries);

        if (client.Length != 2 || !long.TryParse(client[0], out var clientId)) return (false, null);

        var claims = new List<Claim>
                     {
                         new(ClaimTypes.Name, client[0]),
                         new(ClaimTypes.UserData, token)
                     };

        var claimsIdentity = new ClaimsIdentity(claims.ToArray(), "AuthenticationTypes.Basic", ClaimTypes.Name, ClaimTypes.Role);

        return (true, new ClaimsPrincipal(claimsIdentity));
    }

    private static (bool IsValid, ClaimsPrincipal? ClaimsPrincipal) GetBearerClaimsIdentity(string token)
    {
        var jwtSecurityTokenHandler = new JwtSecurityTokenHandler();

        if (!jwtSecurityTokenHandler.CanReadToken(token)) return (false, null);

        var jwt = jwtSecurityTokenHandler.ReadJwtToken(token);

        var claims = new List<Claim>
                     {
                         new(ClaimTypes.UserData, token)
                     };

        foreach (var tokenClaim in jwt.Claims)
        {
            switch (tokenClaim.Type)
            {
                case JwtRegisteredClaimNames.NameId:
                case JwtRegisteredClaimNames.UniqueName:
                    claims.Add(new Claim(ClaimTypes.Name, tokenClaim.Value, tokenClaim.ValueType, tokenClaim.Issuer, tokenClaim.OriginalIssuer));

                    break;
                case "role":
                case "roles":
                    claims.Add(new Claim(ClaimTypes.Role, tokenClaim.Value, tokenClaim.ValueType, tokenClaim.Issuer, tokenClaim.OriginalIssuer));

                    break;
                default:
                    claims.Add(tokenClaim);

                    break;
            }
        }

        var claimsIdentity = new ClaimsIdentity(claims.ToArray(), "AuthenticationTypes.Federation", ClaimTypes.Name, ClaimTypes.Role);

        return (true, new ClaimsPrincipal(claimsIdentity));
    }
}
