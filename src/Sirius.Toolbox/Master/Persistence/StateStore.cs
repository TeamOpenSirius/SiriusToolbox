using System.Text.Json;

namespace Sirius.Toolbox.Master.Persistence;

public sealed class DownloadState
{
    public string? LoginToken { get; set; }
    public string? AccessToken { get; set; }
    public string? ApiEndpoint { get; set; }
    public string? MasterDataUrl { get; set; }
    public string? AssetUrl { get; set; }
    public string? StaticContentUrl { get; set; }
    public string? PhotoContentUrl { get; set; }
    public string? AssetVersion { get; set; }
    public string? MasterDataVersion { get; set; }
    public string? MasterJsonVersion { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? AuthenticationApplicationVersion { get; set; }
    public int GameVersion { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<DownloadState> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new DownloadState();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DownloadState>(stream, Options, ct)
               ?? new DownloadState();
    }

    public static async Task SaveAsync(string path, DownloadState state, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, state, Options, ct);
        File.Move(temp, path, true);
    }
}
