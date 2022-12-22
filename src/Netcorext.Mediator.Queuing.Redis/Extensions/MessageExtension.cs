using FreeRedis;

namespace Netcorext.Mediator.Queuing.Redis.Extensions;

internal static class MessageExtension
{
    public static IEnumerable<StreamData> ToStreamData(this StreamsEntryResult entry)
    {
        return entry.entries.Select(t => t.ToStreamData());
    }

    public static StreamData ToStreamData(this StreamsEntry entry)
    {
        var streamData = new StreamData
                         {
                             StreamId = entry.id
                         };

        for (var i = 0; i < entry.fieldValues.Length; i += 2)
        {
            var key = entry.fieldValues[i];
            var value = entry.fieldValues[i + 1];

            switch (key)
            {
                case nameof(StreamData.Key):
                    streamData.Key = (string)value;

                    break;
                case nameof(StreamData.Timestamp):
                    if (!long.TryParse(value.ToString(), out var unixTimeMilliseconds)) break;

                    streamData.Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds);

                    break;
                case nameof(StreamData.Data):
                    streamData.Data = (string)value;

                    break;
            }
        }

        return streamData;
    }
}