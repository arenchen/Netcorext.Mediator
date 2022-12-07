using System.Reflection;

namespace Netcorext.Mediator.Helpers;

public static class ServiceHandlerHelper
{
    public static Type? FindHandler(IEnumerable<ServiceMap> serviceMaps, params object?[]? parameters)
    {
        if (parameters == null || !parameters.Any()) return null;

        foreach (var map in serviceMaps)
        {
            var mi = map.Implementation.GetMethod(Constants.HANDLER_METHOD, BindingFlags.Instance | BindingFlags.Public);

            if (mi == null) continue;

            var ps = mi.GetParameters();

            if (ps.Length != parameters.Length) continue;

            var isEq = !ps.Where((t, i) => parameters[i] != null && !t.ParameterType.IsInstanceOfType(parameters[i]) && t.ParameterType != parameters[i]?.GetType()).Any();

            if (!isEq) continue;

            return map.Interface;
        }

        return null;
    }
}