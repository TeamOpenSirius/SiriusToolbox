namespace Sirius.Toolbox.Episodes;

internal static class BinaryDiagnostics
{
    private static readonly byte[] Utf8Replacement = [0xEF, 0xBF, 0xBD];

    public static bool IsProbablyUtf8TextCorrupted(ReadOnlySpan<byte> data)
    {
        var total = CountUtf8ReplacementSequences(data);
        var prefixLength = Math.Min(data.Length, 64);
        var prefix = CountUtf8ReplacementSequences(data[..prefixLength]);
        return total >= 4 && prefix >= 2;
    }

    public static int CountUtf8ReplacementSequences(ReadOnlySpan<byte> data)
    {
        var count = 0;
        var offset = 0;
        while (offset <= data.Length - Utf8Replacement.Length)
        {
            var found = data[offset..].IndexOf(Utf8Replacement);
            if (found < 0)
            {
                break;
            }

            count++;
            offset += found + Utf8Replacement.Length;
        }

        return count;
    }
}
