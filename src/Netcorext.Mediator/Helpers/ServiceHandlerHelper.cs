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

            var isEq = true;

            for (var i = 0; i < ps.Length; i++)
            {
                if (parameters[i] == null
                 || ps[i].ParameterType.IsInstanceOfType(parameters[i])
                 || ps[i].ParameterType == parameters[i]?.GetType()) continue;

                isEq = false;

                break;
            }

            if (!isEq) continue;

            return map.Interface;
        }

        return null;
    }
}