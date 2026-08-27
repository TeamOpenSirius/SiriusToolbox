using System.Text.Json;

namespace Sirius.Tooling.Core.Master.Persistence;

public sealed class MasterSyncPublication
{
    public int SchemaVersion { get; set; } = 1;
    public string MasterDataVersion { get; set; } = string.Empty;
    public string SourceMasterDataVersion { get; set; } = string.Empty;
    public long MasterDataPublishTimestamp { get; set; }
    public string MasterDataUri { get; set; } = string.Empty;
    public string MasterDataFile { get; set; } = string.Empty;
    public string MasterDataSha256 { get; set; } = string.Empty;
    public string MasterDataPolicy { get; set; } = string.Empty;
    public string MasterJsonDirectory { get; set; } = string.Empty;
    public string MasterIndexDatabase { get; set; } = string.Empty;
    public string AssetVersion { get; set; } = string.Empty;
    public string AssetSourceUrl { get; set; } = string.Empty;
    public string StaticContentSourceUrl { get; set; } = string.Empty;
    public string? CdnManifest { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
}

public static class MasterSyncPublicationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static MasterSyncPublication? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<MasterSyncPublication>(File.ReadAllBytes(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(
        string path,
        MasterSyncPublication publication,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, publication, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            const int maxAttempts = 12;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temporaryPath, path, overwrite: true);
                    break;
                }
                catch (Exception ex) when (
                    attempt < maxAttempts
                    && ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
