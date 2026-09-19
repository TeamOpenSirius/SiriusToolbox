using System.Text;

namespace Sirius.MasterData;

internal static class Csv
{
    public static void WriteRow(TextWriter writer, params string[] values) =>
        writer.WriteLine(string.Join(',', values.Select(Escape)));

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    public static IEnumerable<string[]> ReadAll(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        while (true)
        {
            var ci = reader.Read();
            if (ci < 0)
            {
                if (quoted) throw new InvalidDataException("Unterminated CSV quote.");
                if (field.Length > 0 || row.Count > 0)
                {
                    row.Add(field.ToString());
                    yield return row.ToArray();
                }
                yield break;
            }

            var c = (char)ci;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return row.ToArray();
                row.Clear();
            }
            else
            {
                field.Append(c);
            }
        }
    }
}
