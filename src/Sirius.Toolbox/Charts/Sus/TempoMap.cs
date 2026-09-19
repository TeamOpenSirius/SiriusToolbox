namespace Sirius.AssetTool.Charts.Sus;

public sealed class TempoMap
{
    private readonly List<TempoPoint> _points;

    public TempoMap(SusChart chart)
    {
        var refs = new List<(decimal Beat, decimal Bpm, int Order)> { (0m, 120m, -1) };
        for (var index = 0; index < chart.BpmReferences.Count; index++)
        {
            var reference = chart.BpmReferences[index];
            if (chart.BpmDefinitions.TryGetValue(reference.Id, out var bpm))
                refs.Add((reference.Beat, bpm, index));
            else
                chart.Warnings.Add($"line {reference.SourceLine}: undefined BPM id '{reference.Id}'.");
        }

        var normalized = refs
            .OrderBy(x => x.Beat)
            .ThenBy(x => x.Order)
            .GroupBy(x => x.Beat)
            .Select(x => x.Last())
            .ToList();

        if (normalized[0].Beat < 0)
            throw new InvalidDataException("Negative BPM positions are not supported.");

        _points = [];
        decimal seconds = 0;
        for (var index = 0; index < normalized.Count; index++)
        {
            var current = normalized[index];
            if (index > 0)
            {
                var previous = normalized[index - 1];
                seconds += (current.Beat - previous.Beat) * 60m / previous.Bpm;
            }
            _points.Add(new TempoPoint(current.Beat, current.Bpm, seconds));
        }
    }

    public decimal GetSeconds(decimal beat)
    {
        var point = FindPoint(beat);
        return point.Seconds + (beat - point.Beat) * 60m / point.Bpm;
    }

    public decimal GetBpm(decimal beat) => FindPoint(beat).Bpm;

    private TempoPoint FindPoint(decimal beat)
    {
        var low = 0;
        var high = _points.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_points[middle].Beat <= beat)
                low = middle;
            else
                high = middle - 1;
        }
        return _points[low];
    }

    private sealed record TempoPoint(decimal Beat, decimal Bpm, decimal Seconds);
}
