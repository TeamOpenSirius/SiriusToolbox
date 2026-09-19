using Sirius.AssetTool.Charts.Sirius;
using Sirius.AssetTool.Charts.Sus;

namespace Sirius.AssetTool.Charts.Conversion;

public sealed class ConversionOptions
{
    public bool ApplyWaveOffset { get; init; } = true;
    public bool Strict { get; init; }
}

public sealed class ConversionResult
{
    public required IReadOnlyList<SiriusNote> Notes { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public static class SusToSiriusConverter
{
    private const int LaneCount = 12;

    public static ConversionResult Convert(SusChart chart, ConversionOptions options)
    {
        var tempo = new TempoMap(chart);
        var warnings = new List<string>(chart.Warnings);
        if (options.Strict && warnings.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, warnings));

        var notes = new List<SiriusNote>();
        AddTimingEvents(chart, tempo, options, notes);

        var noteList = BuildNoteList(chart, tempo);
        ParseSlides(chart, tempo, options, noteList, notes);
        AddRemainingNotes(chart, tempo, options, noteList, notes);

        var ordered = notes
            .Select((note, order) => (note, order))
            .OrderBy(x => x.note.StartTime)
            .ThenBy(x => x.order)
            .Select(x => x.note)
            .ToList();

        return new ConversionResult
        {
            Notes = ordered,
            Warnings = warnings.Distinct().ToList(),
        };
    }

    private static Dictionary<(int Left, int Right), SortedDictionary<decimal, List<NoteEvent>>> BuildNoteList(
        SusChart chart,
        TempoMap tempo)
    {
        var result = new Dictionary<(int Left, int Right), SortedDictionary<decimal, List<NoteEvent>>>();

        foreach (var item in chart.Objects
                     .OrderBy(x => x.Beat)
                     .ThenBy(x => x.Channel)
                     .ThenBy(x => x.Lane)
                     .ThenBy(x => x.Group, StringComparer.Ordinal))
        {
            // The reference converter only consumes channels 1, 3, and 5.
            if (item.Channel is 2 or 4)
                continue;
            if (item.Channel is not (1 or 3 or 5))
                continue;

            ValidateType(item);

            var left = item.Lane - 1;
            var right = left + item.Width - 1;
            if (left is < 1 or > LaneCount || right is < 1 or > LaneCount)
                throw new InvalidDataException($"line {item.SourceLine}: invalid lane range [{left}, {right}].");

            // The reference implementation explicitly discards tap type 3.
            if (item is { Channel: 1, Type: 3 })
                continue;

            if (!result.TryGetValue((left, right), out var timeMap))
            {
                timeMap = new SortedDictionary<decimal, List<NoteEvent>>();
                result[(left, right)] = timeMap;
            }
            if (!timeMap.TryGetValue(item.Beat, out var events))
            {
                events = [];
                timeMap[item.Beat] = events;
            }

            events.Add(new NoteEvent(item, tempo.GetBpm(item.Beat)));
        }

        return result;
    }

    private static void ValidateType(SusObject item)
    {
        var valid = item.Channel switch
        {
            1 => item.Type is 1 or 2 or 3 or 4,
            3 => item.Type is 1 or 2 or 3,
            5 => item.Type is 1 or 3 or 4,
            _ => true,
        };
        if (!valid)
            throw new InvalidDataException($"line {item.SourceLine}: invalid SUS type {item.Type} on channel {item.Channel}.");
        if (item is { Channel: 3, Group.Length: 0 })
            throw new InvalidDataException($"line {item.SourceLine}: slide event has no group id.");
    }

    private static void AddTimingEvents(
        SusChart chart,
        TempoMap tempo,
        ConversionOptions options,
        List<SiriusNote> notes)
    {
        foreach (var speed in chart.HiSpeedEvents.OrderBy(x => x.Beat))
        {
            notes.Add(new SiriusNote(
                TimeOf(speed.Beat, tempo, chart, options),
                (double)speed.Value,
                SiriusNoteType.HiSpeed,
                -1,
                0));
        }

        SusSplitEvent? active = null;
        foreach (var split in chart.SplitEvents.OrderBy(x => x.Beat).ThenBy(x => x.SourceLine))
        {
            if (active is null)
            {
                if (split.LineDelta is < 1 or > 6)
                    throw new InvalidDataException($"line {split.SourceLine}: invalid split-line start {split.LineDelta}.");
                active = split;
                continue;
            }

            if (split.LineDelta != -active.LineDelta || split.Color != active.Color)
                throw new InvalidDataException($"line {split.SourceLine}: overlapping or mismatched split-line event.");

            notes.Add(new SiriusNote(
                TimeOf(active.Beat, tempo, chart, options),
                TimeOf(split.Beat, tempo, chart, options),
                SiriusNoteType.None,
                -1,
                0,
                (SiriusGimmickType)(10 + active.LineDelta),
                active.Color));
            active = null;
        }

        if (active is not null)
            throw new InvalidDataException($"line {active.SourceLine}: split-line event has no matching end.");
    }

    private static void ParseSlides(
        SusChart chart,
        TempoMap tempo,
        ConversionOptions options,
        Dictionary<(int Left, int Right), SortedDictionary<decimal, List<NoteEvent>>> noteList,
        List<SiriusNote> notes)
    {
        for (var left = 1; left <= LaneCount; left++)
        {
            for (var right = left; right <= LaneCount; right++)
            {
                if (!noteList.TryGetValue((left, right), out var timeMap))
                    continue;

                var slideCount = 0;
                var slides = new Dictionary<char, SlideState>();

                foreach (var pair in timeMap)
                {
                    var beat = pair.Key;
                    var info = pair.Value;
                    var time = TimeOf(beat, tempo, chart, options);
                    var width = right - left + 1;
                    var isCritical = info.Any(x => x.Source is { Channel: 1, Type: 2 });
                    var isDamage = info.Any(x => x.Source is { Channel: 1, Type: 4 });
                    var isFlick = info.Any(x => x.Source.Channel == 5);
                    var isSlideSound = info.Any(x => x.Source is { Channel: 3, Type: 3 });
                    var starts = info
                        .Where(x => x.Source is { Channel: 3, Type: 1 })
                        .Select(x => x.Group)
                        .ToList();
                    var ends = info
                        .Where(x => x.Source.Channel == 3 && x.Source.Type == 2)
                        .Select(x => x.Group)
                        .ToList();
                    var bpmAtSlide = info.LastOrDefault(x => x.Source.Channel == 3)?.Bpm ?? 120m;

                    if (starts.Count == 0 && ends.Count == 0 && !isSlideSound && !isFlick)
                        continue;

                    if (isSlideSound)
                    {
                        if (slideCount == 0)
                            throw new InvalidDataException($"unknown slide sound in lane range [{left}, {right}] at {time:G10}.");
                        notes.Add(Point(time, SiriusNoteType.Sound, left, width));
                    }

                    foreach (var group in ends)
                    {
                        if (!slides.TryGetValue(group, out var state) || !state.InSlide || slideCount == 0)
                            throw new InvalidDataException($"unknown slide end '{group}' in lane range [{left}, {right}] at {time:G10}.");

                        var scratchType = 0;
                        var shouldUnscratch = true;

                        if (isDamage)
                            ConsumeFirst(info, x => x.Source.Channel == 1 && x.Source.Type == 4);

                        for (var i = 1; i <= LaneCount; i++)
                        {
                            if (i > left && i <= right)
                                continue;

                            var events = i <= left
                                ? GetEvents(noteList, i, right, beat)
                                : GetEvents(noteList, left, i, beat);
                            foreach (var item in events)
                            {
                                if (item.Source.Channel != 5)
                                    continue;

                                state.ScratchSlide = true;
                                scratchType = i == left
                                    ? item.Source.Type switch
                                    {
                                        3 => -width,
                                        4 => width,
                                        _ => 0,
                                    }
                                    : i < left
                                        ? i - right - 1
                                        : i - left + 1;
                                item.Consumed = true;
                                shouldUnscratch = false;
                            }
                        }

                        if (shouldUnscratch && state.ScratchSlide)
                        {
                            state.ScratchSlide = false;
                            if (!state.IsCausedByDamage)
                                state.AddStart = true;
                        }

                        var startType = state.CriticalSlide
                            ? (state.ScratchSlide ? SiriusNoteType.ScratchCriticalHoldStart : SiriusNoteType.CriticalHoldStart)
                            : (state.ScratchSlide ? SiriusNoteType.ScratchHoldStart : SiriusNoteType.HoldStart);
                        var holdType = state.CriticalSlide
                            ? (state.ScratchSlide ? SiriusNoteType.ScratchCriticalHold : SiriusNoteType.CriticalHold)
                            : (state.ScratchSlide ? SiriusNoteType.ScratchHold : SiriusNoteType.Hold);
                        if (isDamage)
                            holdType = (SiriusNoteType)((int)holdType + 1000);

                        if (state.AddStart)
                            notes.Add(Point(state.StartTime, startType, left, width));

                        notes.Add(new SiriusNote(
                            state.StartTime,
                            time,
                            holdType,
                            left,
                            width,
                            scratchType == 0 ? SiriusGimmickType.None : SiriusGimmickType.JumpScratch,
                            scratchType));

                        var bpm = (int)bpmAtSlide;
                        if (bpm <= 0)
                            throw new InvalidDataException($"invalid BPM {bpmAtSlide} at slide end.");
                        var tickTime = chart.TicksPerBeat / 3840.0 * 60.0 * 4.0 / bpm;
                        for (var tick = state.StartTime + tickTime; tick <= time - 0.0001; tick += tickTime)
                            notes.Add(Point(tick, SiriusNoteType.HoldEighth, left, width));

                        state.InSlide = false;
                        slideCount--;
                    }

                    if (isFlick && slideCount > 0)
                    {
                        var direction = 0;
                        foreach (var item in info.Where(x => x.Source.Channel == 5))
                        {
                            item.Consumed = true;
                            direction = item.Source.Type switch
                            {
                                3 => -width,
                                4 => width,
                                _ => 0,
                            };
                        }
                        notes.Add(new SiriusNote(
                            time,
                            -1,
                            SiriusNoteType.SoundPurple,
                            left,
                            width,
                            SiriusGimmickType.None,
                            direction));
                    }

                    foreach (var group in starts)
                    {
                        if (slides.TryGetValue(group, out var existing) && existing.InSlide)
                            throw new InvalidDataException($"overlapped slide '{group}' in lane range [{left}, {right}] at {time:G10}.");

                        var state = new SlideState
                        {
                            InSlide = true,
                            CriticalSlide = isCritical,
                            AddStart = !isDamage,
                            IsCausedByDamage = isDamage,
                            StartTime = time,
                        };

                        if (isCritical)
                            ConsumeFirst(info, x => x.Source is { Channel: 1, Type: 2 });
                        if (isDamage)
                            ConsumeFirst(info, x => x.Source is { Channel: 1, Type: 4 });

                        for (var i = 1; i <= LaneCount; i++)
                        {
                            if (i > left && i < right)
                                continue;

                            var events = i <= left
                                ? GetEvents(noteList, i, right, beat)
                                : GetEvents(noteList, left, i, beat);
                            if (events.All(x => x.Source.Channel != 5))
                                continue;
                            state.ScratchSlide = true;
                            state.AddStart = false;
                        }

                        slides[group] = state;
                        slideCount++;
                    }
                }
            }
        }
    }

    private static void AddRemainingNotes(
        SusChart chart,
        TempoMap tempo,
        ConversionOptions options,
        Dictionary<(int Left, int Right), SortedDictionary<decimal, List<NoteEvent>>> noteList,
        List<SiriusNote> notes)
    {
        for (var left = 1; left <= LaneCount; left++)
        {
            for (var right = left; right <= LaneCount; right++)
            {
                if (!noteList.TryGetValue((left, right), out var timeMap))
                    continue;

                var width = right - left + 1;
                foreach (var pair in timeMap)
                {
                    var time = TimeOf(pair.Key, tempo, chart, options);
                    foreach (var item in pair.Value)
                    {
                        if (item.Consumed)
                            continue;

                        if (item.Source.Channel == 1)
                        {
                            if (item.Source.Type == 1)
                                notes.Add(Point(time, SiriusNoteType.Normal, left, width));
                            else if (item.Source.Type == 2)
                                notes.Add(Point(time, SiriusNoteType.Critical, left, width));
                        }
                        else if (item.Source.Channel == 5)
                        {
                            var direction = item.Source.Type switch
                            {
                                3 => -width,
                                4 => width,
                                _ => 0,
                            };
                            notes.Add(new SiriusNote(
                                time,
                                -1,
                                SiriusNoteType.Flick,
                                left,
                                width,
                                SiriusGimmickType.None,
                                direction));
                        }
                    }
                }
            }
        }
    }

    private static void ConsumeFirst(IEnumerable<NoteEvent> events, Func<NoteEvent, bool> predicate)
    {
        var item = events.FirstOrDefault(predicate);
        if (item is not null)
            item.Consumed = true;
    }

    private static IReadOnlyList<NoteEvent> GetEvents(
        Dictionary<(int Left, int Right), SortedDictionary<decimal, List<NoteEvent>>> noteList,
        int left,
        int right,
        decimal beat)
    {
        return noteList.TryGetValue((left, right), out var timeMap) && timeMap.TryGetValue(beat, out var events)
            ? events
            : Array.Empty<NoteEvent>();
    }

    private static SiriusNote Point(double time, SiriusNoteType type, int lane, int width) =>
        new(time, -1, type, lane, width);

    private static double TimeOf(decimal beat, TempoMap tempo, SusChart chart, ConversionOptions options)
    {
        var result = tempo.GetSeconds(beat);
        if (options.ApplyWaveOffset)
            result += chart.WaveOffsetSeconds;
        return (double)result;
    }

    private sealed class NoteEvent(SusObject source, decimal bpm)
    {
        public SusObject Source { get; } = source;
        public decimal Bpm { get; } = bpm;
        public bool Consumed { get; set; }
        public char Group => Source.Group.Length == 0 ? '\0' : Source.Group[0];
    }

    private sealed class SlideState
    {
        public bool InSlide { get; set; }
        public bool CriticalSlide { get; set; }
        public bool ScratchSlide { get; set; }
        public bool AddStart { get; set; } = true;
        public bool IsCausedByDamage { get; set; }
        public double StartTime { get; set; }
    }
}
