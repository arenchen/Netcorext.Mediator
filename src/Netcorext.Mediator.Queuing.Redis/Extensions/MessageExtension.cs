using System.Reflection;
using System.Text.Json;
using FreeRedis;

namespace Netcorext.Mediator.Queuing.Redis.Extensions;

internal static class MessageExtension
{
    public static Dictionary<string, object> ToDictionary(this Message message, RedisOptions options)
    {
        var values = new Dictionary<string, object>();

        var props = message.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        foreach (var p in props)
        {
            var val = p.GetValue(message);

            if (val == null) continue;

            switch (val)
            {
                case string s:
                    values.Add(p.Name, s);

                    break;
                case Type t:
                    values.Add(p.Name, t.AssemblyQualifiedName!);

                    break;
                case DateTimeOffset dt:
                    values.Add(p.Name, dt.ToString("O"));

                    break;
                case TimeSpan ts:
                    values.Add(p.Name, ts.ToString());

                    break;
                default:
                    values.Add(p.Name, JsonSerializer.SerializeToUtf8Bytes(val, options.JsonSerializerOptions));

                    break;
            }
        }

        return values;
    }

    public static Message? ToMessage(this StreamsEntry source, RedisOptions options)
    {
        var message = new Message();
        var type = typeof(Message);

        if (source.fieldValues == null || source.fieldValues.Length < 2) return null;

        for (var i = 0; i < source.fieldValues.Length; i += 2)
        {
            var name = source.fieldValues[i].ToString()!;
            var value = source.fieldValues[i + 1].ToString();

            var pi = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (pi == null) continue;

            Type? pt;

            switch (pi.Name)
            {
                case nameof(Message.Payload):
                    if (message.PayloadType == null)
                    {
                        var typeString = source.FindValue(nameof(Message.PayloadType))?.ToString();

                        if (string.IsNullOrWhiteSpace(typeString)) break;

                        message.PayloadType = typeString;
                    }

                    if (message.PayloadType == null) break;

                    object? payload;
                    pt = Type.GetType(message.PayloadType);

                    if (value == null)
                    {
                        pi.SetValue(message, null);

                        break;
                    }

                    if (pt!.IsValueType || pt == typeof(string))
                    {
                        payload = value;
                    }
                    else
                    {
                        payload = JsonSerializer.Deserialize(value, pt, options.JsonSerializerOptions);
                    } 
                    
                    pi.SetValue(message, payload);

                    break;
                case nameof(Message.Referer):
                    if (message.RefererType == null)
                    {
                        var typeString = source.FindValue(nameof(Message.RefererType))?.ToString();

                        if (string.IsNullOrWhiteSpace(typeString)) break;

                        message.RefererType = typeString;
                    }

                    if (message.RefererType == null) break;
                    
                    pt = Type.GetType(message.RefererType);

                    if (value == null)
                    {
                        pi.SetValue(message, null);

                        break;
                    }
                    
                    if (pt!.IsValueType || pt == typeof(string))
                    {
                        payload = value;
                    }
                    else
                    {
                        payload = JsonSerializer.Deserialize(value, pt, options.JsonSerializerOptions);
                    }

                    pi.SetValue(message, payload);

                    break;
                case nameof(Message.CreationDate):
                    if (value == null)
                    {
                        pi.SetValue(message, null);
                    }
                    else
                    {
                        pi.SetValue(message, DateTimeOffset.Parse(value));    
                    }
                    
                    break;
                default:
                    pi.SetValue(message, value);

                    break;
            }
        }

        return message;
    }

    public static object? FindValue(this StreamsEntry source, string name)
    {
        for (var i = 0; i < source.fieldValues.Length; i += 2)
        {
            if (!source.fieldValues[i].ToString()!.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            return source.fieldValues[i + 1];
        }

        return null;
    }
}