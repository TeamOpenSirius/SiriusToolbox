using MessagePack;

namespace Sirius.Toolbox.Master.Serialization;

internal static class MasterMessagePack
{
    public static MessagePackSerializerOptions StandardOptions { get; } =
        MessagePackSerializerOptions.Standard;

    public static MessagePackSerializerOptions Lz4BlockOptions { get; } =
        StandardOptions.WithCompression(MessagePackCompression.Lz4Block);

    public static MessagePackSerializerOptions Lz4BlockArrayOptions { get; } =
        StandardOptions.WithCompression(MessagePackCompression.Lz4BlockArray);

    public static byte[] Serialize<T>(T value) =>
        MessagePackSerializer.Serialize(value, StandardOptions);
}
