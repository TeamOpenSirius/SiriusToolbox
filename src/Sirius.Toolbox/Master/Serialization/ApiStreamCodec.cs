using System.Buffers;
using MessagePack;

namespace Sirius.Toolbox.Master.Serialization;

public static class ApiStreamCodec
{
    public static byte[] EncodeRequest<T>(T payload) => MasterMessagePack.Serialize(payload);

    public static T DecodePayload<T>(ReadOnlyMemory<byte> response)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(response));
        SkipOne(ref reader, "common");
        var payload = ReadOne(ref reader, "payload");

        try
        {
            return MessagePackSerializer.Deserialize<T>(payload, MasterMessagePack.StandardOptions);
        }
        catch (Exception standardError)
        {
            try
            {
                return MessagePackSerializer.Deserialize<T>(payload, MasterMessagePack.Lz4BlockArrayOptions);
            }
            catch (Exception lz4Error)
            {
                throw new InvalidDataException(
                    $"Failed to decode API payload as {typeof(T).FullName} with standard and LZ4 MessagePack.",
                    new AggregateException(standardError, lz4Error));
            }
        }
    }

    private static void SkipOne(ref MessagePackReader reader, string name)
        => _ = ReadOne(ref reader, name);

    private static ReadOnlySequence<byte> ReadOne(ref MessagePackReader reader, string name)
    {
        if (reader.End) throw new InvalidDataException($"API response ended before {name}.");
        return reader.ReadRaw();
    }
}
