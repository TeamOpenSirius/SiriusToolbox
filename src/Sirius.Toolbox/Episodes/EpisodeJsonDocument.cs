using Sirius.Toolbox.Episodes.Protocol;

namespace Sirius.Toolbox.Episodes;

/// <summary>
/// JSON wrapper used by wds-sirius/Adv-Resource/episode/*.json.
/// Only EpisodeDetail is stored in the game's scenes/*.bin resource.
/// </summary>
public sealed class EpisodeJsonDocument
{
    public long EpisodeId { get; set; }
    public int StoryType { get; set; }
    public int Order { get; set; }
    public long? Prev { get; set; }
    public long? Next { get; set; }
    public string? Chapter { get; set; }
    public string? Title { get; set; }
    public EpisodeDetailResult[]? EpisodeDetail { get; set; }
}
