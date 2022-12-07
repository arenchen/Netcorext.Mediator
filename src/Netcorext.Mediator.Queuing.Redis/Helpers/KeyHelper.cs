using System.Text.RegularExpressions;

namespace Netcorext.Mediator.Queuing.Redis.Helpers;

internal static class KeyHelper
{
    private static readonly Regex RegexKeyFormat = new Regex("[^(?:a-zA-Z0-9._\\-)]+", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex RegexType = new Regex(",.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string FormatKey(this string source)
    {
        return RegexKeyFormat.Replace(source, "_").ToLower();
    }

    public static string GetStreamKey(string? prefix, string payloadType)
    {
        var type = RegexType.Replace(payloadType, "");

        return Concat(prefix, type.FormatKey());
    }

    public static string Concat(params string?[] keys)
    {
        return keys.Where(t => !string.IsNullOrWhiteSpace(t))
                   .Select(t => t!)
                   .Aggregate((s, s1) => s + ":" + s1)
                   .ToLower();
    }
}