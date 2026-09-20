using MessagePack;
using MessagePack.Resolvers;

namespace Sirius.Toolbox.Episodes.Protocol
{
    /// <summary>
    /// MessagePack settings used by Sirius episode resources.
    /// Centralized in Sirius.Toolbox so protocol contracts stay independent from the UI project.
    /// </summary>
    internal static class EpisodeMessagePack
    {
        private static readonly MessagePackSerializerOptions StandardOptions = MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                NativeDateTimeResolver.Instance,
                StandardResolver.Instance));

        private static readonly MessagePackSerializerOptions Lz4BlockArrayOptions = StandardOptions
            .WithCompression(MessagePackCompression.Lz4BlockArray);

        public static byte[] Serialize<T>(T value, bool lz4 = false)
            => MessagePackSerializer.Serialize(value, lz4 ? Lz4BlockArrayOptions : StandardOptions);

        public static T Deserialize<T>(byte[] bytes, bool lz4 = false)
            => MessagePackSerializer.Deserialize<T>(bytes, lz4 ? Lz4BlockArrayOptions : StandardOptions);
    }
}

namespace Sirius.Toolbox.Episodes.Protocol
{
    // These contracts mirror the wire layout used by episode scene binaries.
    // MessagePack keys and enum numeric values are compatibility-sensitive.
    [MessagePackObject(false)]
    public sealed class EpisodeDetailResult
    {
        [Key(0)] public long Id { get; set; }
        [Key(1)] public long EpisodeMasterId { get; set; }
        [Key(2)] public int Order { get; set; }
        [Key(3)] public int GroupOrder { get; set; }
        [Key(4)] public string Effect { get; set; } = string.Empty;
        [Key(5)] public string SpeakerName { get; set; } = string.Empty;
        [Key(6)] public FontSizes FontSize { get; set; }
        [Key(7)] public string Phrase { get; set; } = string.Empty;
        [Key(8)] public string Title { get; set; } = string.Empty;
        [Key(9)] public string BackgroundImageFileName { get; set; } = string.Empty;
        [Key(10)] public string BackgroundCharacterImageFileName { get; set; } = string.Empty;
        [Key(11)] public FadeTypes? BackgroundImageFileFadeType { get; set; }
        [Key(21)] public float? FadeValue1 { get; set; }
        [Key(22)] public float? FadeValue2 { get; set; }
        [Key(23)] public float? FadeValue3 { get; set; }
        [Key(12)] public string BgmFileName { get; set; } = string.Empty;
        [Key(13)] public string SeFileName { get; set; } = string.Empty;
        [Key(14)] public string StillPhotoFileName { get; set; } = string.Empty;
        [Key(15)] public string MovieFileName { get; set; } = string.Empty;
        [Key(16)] public WindowEffects? WindowEffect { get; set; }
        [Key(17)] public long? SceneCameraMasterId { get; set; }
        [Key(18)] public string VoiceFileName { get; set; } = string.Empty;
        [Key(19)] public EpisodeDetailCharacterMotionResult[] CharacterMotions { get; set; } = [];
        [Key(20)] public string SpeakerIconId { get; set; } = string.Empty;
    }

    [MessagePackObject(false)]
    public sealed class EpisodeDetailCharacterMotionResult
    {
        [Key(0)] public int SlotNumber { get; set; }
        [Key(1)] public long? FacialExpressionMasterId { get; set; }
        [Key(2)] public long? HeadMotionMasterId { get; set; }
        [Key(3)] public long? HeadDirectionMasterId { get; set; }
        [Key(4)] public long? BodyMotionMasterId { get; set; }
        [Key(5)] public long? LipSyncMasterId { get; set; }
        [Key(6)] public long SpineId { get; set; }
        [Key(7)] public CharacterAppearanceTypes? CharacterAppearanceType { get; set; }
        [Key(8)] public CharacterPositions CharacterPosition { get; set; }
        [Key(9)] public CharacterLayerTypes CharacterLayerType { get; set; }
        [Key(10)] public SpineSizes SpineSize { get; set; }
    }

    public enum FontSizes
    {
        Small = 1,
        Middle = 2,
        Large = 3
    }

    public enum FadeTypes
    {
        BlackFadeOutFadeIn = 1,
        WhiteFadeOutFadeIn = 2,
        TimeElapsed = 3,
        CrossFade = 4
    }

    public enum WindowEffects
    {
        Sepia = 1,
        WhiteBlur = 2
    }

    public enum CharacterAppearanceTypes
    {
        FadeIn = 0,
        SlideInFromRight = 1,
        SlideInFromLeft = 2,
        SlideInFromBottom = 3
    }

    public enum CharacterLayerTypes
    {
        None = 0,
        Layer1 = 1,
        Layer2 = 2,
        Layer3 = 3
    }

    public enum CharacterPositions
    {
        None = 0,
        OuterLeft = 1,
        InnerLeft = 2,
        Center = 3,
        InnerRight = 4,
        OuterRight = 5
    }

    public enum SpineSizes
    {
        Small = 1,
        Middle = 2,
        Large = 3
    }
}
