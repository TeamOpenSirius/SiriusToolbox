using System.Globalization;
using System.Text.RegularExpressions;

namespace Sirius.Toolbox.Charts.Sus;

public static partial class SusParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const decimal SusTicksPerMeasure = 1920m;

    public static SusChart Parse(string path)
    {
        var chart = new SusChart();
        var lines = File.ReadAllLines(path);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var sourceLine = lineIndex + 1;
            var line = lines[lineIndex].Trim().TrimStart('\uFEFF');
            if (!line.StartsWith('#'))
                continue;

            if (TryParseScalar(line, "#WAVEOFFSET", out var offset))
            {
                chart.WaveOffsetSeconds = offset;
                continue;
            }

            var request = RequestRegex().Match(line);
            if (request.Success && int.TryParse(request.Groups[1].Value, NumberStyles.Integer, Invariant, out var ticks))
            {
                chart.TicksPerBeat = ticks;
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;

            var key = line[1..colon].Trim();
            var value = StripComment(line[(colon + 1)..]).Trim();

            if (key.Equals("TIL00", StringComparison.OrdinalIgnoreCase))
            {
                ParseHiSpeedList(value, sourceLine, chart);
                continue;
            }

            if (key.Equals("TIL01", StringComparison.OrdinalIgnoreCase))
            {
                ParseSplitList(value, sourceLine, chart);
                continue;
            }

            if (key.StartsWith("BPM", StringComparison.OrdinalIgnoreCase) && key.Length > 3)
            {
                if (decimal.TryParse(value, NumberStyles.Float, Invariant, out var bpm) && bpm > 0)
                    chart.BpmDefinitions[key[3..]] = bpm;
                else
                    chart.Warnings.Add($"line {sourceLine}: invalid BPM definition '{line}'.");
                continue;
            }

            if (key.Length < 5 || !int.TryParse(key[..3], NumberStyles.Integer, Invariant, out var measure))
                continue;

            var channelText = key[3..5];
            if (channelText.Equals("02", StringComparison.OrdinalIgnoreCase))
            {
                if (decimal.TryParse(value, NumberStyles.Float, Invariant, out var beats) && beats > 0)
                    chart.MeasureLengths[measure] = beats;
                else
                    chart.Warnings.Add($"line {sourceLine}: invalid measure length '{line}'.");
                continue;
            }

            if (channelText.Equals("08", StringComparison.OrdinalIgnoreCase))
            {
                ParsePairs(value, sourceLine, chart.Warnings, (index, count, _, _, raw) =>
                {
                    if (raw == "00")
                        return;
                    chart.BpmReferences.Add((measure, (decimal)index / count, raw, sourceLine));
                });
                continue;
            }

            var channel = FromBase36(key[3]);
            if (channel is not (1 or 2 or 3 or 4 or 5))
                continue;

            var lane = FromBase36(key[4]);
            if (lane < 0)
            {
                chart.Warnings.Add($"line {sourceLine}: invalid lane in '{key}'.");
                continue;
            }

            var group = key.Length >= 6 ? key[5..].ToUpperInvariant() : string.Empty;
            if (channel == 3 && group.Length == 0)
            {
                chart.Warnings.Add($"line {sourceLine}: long-note channel has no group id.");
                continue;
            }

            ParsePairs(value, sourceLine, chart.Warnings, (index, count, type, width, raw) =>
            {
                if (raw == "00")
                    return;
                chart.Objects.Add(new SusObject(
                    measure,
                    channel,
                    lane,
                    group,
                    type,
                    width,
                    (decimal)index / count,
                    sourceLine));
            });
        }

        AssignAbsoluteBeats(chart);
        return chart;
    }

    private static void ParseHiSpeedList(string value, int sourceLine, SusChart chart)
    {
        foreach (var entry in ParseQuotedList(value))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0 || separator == entry.Length - 1)
                throw new InvalidDataException($"line {sourceLine}: invalid HiSpeed parameter '{entry}'.");

            var (measure, tick) = ParsePosition(entry[..separator], sourceLine, "HiSpeed");
            if (!decimal.TryParse(entry[(separator + 1)..], NumberStyles.Float, Invariant, out var speed))
                throw new InvalidDataException($"line {sourceLine}: invalid HiSpeed value '{entry}'.");

            chart.HiSpeedEvents.Add(new SusHiSpeedEvent(measure, tick, speed, sourceLine));
        }
    }

    private static void ParseSplitList(string value, int sourceLine, SusChart chart)
    {
        foreach (var entry in ParseQuotedList(value))
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0 || separator == entry.Length - 1)
                throw new InvalidDataException($"line {sourceLine}: invalid split-line parameter '{entry}'.");

            var (measure, tick) = ParsePosition(entry[..separator], sourceLine, "split-line");
            var type = entry[(separator + 1)..];
            var dot = type.IndexOf('.');
            if (dot <= 0 || dot == type.Length - 1 ||
                !int.TryParse(type[..dot], NumberStyles.Integer, Invariant, out var lineDelta) ||
                !int.TryParse(type[(dot + 1)..], NumberStyles.Integer, Invariant, out var color))
            {
                throw new InvalidDataException($"line {sourceLine}: invalid split-line value '{entry}'.");
            }

            chart.SplitEvents.Add(new SusSplitEvent(measure, tick, lineDelta, color, sourceLine));
        }
    }

    private static IEnumerable<string> ParseQuotedList(string value)
    {
        value = value.Trim();
        if (value is ['"', _, ..] && value[^1] == '"')
            value = value[1..^1];
        if (value.Length == 0)
            yield break;

        foreach (var entry in value.Split(','))
        {
            var trimmed = entry.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private static (int Measure, int Tick) ParsePosition(string text, int sourceLine, string kind)
    {
        var quote = text.IndexOf('\'');
        if (quote <= 0 || quote == text.Length - 1 ||
            !int.TryParse(text[..quote], NumberStyles.Integer, Invariant, out var measure) ||
            !int.TryParse(text[(quote + 1)..], NumberStyles.Integer, Invariant, out var tick))
        {
            throw new InvalidDataException($"line {sourceLine}: invalid {kind} position '{text}'.");
        }

        return (measure, tick);
    }

    private static void AssignAbsoluteBeats(SusChart chart)
    {
        var maxMeasure = 0;
        if (chart.Objects.Count > 0)
            maxMeasure = Math.Max(maxMeasure, chart.Objects.Max(x => x.Measure));
        if (chart.BpmReferences.Count > 0)
            maxMeasure = Math.Max(maxMeasure, chart.BpmReferences.Max(x => x.Measure));
        if (chart.MeasureLengths.Count > 0)
            maxMeasure = Math.Max(maxMeasure, chart.MeasureLengths.Keys.Max());
        if (chart.HiSpeedEvents.Count > 0)
            maxMeasure = Math.Max(maxMeasure, chart.HiSpeedEvents.Max(x => x.Measure));
        if (chart.SplitEvents.Count > 0)
            maxMeasure = Math.Max(maxMeasure, chart.SplitEvents.Max(x => x.Measure));

        var starts = new decimal[maxMeasure + 2];
        decimal currentLength = 2;
        for (var measure = 0; measure <= maxMeasure; measure++)
        {
            if (chart.MeasureLengths.TryGetValue(measure, out var specified))
                currentLength = specified;
            starts[measure + 1] = starts[measure] + currentLength;
        }

        decimal PositionToBeat(int measure, decimal fraction)
        {
            var length = starts[measure + 1] - starts[measure];
            return starts[measure] + length * fraction;
        }

        foreach (var item in chart.Objects)
            item.Beat = PositionToBeat(item.Measure, item.Fraction);

        for (var i = 0; i < chart.BpmReferences.Count; i++)
        {
            var item = chart.BpmReferences[i];
            chart.BpmReferences[i] = (item.Measure, PositionToBeat(item.Measure, item.Beat), item.Id, item.SourceLine);
        }

        foreach (var item in chart.HiSpeedEvents)
            item.Beat = PositionToBeat(item.Measure, item.Tick / SusTicksPerMeasure);

        foreach (var item in chart.SplitEvents)
            item.Beat = PositionToBeat(item.Measure, item.Tick / SusTicksPerMeasure);
    }

    private static void ParsePairs(
        string data,
        int sourceLine,
        List<string> warnings,
        Action<int, int, int, int, string> callback)
    {
        data = string.Concat(data.Where(c => !char.IsWhiteSpace(c)));
        if (data.Length == 0)
            return;
        if ((data.Length & 1) != 0)
        {
            warnings.Add($"line {sourceLine}: note data has an odd number of characters.");
            return;
        }

        var count = data.Length / 2;
        for (var index = 0; index < count; index++)
        {
            var raw = data.Substring(index * 2, 2);
            var type = FromBase36(raw[0]);
            var width = FromBase36(raw[1]);
            if (type < 0 || width < 0)
            {
                warnings.Add($"line {sourceLine}: invalid base-36 note token '{raw}'.");
                continue;
            }
            callback(index, count, type, width, raw);
        }
    }

    private static string StripComment(string value)
    {
        var index = value.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? value[..index] : value;
    }

    private static bool TryParseScalar(string line, string name, out decimal value)
    {
        value = 0;
        if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return false;
        var text = line[name.Length..].Trim().Trim('"');
        return decimal.TryParse(text, NumberStyles.Float, Invariant, out value);
    }

    public static int FromBase36(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c is >= 'A' and <= 'Z')
            return c - 'A' + 10;
        return -1;
    }

    [GeneratedRegex("^#REQUEST\\s+\"ticks_per_beat\\s+(\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestRegex();
}
