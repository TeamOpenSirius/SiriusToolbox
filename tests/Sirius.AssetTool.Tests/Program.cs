using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Sirius.AssetTool.Charts;
using Sirius.AssetTool.Episodes;
using Sirius.AssetTool.R2;
using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.Episodes.Protocol;

var root = Path.Combine(Path.GetTempPath(), "sirius-asset-tool-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var input = Path.Combine(root, "测试谱面.sus");
    var output = Path.Combine(root, "输出.txt");
    await File.WriteAllTextAsync(input, "#TITLE:Test\n#BPM01:120\n#WAVEOFFSET:0\n#00112:11\n");

    var service = new ChartToolService();
    var result = service.ConvertToText(input, output, new ChartConvertOptions(false, false));
    Assert(File.Exists(output), "Chart text output was not created.");
    Assert(result.NoteCount > 0, "Chart conversion produced no notes.");

    var text = await File.ReadAllTextAsync(output);
    Assert(text.Length > 0, "Chart text output was empty.");

    var episodeJson = Path.Combine(root, "剧情.json");
    var episodeBin = Path.Combine(root, "剧情.bin");
    await File.WriteAllTextAsync(episodeJson, "[{\"Id\":1,\"EpisodeMasterId\":42,\"GroupOrder\":1,\"Phrase\":\"你好\",\"CharacterMotions\":[]}]");
    var episodeService = new EpisodeToolService();
    var packed = episodeService.Pack(episodeJson, episodeBin, overwrite: false);
    Assert(packed.DetailCount == 1 && File.Exists(episodeBin), "Episode pack failed.");
    var rejectedExistingOutput = false;
    try
    {
        episodeService.Pack(episodeJson, episodeBin, overwrite: false);
    }
    catch (IOException)
    {
        rejectedExistingOutput = true;
    }
    Assert(rejectedExistingOutput, "Episode pack did not reject an existing output.");
    var unpacked = episodeService.Unpack(episodeBin, Path.Combine(root, "解包.json"), overwrite: false);
    Assert(unpacked.DetailCount == 1, "Episode unpack failed.");
    var inspect = episodeService.Inspect(episodeBin);
    Assert(inspect.Decoded && !inspect.IsProbablyCorrupt, "Episode inspect failed.");

    var batchInput = Path.Combine(root, "批量输入");
    var batchBin = Path.Combine(root, "批量BIN");
    var batchJson = Path.Combine(root, "批量JSON");
    Directory.CreateDirectory(batchInput);
    File.Copy(episodeJson, Path.Combine(batchInput, "episode.json"));
    Directory.CreateDirectory(Path.Combine(batchInput, "子目录"));
    File.Copy(episodeJson, Path.Combine(batchInput, "子目录", "nested.json"));
    var packedBatch = episodeService.PackDirectory(batchInput, batchBin, overwrite: false);
    Assert(packedBatch.Items.Count == 2 && packedBatch.Items.All(item => item.Succeeded), "Episode batch pack failed.");
    var unpackedBatch = episodeService.UnpackDirectory(batchBin, batchJson, overwrite: false);
    Assert(unpackedBatch.Items.Count == 2 && unpackedBatch.Items.All(item => item.Succeeded), "Episode batch unpack failed.");
    Assert(File.Exists(Path.Combine(batchJson, "子目录", "nested.json")), "Episode batch unpack did not preserve subdirectories.");

    var cacheEpisodeRoot = Path.Combine(root, "剧情 JSON 输入 with spaces");
    var cacheSceneRoot = Path.Combine(root, "场景 BIN 输入");
    var cacheJsonDirectory = Path.Combine(cacheEpisodeRoot, "嵌套目录");
    var cacheSceneDirectory = Path.Combine(cacheSceneRoot, "嵌套目录");
    Directory.CreateDirectory(cacheJsonDirectory);
    Directory.CreateDirectory(cacheSceneDirectory);
    var cacheJsonPath = Path.Combine(cacheJsonDirectory, "42.json");
    await File.WriteAllTextAsync(cacheJsonPath, """
        {
          "EpisodeId": 42,
          "Title": "缓存测试",
          "UnknownMeta": "保留",
          "EpisodeDetail": [
            {
              "Id": 1,
              "EpisodeMasterId": 42,
              "Order": 1,
              "GroupOrder": 1,
              "SpeakerName": "测试角色",
              "Phrase": "第一段",
              "CharacterMotions": []
            }
          ]
        }
        """);
    var cacheBinPath = Path.Combine(cacheSceneDirectory, "42.bin");
    episodeService.Pack(cacheJsonPath, cacheBinPath, overwrite: false);
    File.Copy(cacheBinPath, Path.Combine(cacheSceneRoot, "99.bin"));

    var cacheOutput = Path.Combine(root, "缓存输出", "scene-assets.json");
    var cacheService = new SceneAssetCacheService();
    var cacheResult = cacheService.Build(
        cacheEpisodeRoot,
        cacheSceneRoot,
        cacheOutput,
        new SceneAssetCacheOptions(false, "md-test", "rev-test"),
        overwrite: false);
    Assert(cacheResult.AssetCount == 2, "Scene asset cache did not include every BIN.");
    Assert(cacheResult.MatchedJsonCount == 1 && cacheResult.MissingJsonCount == 1, "Scene asset cache match counts are incorrect.");
    var cacheDocument = cacheService.Load(cacheOutput);
    Assert(cacheDocument.Version == 2, "Scene asset cache version is incorrect.");
    Assert(cacheDocument.Assets["42"].SourcePath == "episode/嵌套目录/42.json", "Scene asset source path is incorrect.");
    Assert(cacheDocument.Assets["42"].RelativePath == "scenes/嵌套目录/42.bin", "Scene asset relative path is incorrect.");
    Assert(cacheDocument.Assets["42"].HashAlgorithm == "sha256", "Scene asset hash algorithm is missing.");
    Assert(cacheDocument.Assets["42"].Sha256 == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(cacheBinPath))).ToLowerInvariant(), "Scene asset SHA-256 is incorrect.");
    Assert(cacheDocument.Assets["99"].SourcePath == string.Empty, "Unmatched BIN should have an empty source path.");

    var cacheOverwriteRejected = false;
    try
    {
        cacheService.Build(
            cacheEpisodeRoot,
            cacheSceneRoot,
            cacheOutput,
            new SceneAssetCacheOptions(true, null, null),
            overwrite: false);
    }
    catch (IOException)
    {
        cacheOverwriteRejected = true;
    }
    Assert(cacheOverwriteRejected, "Scene asset cache unexpectedly overwrote an existing output.");
    cacheService.Build(
        cacheEpisodeRoot,
        cacheSceneRoot,
        cacheOutput,
        new SceneAssetCacheOptions(true, null, null),
        overwrite: true);

    var metadataOutput = Path.Combine(root, "缓存输出", "metadata-only.json");
    cacheService.Build(
        cacheEpisodeRoot,
        cacheSceneRoot,
        metadataOutput,
        new SceneAssetCacheOptions(true, null, null),
        overwrite: false);
    var metadataDocument = cacheService.Load(metadataOutput);
    Assert(metadataDocument.Assets["42"].Sha256 == string.Empty, "Metadata-only cache unexpectedly contains a hash.");
    Assert(metadataDocument.Assets["42"].HashAlgorithm == string.Empty, "Metadata-only cache unexpectedly contains a hash algorithm.");
    Assert(metadataDocument.Assets["42"].LastWriteTimeUtcTicks == 0, "Metadata-only cache unexpectedly contains a timestamp.");

    Directory.CreateDirectory(Path.Combine(cacheSceneRoot, "duplicate"));
    File.Copy(cacheBinPath, Path.Combine(cacheSceneRoot, "duplicate", "42.bin"));
    var duplicateOutput = Path.Combine(root, "缓存输出", "duplicate.json");
    var duplicateRejected = false;
    try
    {
        cacheService.Build(
            cacheEpisodeRoot,
            cacheSceneRoot,
            duplicateOutput,
            new SceneAssetCacheOptions(false, null, null),
            overwrite: false);
    }
    catch (InvalidDataException)
    {
        duplicateRejected = true;
    }
    Assert(duplicateRejected && !File.Exists(duplicateOutput), "Duplicate scene IDs were not rejected before output.");

    var editorService = new EpisodeEditorService();
    var editorDocument = editorService.Load(cacheJsonPath);
    editorDocument.Details[0].Phrase = "修改后的中文剧情";
    editorDocument.Details.Add(new EpisodeDetailResult
    {
        Id = 2,
        EpisodeMasterId = 42,
        Order = 2,
        GroupOrder = 1,
        Phrase = "新增剧情",
        CharacterMotions = []
    });
    var editedJsonPath = Path.Combine(root, "剧情编辑后.json");
    editorService.Save(editorDocument, editedJsonPath, overwrite: false);
    var editorOverwriteRejected = false;
    try
    {
        editorService.Save(editorDocument, editedJsonPath, overwrite: false);
    }
    catch (IOException)
    {
        editorOverwriteRejected = true;
    }
    Assert(editorOverwriteRejected, "Episode editor unexpectedly overwrote an existing JSON output.");
    var editedJsonText = await File.ReadAllTextAsync(editedJsonPath);
    Assert(editedJsonText.Contains("修改后的中文剧情", StringComparison.Ordinal) && !editedJsonText.Contains("\\u", StringComparison.OrdinalIgnoreCase), "Episode editor escaped Chinese text in the saved JSON.");
    using var editedJson = JsonDocument.Parse(editedJsonText);
    Assert(editedJson.RootElement.GetProperty("UnknownMeta").GetString() == "保留", "Episode editor dropped unknown wrapper metadata.");
    Assert(editedJson.RootElement.GetProperty("EpisodeDetail").GetArrayLength() == 2, "Episode editor did not save the changed detail count.");
    Assert(editedJson.RootElement.GetProperty("EpisodeDetail")[0].GetProperty("Phrase").GetString() == "修改后的中文剧情", "Episode editor did not save the changed phrase.");
    var editedBinPath = Path.Combine(root, "剧情编辑后.bin");
    editorService.Pack(editorDocument, editedBinPath, overwrite: false);
    var editedDetails = EpisodeCodec.Unpack(await File.ReadAllBytesAsync(editedBinPath));
    Assert(editedDetails.Length == 2 && editedDetails[0].Phrase == "修改后的中文剧情" && editedDetails[0].CharacterMotions.Length == 0, "Edited Episode BIN round-trip failed.");

    var masterRoot = Path.Combine(root, "主数据输出");
    var masterDirectory = Path.Combine(masterRoot, "master");
    Directory.CreateDirectory(masterDirectory);
    var masterManifestPath = Path.Combine(masterDirectory, "manifest.json");
    var masterDatabasePath = Path.Combine(masterDirectory, "mastermemory.db");
    await File.WriteAllTextAsync(masterManifestPath, "{\"Uri\":\"master-data/production/test/mastermemory.db\"}");
    var masterBytes = new byte[] { 0, 1, 2, 3, 4, 5 };
    await File.WriteAllBytesAsync(masterDatabasePath, masterBytes);

    var uploadOptions = new MasterDataR2UploadOptions(masterRoot)
    {
        Endpoint = "https://example.r2.cloudflarestorage.com",
        Bucket = "wds",
        KeyPrefix = "release",
        DryRun = true
    };
    var uploadService = new MasterDataR2UploadService();
    var uploadPlan = uploadService.BuildPlan(uploadOptions);
    Assert(uploadPlan.ObjectKey == "release/master-data/production/test/mastermemory.db", "MasterData R2 object key mapping is incorrect.");
    Assert(uploadPlan.DatabasePath == Path.GetFullPath(masterDatabasePath), "MasterData R2 database path is incorrect.");
    Assert(uploadPlan.Length == masterBytes.Length && uploadPlan.ContentType == "application/octet-stream", "MasterData R2 plan metadata is incorrect.");
    var dryRunResult = await uploadService.UploadAsync(uploadOptions);
    Assert(dryRunResult.DryRun && !dryRunResult.Uploaded && !dryRunResult.Skipped, "MasterData R2 dry-run unexpectedly performed an upload.");
    Assert(dryRunResult.Sha256 == string.Empty, "MasterData R2 dry-run unexpectedly hashed the file.");

    await File.WriteAllTextAsync(masterManifestPath, "{\"Uri\":\"../outside/mastermemory.db\"}");
    var traversalRejected = false;
    try
    {
        uploadService.BuildPlan(uploadOptions);
    }
    catch (InvalidDataException)
    {
        traversalRejected = true;
    }
    Assert(traversalRejected, "MasterData R2 manifest traversal was not rejected.");

    await File.WriteAllTextAsync(masterManifestPath, "{\"Uri\":\"https://example.com/mastermemory.db\"}");
    var absoluteUriRejected = false;
    try
    {
        uploadService.BuildPlan(uploadOptions);
    }
    catch (InvalidDataException)
    {
        absoluteUriRejected = true;
    }
    Assert(absoluteUriRejected, "MasterData R2 absolute manifest URI was not rejected.");

    await File.WriteAllTextAsync(masterManifestPath, "{\"Uri\":\"master-data/production/test/mastermemory.db\"}");
    File.Delete(masterDatabasePath);
    var missingDatabaseRejected = false;
    try
    {
        uploadService.BuildPlan(uploadOptions);
    }
    catch (InvalidDataException)
    {
        missingDatabaseRejected = true;
    }
    Assert(missingDatabaseRejected, "MasterData R2 missing database was not rejected.");

    await File.WriteAllBytesAsync(masterDatabasePath, masterBytes);
    var missingCredentialsRejected = false;
    try
    {
        await uploadService.UploadAsync(uploadOptions with { DryRun = false });
    }
    catch (InvalidOperationException)
    {
        missingCredentialsRejected = true;
    }
    Assert(missingCredentialsRejected, "MasterData R2 missing credentials were not rejected.");

    var syncRoot = Path.Combine(root, "完整 CDN 同步输出");
    var syncMasterDirectory = Path.Combine(syncRoot, "master");
    Directory.CreateDirectory(syncMasterDirectory);
    await File.WriteAllTextAsync(
        Path.Combine(syncMasterDirectory, "manifest.json"),
        "{\"Uri\":\"master-data/production/test/mastermemory.db\"}");
    await File.WriteAllBytesAsync(Path.Combine(syncMasterDirectory, "mastermemory.db"), masterBytes);
    var catalogDirectory = Path.Combine(syncRoot, "assets", "catalogs", "2d-assets", "Android");
    Directory.CreateDirectory(catalogDirectory);
    await File.WriteAllTextAsync(Path.Combine(catalogDirectory, "catalog_2026.json"), "{\"version\":2026}");
    await File.WriteAllTextAsync(Path.Combine(catalogDirectory, "catalog_2026.hash"), "hash");
    var compressedCatalogDirectory = Path.Combine(syncRoot, "assets", "catalogs", "3d-assets");
    Directory.CreateDirectory(compressedCatalogDirectory);
    await File.WriteAllBytesAsync(Path.Combine(compressedCatalogDirectory, "catalog_2027.json.br"), [7, 8, 9]);
    var imagePath = Path.Combine(syncRoot, "assets", "files", "2d-assets", "cdn.example.com", "images", "头像.png");
    Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
    await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
    var scenePath = Path.Combine(syncRoot, "assets", "files", "scenes", "42.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
    await File.WriteAllBytesAsync(scenePath, [4, 5, 6]);
    var notationPath = Path.Combine(syncRoot, "assets", "files", "notations", "character", "voice.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(notationPath)!);
    await File.WriteAllTextAsync(notationPath, "voice");
    await File.WriteAllTextAsync(Path.Combine(syncRoot, "assets", "files", "ignored.part"), "partial");
    var manifestDirectory = Path.Combine(syncRoot, "assets", "manifests");
    Directory.CreateDirectory(manifestDirectory);
    var imageHash = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })).ToLowerInvariant();
    await File.WriteAllTextAsync(
        Path.Combine(manifestDirectory, "cdn_seed.json"),
        $"{{\"Objects\":{{\"image\":{{\"RelativePath\":\"2d-assets/cdn.example.com/images/头像.png\",\"DownloadedSize\":3,\"Sha256\":\"{imageHash}\",\"Status\":2}}}}}}");

    var syncOptions = new R2SyncOptions(syncRoot)
    {
        Endpoint = "https://example.r2.cloudflarestorage.com",
        Bucket = "wds",
        KeyPrefix = "release",
        Concurrency = 4,
        MaxRetries = 2,
        DryRun = true
    };
    var syncService = new R2AssetSyncService();
    var syncPlan = syncService.BuildPlan(syncOptions);
    Assert(syncPlan.Objects.Count == 7, "Full R2 sync did not discover the complete MasterData, catalog, CDN, scene, and notation set.");
    Assert(syncPlan.Objects.Any(item => item.ObjectKey == "release/production/2d-assets/Android/2026/catalog_2026.json"), "Catalog R2 mapping is incorrect.");
    Assert(syncPlan.Objects.Any(item => item.ObjectKey == "release/production/3d-assets/Android/2027/catalog_2027.json.br"), "Compressed catalog R2 mapping is incorrect.");
    Assert(syncPlan.Objects.Any(item => item.ObjectKey == "release/production/images/头像.png"), "CDN origin-host R2 mapping is incorrect.");
    Assert(syncPlan.Objects.Any(item => item.ObjectKey == "release/master-data/production/scenes/42.bin"), "Scene R2 mapping is incorrect.");
    Assert(syncPlan.Objects.Any(item => item.ObjectKey == "release/production/Notations/character/voice.txt"), "Notation R2 mapping is incorrect.");
    var syncResult = await syncService.SyncAsync(syncOptions);
    Assert(syncResult.DryRun && syncResult.ObjectCount == 7 && syncResult.UploadedCount == 0, "Full R2 dry-run result is incorrect.");
    Assert(syncResult.MappingManifestPath is not null && File.Exists(syncResult.MappingManifestPath), "Full R2 dry-run did not write the mapping manifest.");
    Assert(!File.Exists(Path.Combine(syncRoot, "assets", "r2-hash-cache.json")), "Full R2 dry-run unexpectedly wrote the hash cache.");

    using (var fakeR2 = new FakeR2Server())
    {
        var networkOptions = syncOptions with
        {
            Endpoint = fakeR2.Endpoint,
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            DryRun = false,
            MaxRetries = 0
        };
        var uploadedResult = await syncService.SyncAsync(networkOptions);
        Assert(uploadedResult.UploadedCount == 7 && uploadedResult.SkippedCount == 0 &&
               uploadedResult.SeededHashCount == 1 && uploadedResult.CachedHashCount == 1,
            "Full R2 sync did not upload all objects or seed the CDN hash cache.");
        Assert(File.Exists(uploadedResult.HashCachePath), "Full R2 sync did not persist the hash cache.");

        var skippedResult = await syncService.SyncAsync(networkOptions);
        Assert(skippedResult.CachedHashCount == 7 && skippedResult.UploadedCount == 0 && skippedResult.SkippedCount == 7, "Full R2 sync did not reuse the hash cache and skip unchanged remote objects.");
    }

    Console.WriteLine("Chart, Episode, and MasterData R2 service tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeR2Server : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<string, (long Length, string Sha256)> _objects = new(StringComparer.Ordinal);
    private readonly Task _acceptLoop;

    public FakeR2Server()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        Endpoint = $"http://127.0.0.1:{endpoint.Port}";
        _acceptLoop = AcceptLoopAsync();
    }

    public string Endpoint { get; }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        try { _acceptLoop.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _cancellation.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var headerBytes = await ReadHeadersAsync(stream, _cancellation.Token);
            var headerText = Encoding.ASCII.GetString(headerBytes);
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var request = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (request.Length < 2)
                return;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            var length = headers.TryGetValue("Content-Length", out var lengthValue) && long.TryParse(lengthValue, out var parsedLength)
                ? parsedLength
                : 0;
            if (length > 0)
                await DrainAsync(stream, length, _cancellation.Token);

            var key = request[1];
            string response;
            if (request[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase) && _objects.TryGetValue(key, out var current))
            {
                response = $"HTTP/1.1 200 OK\r\nContent-Length: {current.Length}\r\nx-amz-meta-sha256: {current.Sha256}\r\nConnection: close\r\n\r\n";
            }
            else if (request[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                response = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            }
            else if (request[0].Equals("PUT", StringComparison.OrdinalIgnoreCase))
            {
                headers.TryGetValue("x-amz-meta-sha256", out var sha256);
                _objects[key] = (length, sha256 ?? string.Empty);
                response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            }
            else
            {
                response = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            }

            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, _cancellation.Token);
        }
    }

    private static async Task<byte[]> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var single = new byte[1];
        while (buffer.Length < 64 * 1024)
        {
            var read = await stream.ReadAsync(single, cancellationToken);
            if (read == 0)
                break;
            buffer.WriteByte(single[0]);
            if (buffer.Length >= 4)
            {
                var bytes = buffer.GetBuffer();
                var length = (int)buffer.Length;
                if (bytes[length - 4] == '\r' && bytes[length - 3] == '\n' && bytes[length - 2] == '\r' && bytes[length - 1] == '\n')
                    break;
            }
        }
        return buffer.ToArray();
    }

    private static async Task DrainAsync(NetworkStream stream, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (length > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), cancellationToken);
            if (read == 0)
                break;
            length -= read;
        }
    }
}
