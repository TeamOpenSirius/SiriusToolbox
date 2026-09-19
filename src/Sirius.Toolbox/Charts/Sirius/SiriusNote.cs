using System.Globalization;

namespace Sirius.AssetTool.Charts.Sirius;

public enum SiriusNoteType
{
    HiSpeed = -1,
    None = 0,
    Normal = 10,
    Critical = 20,
    Sound = 30,
    ScratchSound = 31,
    SoundPurple = 40,
    Flick = 50,
    HoldStart = 80,
    CriticalHoldStart = 81,
    ScratchHoldStart = 82,
    ScratchCriticalHoldStart = 83,
    Hold = 100,
    CriticalHold = 101,
    ScratchHold = 110,
    ScratchCriticalHold = 111,
    BlueTap = 200,
    HoldEighth = 900,
    NontailHold = 1100,
    NontailCriticalHold = 1101,
    NontailScratchHold = 1110,
    NontailScratchCriticalHold = 1111,
}

public enum SiriusGimmickType
{
    None = 0,
    JumpScratch = 1,
    OneDirection = 2,
    Split1 = 11,
    Split2 = 12,
    Split3 = 13,
    Split4 = 14,
    Split5 = 15,
    Split6 = 16,
    FullSplit1 = 31,
    FullSplit2 = 32,
    FullSplit3 = 33,
    FullSplit4 = 34,
    FullSplit5 = 35,
    FullSplit6 = 36,
    LightSplit1 = 51,
    LightSplit2 = 52,
    LightSplit3 = 53,
    LightSplit4 = 54,
    LightSplit5 = 55,
    LightSplit6 = 56,
    IgnoreSplit1 = 71,
    IgnoreSplit2 = 72,
    IgnoreSplit3 = 73,
    IgnoreSplit4 = 74,
    IgnoreSplit5 = 75,
    IgnoreSplit6 = 76,
}

public sealed record SiriusNote(
    double StartTime,
    double EndTime,
    SiriusNoteType Type,
    int LeftLane,
    int LaneLength,
    SiriusGimmickType GimmickType = SiriusGimmickType.None,
    int GimmickValue = 0)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public string ToChartLine() => string.Join(",",
        Format(StartTime),
        Format(EndTime),
        ((int)Type).ToString(Invariant),
        LeftLane.ToString(Invariant),
        LaneLength.ToString(Invariant),
        FormatGimmickType(GimmickType),
        GimmickValue.ToString(Invariant));

    private static string Format(double value) => value.ToString("G10", Invariant);

    private static string FormatGimmickType(SiriusGimmickType type) => type switch
    {
        SiriusGimmickType.JumpScratch => nameof(SiriusGimmickType.JumpScratch),
        SiriusGimmickType.OneDirection => nameof(SiriusGimmickType.OneDirection),
        _ => ((int)type).ToString(Invariant),
    };
}
