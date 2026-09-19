using System.Buffers;
using MessagePack;

namespace Sirius.MasterTool.MasterMemory;

internal static class MasterMemoryBinary
{
    internal sealed record TableBlock(string Name, int Offset, int Size);

    internal sealed class Layout
    {
        public required int PayloadOffset { get; init; }
        public required IReadOnlyList<TableBlock> Tables { get; init; }

        public TableBlock GetTable(string name) =>
            Tables.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"MasterMemory table not found: {name}");

        public ReadOnlyMemory<byte> GetSegment(byte[] database, string name)
        {
            var table = GetTable(name);
            return database.AsMemory(checked(PayloadOffset + table.Offset), table.Size);
        }
    }

    public static Layout Parse(byte[] database)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(database));
        var count = reader.ReadMapHeader();
        var tables = new List<TableBlock>(count);

        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString()
                ?? throw new InvalidDataException("Null MasterMemory table name.");
            if (reader.ReadArrayHeader() != 2)
                throw new InvalidDataException($"Invalid MasterMemory header tuple: {name}");
            var offset = reader.ReadInt32();
            var size = reader.ReadInt32();
            if (offset < 0 || size < 0)
                throw new InvalidDataException($"Invalid MasterMemory table span: {name}");
            tables.Add(new TableBlock(name, offset, size));
        }

        var payloadOffset = checked((int)reader.Consumed);
        var payloadLength = database.Length - payloadOffset;
        foreach (var table in tables)
        {
            if ((long)table.Offset + table.Size > payloadLength)
                throw new InvalidDataException($"Table span exceeds payload: {table.Name}");
        }

        return new Layout { PayloadOffset = payloadOffset, Tables = tables };
    }

    public static byte[] Rebuild(
        byte[] originalDatabase,
        Layout originalLayout,
        IReadOnlyDictionary<string, byte[]> replacements)
    {
        var segments = new List<(string Name, byte[] Bytes)>(originalLayout.Tables.Count);
        foreach (var table in originalLayout.Tables)
        {
            var bytes = replacements.TryGetValue(table.Name, out var replacement)
                ? replacement
                : originalLayout.GetSegment(originalDatabase, table.Name).ToArray();
            segments.Add((table.Name, bytes));
        }

        foreach (var name in replacements.Keys)
            _ = originalLayout.GetTable(name);

        var headerBuffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(headerBuffer);
        writer.WriteMapHeader(segments.Count);
        var offset = 0;
        foreach (var (name, bytes) in segments)
        {
            writer.Write(name);
            writer.WriteArrayHeader(2);
            writer.Write(offset);
            writer.Write(bytes.Length);
            offset = checked(offset + bytes.Length);
        }
        writer.Flush();

        var result = new byte[checked(headerBuffer.WrittenCount + offset)];
        headerBuffer.WrittenSpan.CopyTo(result);
        var cursor = headerBuffer.WrittenCount;
        foreach (var (_, bytes) in segments)
        {
            bytes.CopyTo(result, cursor);
            cursor += bytes.Length;
        }
        return result;
    }
}
