namespace Sirius.AssetTool.Charts.Sus;

public sealed record SusObject(
    int Measure,
    int Channel,
    int Lane,
    string Group,
    int Type,
    int Width,
    decimal Fraction,
    int SourceLine)
{
    public decimal Beat { get; set; }
}

public sealed record SusHiSpeedEvent(
    int Measure,
    int Tick,
    decimal Value,
    int SourceLine)
{
    public decimal Beat { get; set; }
}

public sealed record SusSplitEvent(
    int Measure,
    int Tick,
    int LineDelta,
    int Color,
    int SourceLine)
{
    public decimal Beat { get; set; }
}

public sealed class SusChart
{
    public int TicksPerBeat { get; set; } = 480;
    public decimal WaveOffsetSeconds { get; set; }
    public Dictionary<int, decimal> MeasureLengths { get; } = new();
    public Dictionary<string, decimal> BpmDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(int Measure, decimal Beat, string Id, int SourceLine)> BpmReferences { get; } = [];
    public List<SusObject> Objects { get; } = [];
    public List<SusHiSpeedEvent> HiSpeedEvents { get; } = [];
    public List<SusSplitEvent> SplitEvents { get; } = [];
    public List<string> Warnings { get; } = [];
}
