using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Sirius.Toolbox.Assets;
using Sirius.Toolbox.Charts;
using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.R2;
using Sirius.Toolbox.Episodes.Protocol;
using Sirius.Toolbox.Master.Sync;
using Sirius.MasterData;

var root = Path.Combine(Path.GetTempPath(), "sirius-asset-tool-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var invalidMasterSyncRejected = false;
    try
    {
        MasterDataSyncService.ValidateOptions(new MasterDataSyncOptions(string.Empty));
    }
    catch (ArgumentException)
    {
        invalidMasterSyncRejected = true;
    }
    Assert(invalidMasterSyncRejected, "MasterData sync accepted an empty output directory.");

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

    var inheritedEpisodeJson = Path.Combine(root, "外层ID继承.json");
    var inheritedEpisodeBin = Path.Combine(root, "外层ID继承.bin");
    await File.WriteAllTextAsync(inheritedEpisodeJson, """
        {
          "EpisodeId": 230460,
          "EpisodeDetail": [
            {
              "Id": 23046008,
              "Order": 8,
              "GroupOrder": 1,
              "Phrase": "外层 ID 提供主数据关联",
              "CharacterMotions": []
            }
          ]
        }
        """);
    var inheritedPack = episodeService.Pack(inheritedEpisodeJson, inheritedEpisodeBin, overwrite: false);
    var inheritedDetails = EpisodeCodec.Unpack(await File.ReadAllBytesAsync(inheritedEpisodeBin));
    Assert(inheritedPack.DetailCount == 1 && inheritedDetails[0].EpisodeMasterId == 230460,
        "Episode pack did not inherit the wrapper EpisodeId when EpisodeMasterId was omitted.");

    var posterStoryInput = Path.Combine(root, "特殊剧情输入");
    var posterStoryOutput = Path.Combine(root, "特殊剧情输出");
    Directory.CreateDirectory(posterStoryInput);
    await File.WriteAllTextAsync(Path.Combine(posterStoryInput, "230460.json"), """
        {
          "EpisodeId": 230460,
          "StoryType": 5,
          "EpisodeDetail": [
            {
              "Id": 23046008,
              "EpisodeType": 0,
              "CharacterId": null,
              "SpeakerName": "",
              "Description": "特殊剧情正文不能丢失",
              "Order": 8
            }
          ]
        }
        """);
    var posterStoryBatch = episodeService.PackDirectory(posterStoryInput, posterStoryOutput, overwrite: false);
    var posterStoryItem = posterStoryBatch.Items.Single();
    Assert(posterStoryItem.Skipped && !posterStoryItem.Succeeded && !File.Exists(Path.Combine(posterStoryOutput, "230460.bin")),
        "Poster story JSON was incorrectly treated as a scene BIN.");

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

    var cdnPrefixOutput = Path.Combine(root, "缓存输出", "cdn-prefixes.json");
    cacheService.Build(
        cacheEpisodeRoot,
        cacheSceneRoot,
        cdnPrefixOutput,
        new SceneAssetCacheOptions(
            false,
            "md-test",
            "rev-test",
            "master-data/production/scenes-zh-cn",
            "master-data/production/scenes-zh-cn"),
        overwrite: false);
    var cdnPrefixDocument = cacheService.Load(cdnPrefixOutput);
    Assert(cdnPrefixDocument.Assets["42"].SourcePath == "master-data/production/scenes-zh-cn/嵌套目录/42.json", "Custom Episode CDN prefix was not applied to SourcePath.");
    Assert(cdnPrefixDocument.Assets["42"].RelativePath == "master-data/production/scenes-zh-cn/嵌套目录/42.bin", "Custom scene CDN prefix was not applied to RelativePath.");

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

    var assetsRoot = Path.Combine(syncRoot, "assets");
    var selectedAssetsPlan = syncService.BuildPlan(syncOptions with { RootDirectory = assetsRoot });
    Assert(selectedAssetsPlan.Objects.Count == syncPlan.Objects.Count, "Selecting the assets directory did not discover the same R2 objects.");
    var selectedAssetsResult = await syncService.SyncAsync(syncOptions with { RootDirectory = assetsRoot });
    Assert(selectedAssetsResult.RootDirectory == Path.GetFullPath(syncRoot), "Assets-directory input was not normalized to the project root.");
    Assert(selectedAssetsResult.MappingManifestPath == Path.Combine(syncRoot, "assets", "r2-object-map.tsv"), "Assets-directory input wrote the mapping manifest to a nested assets directory.");

    using (var fakeR2 = new FakeR2Server())
    {
        var networkOptions = syncOptions with
        {
            Endpoint = fakeR2.Endpoint,
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            SessionToken = "cfat_test-cloudflare-api-token",
            DryRun = false,
            MaxRetries = 0
        };
        var progressEvents = new ConcurrentQueue<R2SyncProgress>();
        var uploadedResult = await syncService.SyncAsync(
            networkOptions,
            new InlineProgress<R2SyncProgress>(progressEvents.Enqueue));
        Assert(uploadedResult.UploadedCount == 7 && uploadedResult.SkippedCount == 0 &&
               uploadedResult.SeededHashCount == 1 && uploadedResult.CachedHashCount == 1,
            "Full R2 sync did not upload all objects or seed the CDN hash cache.");
        Assert(File.Exists(uploadedResult.HashCachePath), "Full R2 sync did not persist the hash cache.");
        Assert(progressEvents.Any(item => item.Message.Contains("本地缓存命中", StringComparison.Ordinal)),
            "R2 sync progress did not report incremental hash-cache status.");
        var firstBaselineHeadCount = fakeR2.HeadCount;
        Assert(firstBaselineHeadCount == 7, "Initial incremental sync did not verify every remote object.");

        var skippedResult = await syncService.SyncAsync(networkOptions);
        Assert(skippedResult.CachedHashCount == 7 && skippedResult.UploadedCount == 0 && skippedResult.SkippedCount == 7, "Full R2 sync did not reuse the hash cache and skip unchanged remote objects.");
        Assert(fakeR2.HeadCount == firstBaselineHeadCount + 7, "Default incremental sync stopped checking remote objects.");
        Assert(fakeR2.PutSignedHeaders.Count > 0 &&
               fakeR2.PutSignedHeaders.All(headers => headers.Split(';').Contains("content-type", StringComparer.Ordinal)),
            "R2 PUT signatures did not include the Content-Type header.");
        Assert(fakeR2.PutSecurityTokens.Count > 0 &&
               fakeR2.PutSecurityTokens.All(string.IsNullOrEmpty),
            "Cloudflare API Tokens must not be sent as S3 session tokens.");

        File.Delete(uploadedResult.HashCachePath);
        var firstOnlyChangedHeadCount = fakeR2.HeadCount;
        var firstOnlyChangedResult = await syncService.SyncAsync(networkOptions with { OnlyUploadChanged = true });
        Assert(firstOnlyChangedResult.UploadedCount == 0 && firstOnlyChangedResult.SkippedCount == 7,
            "The first local-change-only sync did not establish its remote baseline.");
        Assert(fakeR2.HeadCount == firstOnlyChangedHeadCount + 7,
            "The first local-change-only sync did not verify every remote object.");

        var stableOnlyChangedHeadCount = fakeR2.HeadCount;
        var stableOnlyChangedResult = await syncService.SyncAsync(networkOptions with { OnlyUploadChanged = true });
        Assert(stableOnlyChangedResult.UploadedCount == 0 && stableOnlyChangedResult.SkippedCount == 7 &&
               fakeR2.HeadCount == stableOnlyChangedHeadCount,
            "Local-change-only sync still checked unchanged objects after the baseline was established.");

        await File.WriteAllBytesAsync(scenePath, [4, 5, 6, 7]);
        var changedOnlyResult = await syncService.SyncAsync(networkOptions with { OnlyUploadChanged = true });
        Assert(changedOnlyResult.UploadedCount == 1 && changedOnlyResult.SkippedCount == 6 &&
               fakeR2.HeadCount == stableOnlyChangedHeadCount,
            "Local-change-only sync did not upload only the changed file.");
    }

    var customRoot = Path.Combine(root, "自定义映射输出");
    var customScenePath = Path.Combine(customRoot, "assets", "files", "scenes-zh-cn", "230460.bin");
    Directory.CreateDirectory(Path.GetDirectoryName(customScenePath)!);
    await File.WriteAllBytesAsync(customScenePath, [8, 9, 10]);
    var customOptions = new R2SyncOptions(customRoot)
    {
        Endpoint = "https://example.r2.cloudflarestorage.com",
        Bucket = "wds",
        KeyPrefix = "release",
        DryRun = true,
        CustomMappings = [new R2CustomMapping("scenes-zh-cn", "master-data/production/scenes-zh-cn")]
    };
    var customPlan = syncService.BuildPlan(customOptions);
    Assert(customPlan.Objects.Count == 1 &&
           customPlan.Objects[0].ObjectKey == "release/master-data/production/scenes-zh-cn/230460.bin",
        "Custom R2 directory mapping did not override the default production mapping.");

    // ---- Addressables catalog 解析（纯函数） ----
    var catalogJson = Encoding.UTF8.GetBytes("""
        {
          "m_InternalIdPrefixes": ["{AssetUrl}/2d-assets/Android/100/bundles/"],
          "m_InternalIds": ["0#a.bundle", "0#b.bundle", "0#a.bundle"]
        }
        """);
    var brotliCatalog = CompressBrotli(catalogJson);
    Assert(brotliCatalog[0] != (byte)'{' && !brotliCatalog.SequenceEqual(catalogJson),
        "Brotli catalog fixture was not actually compressed.");
    Assert(AssetCatalogParser.DecodeCatalogBytes(brotliCatalog).SequenceEqual(catalogJson),
        "Brotli-compressed catalog was not decoded back to JSON.");
    Assert(AssetCatalogParser.DecodeCatalogBytes(catalogJson).SequenceEqual(catalogJson),
        "Plain JSON catalog bytes were modified by the decoder.");
    Assert(AssetCatalogParser.DecodeCatalogBytes([1, 2, 3]).SequenceEqual(new byte[] { 1, 2, 3 }),
        "Undecodable catalog bytes were not returned unchanged.");

    const string parserBase = "http://cdn.example.com/cdn";
    const string parserCatalogUrl = "http://cdn.example.com/cdn/2d-assets/Android/100/catalog_100.json.br";
    var parsedCatalog = AssetCatalogParser.Parse(catalogJson, parserCatalogUrl, "2d-assets", "Android", parserBase, "100");
    Assert(parsedCatalog.Count == 2, "m_InternalIdPrefixes expansion did not de-duplicate catalog internal ids.");
    Assert(parsedCatalog.All(item => item.Url.StartsWith($"{parserBase}/2d-assets/Android/100/bundles/", StringComparison.Ordinal)),
        "m_InternalIdPrefixes were not expanded into downloadable object URLs.");
    Assert(parsedCatalog.All(item => item.RelativePath == $"2d-assets/cdn.example.com/cdn/2d-assets/Android/100/bundles/{item.Url[(item.Url.LastIndexOf('/') + 1)..]}"),
        "Catalog object relative paths are incorrect.");
    Assert(parsedCatalog.All(item => item.Status == CdnAssetStatus.Pending), "Newly parsed catalog objects must start Pending.");

    var iosEntries = AssetCatalogParser.BuildPlatformEntries(parsedCatalog, "iOS");
    Assert(iosEntries.Count == parsedCatalog.Count
           && iosEntries.All(item => item.Url.Contains("/iOS/", StringComparison.Ordinal)
                                     && !item.Url.Contains("/Android/", StringComparison.Ordinal)),
        "iOS platform entries were not re-derived from the shared Android catalog.");

    Assert(AssetCatalogParser.GetCatalogHashUrl(parserCatalogUrl) == "http://cdn.example.com/cdn/2d-assets/Android/100/catalog_100.hash",
        "Catalog hash URL for a Brotli catalog is incorrect.");
    Assert(AssetCatalogParser.GetCatalogHashUrl("http://cdn.example.com/a/catalog_100.json") == "http://cdn.example.com/a/catalog_100.hash",
        "Catalog hash URL for a plain JSON catalog is incorrect.");
    Assert(AssetCatalogParser.CatalogCandidates(parserBase, "100", "2d-assets", "Android").First()
           == $"{parserBase}/2d-assets/Android/100/catalog_100.json.br",
        "Default catalog candidate order is incorrect.");
    Assert(AssetCatalogParser.CatalogCandidates(parserBase, "100", "2d-assets", "iOS").Count() == 2,
        "Non-Android platforms must only try the platform directory catalog shapes.");
    Assert(AssetCatalogParser.IsSafeRelativePath("2d-assets/a.bundle")
           && !AssetCatalogParser.IsSafeRelativePath("../escape.bundle"),
        "Relative path safety check is incorrect.");

    // ---- CDN 镜像：catalog 发现、增量 diff、断点续传、416 收口与 404 ----
    using (var fakeCdn = new FakeCdnServer())
    {
        var mirrorRoot = Path.Combine(root, "CDN 镜像输出");
        Directory.CreateDirectory(Path.Combine(mirrorRoot, "assets"));
        await File.WriteAllTextAsync(
            Path.Combine(mirrorRoot, "assets", "static-assets.txt"),
            "# 缺失的静态资源应被记为 404\nResources/Textures/Banners/missing.astc.gz\n");

        var assetBase = fakeCdn.Endpoint + "/cdn";
        var mirrorOptions = new CdnAssetMirrorOptions(mirrorRoot)
        {
            AssetCategories = ["2d-assets"],
            Concurrency = 2,
            Retries = 0,
            SkipStaticAssets = false
        };
        var mirrorContext = new CdnAssetMirrorContext
        {
            AssetBaseUrl = assetBase,
            AssetVersion = "100",
            StaticContentBaseUrl = fakeCdn.Endpoint + "/static"
        };

        var bundleA = Encoding.UTF8.GetBytes(new string('a', 600));
        var bundleB = Encoding.UTF8.GetBytes(new string('b', 600));
        var bundleC = Encoding.UTF8.GetBytes(new string('c', 600));
        var bundleD = Encoding.UTF8.GetBytes(new string('d', 600));

        var bundleUrlBase = $"{assetBase}/2d-assets/Android/100/bundles/";
        string FinalPathFor(string name) => Path.Combine(
            mirrorRoot,
            "assets",
            "files",
            AssetCatalogParser.BuildRelativePath(new Uri(bundleUrlBase + name), "2d-assets")
                .Replace('/', Path.DirectorySeparatorChar));

        const string catalogRoute = "/cdn/2d-assets/Android/100/catalog_100.json.br";
        const string catalogHashRoute = "/cdn/2d-assets/Android/100/catalog_100.hash";

        var finalC = FinalPathFor("c.bundle");
        var finalD = FinalPathFor("d.bundle");
        Directory.CreateDirectory(Path.GetDirectoryName(finalC)!);
        await File.WriteAllBytesAsync(finalC + ".part", bundleC[..300]);
        await File.WriteAllBytesAsync(finalD + ".part", bundleD);

        fakeCdn.SetRoute(catalogRoute, CompressBrotli(Encoding.UTF8.GetBytes(
            BuildCatalogJson(assetBase, "a.bundle", "b.bundle", "c.bundle", "d.bundle"))));
        fakeCdn.SetRoute(catalogHashRoute, Encoding.UTF8.GetBytes("hash-1"));
        fakeCdn.SetRoute("/cdn/2d-assets/Android/100/bundles/a.bundle", bundleA);
        fakeCdn.SetRoute("/cdn/2d-assets/Android/100/bundles/b.bundle", bundleB);
        fakeCdn.SetRoute("/cdn/2d-assets/Android/100/bundles/c.bundle", bundleC);
        fakeCdn.SetRoute("/cdn/2d-assets/Android/100/bundles/d.bundle", bundleD);

        var mirrorService = new CdnAssetMirrorService();
        var mirrorProgress = new ConcurrentQueue<CdnAssetMirrorProgress>();
        var mirrorResult = await mirrorService.MirrorAsync(
            mirrorOptions,
            mirrorContext,
            episodeApi: null,
            progress: new InlineProgress<CdnAssetMirrorProgress>(mirrorProgress.Enqueue));

        Assert(mirrorResult.DownloadedCount == 4 && mirrorResult.NotFoundCount == 1 && mirrorResult.RemovedCount == 0,
            "CDN mirror download, 404, and removal counters are incorrect.");
        Assert(mirrorResult.ObjectCount == 5 && mirrorResult.CatalogCount == 1,
            "CDN mirror did not register every catalog and supplemental object.");
        Assert(File.Exists(mirrorResult.ManifestPath), "CDN mirror did not persist its manifest.");
        Assert((await File.ReadAllBytesAsync(finalC)).SequenceEqual(bundleC),
            "Resumed download did not append onto the existing .part file.");
        Assert((await File.ReadAllBytesAsync(finalD)).SequenceEqual(bundleD),
            "A 416 response for a complete .part file was not promoted to the final object.");
        Assert(!File.Exists(finalC + ".part") && !File.Exists(finalD + ".part"),
            "CDN mirror left .part files behind after successful downloads.");
        Assert(fakeCdn.RangeRequestCount == 2, "CDN mirror did not resume exactly the two partial objects with a Range request.");
        Assert(mirrorProgress.Any(item => item.Message.Contains("404", StringComparison.Ordinal)),
            "CDN mirror progress did not report the missing static asset as 404.");

        fakeCdn.SetRoute(catalogRoute, CompressBrotli(Encoding.UTF8.GetBytes(
            BuildCatalogJson(assetBase, "a.bundle", "b.bundle"))));
        fakeCdn.SetRoute(catalogHashRoute, Encoding.UTF8.GetBytes("hash-2"));

        var incrementalProgress = new ConcurrentQueue<CdnAssetMirrorProgress>();
        var incrementalResult = await mirrorService.MirrorAsync(
            mirrorOptions,
            mirrorContext,
            episodeApi: null,
            progress: new InlineProgress<CdnAssetMirrorProgress>(incrementalProgress.Enqueue));

        Assert(incrementalResult.DownloadedCount == 0,
            "Incremental CDN mirror re-downloaded unchanged objects.");
        Assert(incrementalResult.RemovedCount == 2 && incrementalResult.ObjectCount == 3,
            "Incremental CDN mirror did not drop the objects removed from the catalog.");
        Assert(incrementalProgress.Any(item => item.Message.Contains("增量差异 新增=0 未变=2 移除=2", StringComparison.Ordinal)),
            "Incremental CDN mirror did not report the added/unchanged/removed diff.");
        Assert(incrementalResult.NotFoundCount == 0,
            "Incremental CDN mirror re-reported an already settled 404 static asset.");

        var selectedOutput = Path.Combine(root, "官方同步选择目录");
        var directOptions = mirrorOptions with
        {
            OutputDirectory = selectedOutput,
            UseOutputDirectoryAsAssetRoot = true,
            SkipStaticAssets = true
        };
        var directResult = await mirrorService.MirrorAsync(
            directOptions,
            mirrorContext,
            episodeApi: null);
        Assert(Path.GetDirectoryName(directResult.ManifestPath) == Path.Combine(selectedOutput, "manifests"),
            "Official sync did not write the CDN manifest directly under the selected directory.");
        Assert(File.Exists(Path.Combine(
                selectedOutput,
                "files",
                AssetCatalogParser.BuildRelativePath(new Uri(bundleUrlBase + "a.bundle"), "2d-assets")
                    .Replace('/', Path.DirectorySeparatorChar))),
            "Official sync did not write CDN objects directly under the selected directory.");
        Assert(!Directory.Exists(Path.Combine(selectedOutput, "assets")),
            "Official sync unexpectedly created a nested assets directory.");
    }

    var packSample = Environment.GetEnvironmentVariable("SIRIUS_MASTERMEMORY_SAMPLE")
        ?? Path.Combine("E:\\Ymst", "Projects", "SiriusNet", "data", "mastermemory.db");
    if (!File.Exists(packSample))
    {
        Console.WriteLine($"SKIP offline master repack tests: sample database not found at {packSample}.");
    }
    else
    {
        var packWork = Path.Combine(root, "master-repack");
        var packJson = Path.Combine(packWork, "json");
        var packBaseline = Path.Combine(packWork, "baseline");
        Directory.CreateDirectory(packWork);
        await MasterMemoryDatabaseService.ExportAllJsonAsync(packSample, packJson, _ => { }, CancellationToken.None);
        CopyDirectory(packJson, packBaseline);

        var exactOutput = Path.Combine(packWork, "repack-exact.db");
        var exactPack = MasterMemoryDatabaseService.PackFromJson(
            packSample, packJson, packBaseline, exactOutput, requireExact: true);
        Assert(exactPack.Exact
            && exactPack.RebuiltTables.Count == 0
            && exactPack.PreservedTables.Count == exactPack.TableCount,
            "Offline repack of untouched JSON was not byte-exact.");
        Assert(exactPack.OutputSha256 == exactPack.SourceSha256,
            "Offline repack produced a different hash for untouched JSON.");

        var reformatted = Path.Combine(packWork, "reformatted");
        CopyDirectory(packJson, reformatted);
        foreach (var jsonFile in Directory.EnumerateFiles(reformatted, "*.json"))
        {
            var reformattedNode = JsonNode.Parse(await File.ReadAllTextAsync(jsonFile));
            await File.WriteAllTextAsync(jsonFile, reformattedNode!.ToJsonString());
        }
        var reformattedOutput = Path.Combine(packWork, "repack-reformatted.db");
        var reformattedPack = MasterMemoryDatabaseService.PackFromJson(
            packSample, reformatted, packBaseline, reformattedOutput, requireExact: false);
        Assert(reformattedPack.Exact && reformattedPack.RebuiltTables.Count == 0,
            "Offline repack rebuilt tables whose JSON only changed formatting.");

        var edited = Path.Combine(packWork, "edited");
        CopyDirectory(packJson, edited);
        string? editedTable = null;
        string? editedFile = null;
        JsonNode? editedNode = null;
        foreach (var jsonFile in Directory.EnumerateFiles(edited, "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            var table = Path.GetFileNameWithoutExtension(jsonFile);
            var stringFields = MasterMemoryDatabaseService.GetSchema(packSample, table)
                .Where(property => property.Type is "System.String" or "string")
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (stringFields.Count == 0)
                continue;
            var candidate = JsonNode.Parse(await File.ReadAllTextAsync(jsonFile));
            if (!MutateFirstString(candidate, stringFields))
                continue;
            editedTable = table;
            editedFile = jsonFile;
            editedNode = candidate;
            break;
        }
        Assert(editedTable is not null && editedFile is not null && editedNode is not null,
            "Offline repack found no editable string value in the sample database.");
        await File.WriteAllTextAsync(editedFile!, editedNode!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var editedOutput = Path.Combine(packWork, "repack-edited.db");
        var editedPack = MasterMemoryDatabaseService.PackFromJson(
            packSample, edited, packBaseline, editedOutput, requireExact: false);
        Assert(editedPack.RebuiltTables.Count == 1 && editedPack.RebuiltTables[0] == editedTable && !editedPack.Exact,
            $"Offline repack did not rebuild exactly the edited table ({editedTable}).");
        var editedVerification = MasterMemoryDatabaseService.Verify(editedOutput);
        Assert(editedVerification.TableCount == editedPack.TableCount && editedVerification.RowCount > 0,
            "Offline repack output failed full database verification.");
    }

    Console.WriteLine("Chart, Episode, Asset catalog, CDN mirror, MasterData R2, and offline pack service tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CopyDirectory(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var file in Directory.EnumerateFiles(from))
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
}

static bool MutateFirstString(JsonNode? node, IReadOnlySet<string> allowedNames)
{
    switch (node)
    {
        case JsonArray array:
            foreach (var item in array)
            {
                if (MutateFirstString(item, allowedNames))
                    return true;
            }
            return false;
        case JsonObject obj:
            foreach (var pair in obj.ToArray())
            {
                if (allowedNames.Contains(pair.Key)
                    && pair.Value is JsonValue value
                    && value.TryGetValue<string>(out var text))
                {
                    obj[pair.Key] = text + "__verify";
                    return true;
                }
                if (MutateFirstString(pair.Value, allowedNames))
                    return true;
            }
            return false;
        default:
            return false;
    }
}

static byte[] CompressBrotli(byte[] data)
{
    using var output = new MemoryStream();
    using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        brotli.Write(data);
    return output.ToArray();
}

static string BuildCatalogJson(string assetBaseUrl, params string[] internalIds) =>
    "{\"m_InternalIdPrefixes\":[\"{AssetUrl}/2d-assets/Android/100/bundles/\"],\"m_InternalIds\":["
    + string.Join(",", internalIds.Select(id => $"\"0#{id}\""))
    + "]}";

sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public InlineProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}

sealed class FakeR2Server : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<string, (long Length, string Sha256)> _objects = new(StringComparer.Ordinal);
    private int _headCount;
    public ConcurrentBag<string> PutSignedHeaders { get; } = new();
    public ConcurrentBag<string> PutSecurityTokens { get; } = new();
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
    public int HeadCount => Volatile.Read(ref _headCount);

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
            var headerBytes = await FakeHttp.ReadHeadersAsync(stream, _cancellation.Token);
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
            if (request[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _headCount);
            }
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
                PutSecurityTokens.Add(headers.TryGetValue("x-amz-security-token", out var securityToken)
                    ? securityToken
                    : string.Empty);
                if (headers.TryGetValue("Authorization", out var authorization))
                {
                    var signedHeadersMarker = "SignedHeaders=";
                    var start = authorization.IndexOf(signedHeadersMarker, StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        start += signedHeadersMarker.Length;
                        var end = authorization.IndexOf(", Signature=", start, StringComparison.Ordinal);
                        PutSignedHeaders.Add((end >= 0 ? authorization[start..end] : authorization[start..]).Trim());
                    }
                }
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

/// <summary>两个假 HTTP 服务共用的请求行/头部读取工具。</summary>
internal static class FakeHttp
{
    public static async Task<byte[]> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
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

    public static string[] SplitLines(byte[] headerBytes) =>
        Encoding.ASCII.GetString(headerBytes).Split("\r\n", StringSplitOptions.None);

    public static Dictionary<string, string> ParseHeaders(IEnumerable<string> lines)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return headers;
    }

    public static long ParseRangeOffset(string? rangeHeader)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader)
            || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var spec = rangeHeader["bytes=".Length..];
        var dash = spec.IndexOf('-');
        return long.TryParse(dash > 0 ? spec[..dash] : spec, out var offset) ? offset : 0;
    }
}

/// <summary>
/// 极简 CDN HTTP 服务：按路径返回注册的字节内容，支持 Range / 206 / 416 / 404，
/// 用于验证镜像的增量下载与断点续传行为。
/// Minimal CDN HTTP server used to exercise mirror resume, 416 semantics, and 404 handling.
/// </summary>
sealed class FakeCdnServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentDictionary<string, byte[]> _routes = new(StringComparer.Ordinal);
    private int _rangeRequestCount;

    public FakeCdnServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Endpoint = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _acceptLoop = AcceptLoopAsync();
    }

    public string Endpoint { get; }
    public int RangeRequestCount => Volatile.Read(ref _rangeRequestCount);

    public void SetRoute(string path, byte[] content) => _routes[path] = content;

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
            var headerBytes = await FakeHttp.ReadHeadersAsync(stream, _cancellation.Token);
            var lines = FakeHttp.SplitLines(headerBytes);
            var request = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (request.Length < 2)
                return;
            var headers = FakeHttp.ParseHeaders(lines.Skip(1));

            var path = request[1];
            var queryIndex = path.IndexOf('?');
            if (queryIndex >= 0)
                path = path[..queryIndex];

            var offset = FakeHttp.ParseRangeOffset(
                headers.TryGetValue("Range", out var rangeValue) ? rangeValue : null);

            var status = "404 Not Found";
            var extraHeaders = string.Empty;
            byte[]? body = null;
            if (_routes.TryGetValue(path, out var content))
            {
                if (offset <= 0)
                {
                    status = "200 OK";
                    body = content;
                }
                else
                {
                    Interlocked.Increment(ref _rangeRequestCount);
                    if (offset >= content.Length)
                    {
                        status = "416 Range Not Satisfiable";
                        extraHeaders = $"Content-Range: bytes */{content.Length}\r\n";
                    }
                    else
                    {
                        status = "206 Partial Content";
                        body = content[(int)offset..];
                        extraHeaders = $"Content-Range: bytes {offset}-{content.Length - 1}/{content.Length}\r\n";
                    }
                }
            }

            var head = $"HTTP/1.1 {status}\r\nContent-Length: {body?.Length ?? 0}\r\n{extraHeaders}Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _cancellation.Token);
            if (body is { Length: > 0 })
                await stream.WriteAsync(body, _cancellation.Token);
        }
    }
}
