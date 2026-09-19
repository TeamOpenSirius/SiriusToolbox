using System.Security.Cryptography;
using System.Text.Json;

namespace Sirius.AssetTool.R2;

public static class MasterDataR2UploadDefaults
{
    public const string Endpoint = "https://REMOVED_R2_ENDPOINT/wds";
    public const string Bucket = "wds";
    public const int MaxRetries = 5;
}

public sealed record MasterDataR2UploadOptions(string RootDirectory)
{
    public string Endpoint { get; init; } = MasterDataR2UploadDefaults.Endpoint;
    public string Bucket { get; init; } = MasterDataR2UploadDefaults.Bucket;
    public string? AccessKeyId { get; init; }
    public string? SecretAccessKey { get; init; }
    public string? SessionToken { get; init; }
    public string KeyPrefix { get; init; } = string.Empty;
    public int MaxRetries { get; init; } = MasterDataR2UploadDefaults.MaxRetries;
    public bool Force { get; init; }
    public bool DryRun { get; init; }
}

public sealed record MasterDataR2UploadPlan(
    string ManifestPath,
    string DatabasePath,
    string ObjectKey,
    long Length,
    string ContentType);

public sealed record MasterDataR2UploadProgress(
    string Stage,
    long ProcessedBytes,
    long TotalBytes,
    string Message);

public sealed record MasterDataR2UploadResult(
    string ManifestPath,
    string DatabasePath,
    string ObjectKey,
    long ByteCount,
    string Sha256,
    bool DryRun,
    bool Uploaded,
    bool Skipped);

public sealed class MasterDataR2UploadService
{
    private const long MaxObjectSize = 5L * 1024 * 1024 * 1024;

    public MasterDataR2UploadPlan BuildPlan(MasterDataR2UploadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        var root = RequireDirectory(options.RootDirectory, "主数据输出目录不存在");
        var masterDirectory = Path.Combine(root, "master");
        var manifestPath = Path.Combine(masterDirectory, "manifest.json");
        var databasePath = Path.Combine(masterDirectory, "mastermemory.db");
        if (!File.Exists(manifestPath) || !File.Exists(databasePath))
        {
            throw new InvalidDataException(
                $"主数据发布需要同时存在“{manifestPath}”和“{databasePath}”。");
        }

        string relativeUri;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            if (!document.RootElement.TryGetProperty("Uri", out var uriElement) ||
                uriElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(uriElement.GetString()))
            {
                throw new InvalidDataException($"主数据清单没有有效的 Uri：{manifestPath}");
            }

            relativeUri = uriElement.GetString()!.Replace('\\', '/').Trim('/');
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"主数据清单不是有效 JSON：{manifestPath}", exception);
        }

        var objectKey = BuildObjectKey(relativeUri, options.KeyPrefix);
        var database = new FileInfo(databasePath);
        if (database.Length > MaxObjectSize)
        {
            throw new InvalidDataException(
                $"主数据文件超过单对象 5 GiB 限制：{databasePath}");
        }

        return new MasterDataR2UploadPlan(
            Path.GetFullPath(manifestPath),
            Path.GetFullPath(databasePath),
            objectKey,
            database.Length,
            "application/octet-stream");
    }

    public async Task<MasterDataR2UploadResult> UploadAsync(
        MasterDataR2UploadOptions options,
        IProgress<MasterDataR2UploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var plan = BuildPlan(options);
        progress?.Report(new MasterDataR2UploadProgress(
            "准备",
            0,
            plan.Length,
            $"已找到主数据文件：{plan.DatabasePath}"));

        if (options.DryRun)
        {
            progress?.Report(new MasterDataR2UploadProgress(
                "预览",
                0,
                plan.Length,
                $"预览模式：{plan.DatabasePath} → {plan.ObjectKey}，不会访问 R2。"));
            return new MasterDataR2UploadResult(
                plan.ManifestPath,
                plan.DatabasePath,
                plan.ObjectKey,
                plan.Length,
                string.Empty,
                true,
                false,
                false);
        }

        ValidateCredentials(options);
        using var store = new R2S3ObjectStoreClient(
            options.Endpoint,
            options.Bucket,
            options.AccessKeyId!,
            options.SecretAccessKey!,
            options.SessionToken);

        progress?.Report(new MasterDataR2UploadProgress(
            "校验",
            0,
            plan.Length,
            "正在计算主数据 SHA-256…"));
        var sha256 = await ComputeSha256Async(plan, progress, cancellationToken);

        if (!options.Force)
        {
            progress?.Report(new MasterDataR2UploadProgress(
                "检查",
                plan.Length,
                plan.Length,
                $"正在检查远端对象：{plan.ObjectKey}"));
            var current = await ExecuteWithRetryAsync(
                ct => store.IsCurrentAsync(plan.ObjectKey, plan.Length, sha256, ct),
                options.MaxRetries,
                cancellationToken);
            if (current)
            {
                progress?.Report(new MasterDataR2UploadProgress(
                    "完成",
                    plan.Length,
                    plan.Length,
                    "远端对象已是最新版本，跳过上传。"));
                return new MasterDataR2UploadResult(
                    plan.ManifestPath,
                    plan.DatabasePath,
                    plan.ObjectKey,
                    plan.Length,
                    sha256,
                    false,
                    false,
                    true);
            }
        }

        progress?.Report(new MasterDataR2UploadProgress(
            "上传",
            0,
            plan.Length,
            $"正在上传：{plan.ObjectKey}"));
        await ExecuteWithRetryAsync(
            ct => store.PutAsync(plan, sha256, ct),
            options.MaxRetries,
            cancellationToken);
        progress?.Report(new MasterDataR2UploadProgress(
            "完成",
            plan.Length,
            plan.Length,
            "主数据上传完成。"));
        return new MasterDataR2UploadResult(
            plan.ManifestPath,
            plan.DatabasePath,
            plan.ObjectKey,
            plan.Length,
            sha256,
            false,
            true,
            false);
    }

    private static async Task<string> ComputeSha256Async(
        MasterDataR2UploadPlan plan,
        IProgress<MasterDataR2UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            plan.DatabasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long processed = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            processed += read;
            progress?.Report(new MasterDataR2UploadProgress(
                "校验",
                processed,
                plan.Length,
                $"正在计算 SHA-256：{processed:N0}/{plan.Length:N0} 字节"));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0;; attempt++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception exception) when (
                attempt < maxRetries &&
                exception is not OperationCanceledException &&
                IsRetryable(exception))
            {
                var seconds = Math.Min(30, Math.Pow(2, attempt + 1));
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
        }
    }

    private static async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> action,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        await ExecuteWithRetryAsync(
            async ct =>
            {
                await action(ct);
                return true;
            },
            maxRetries,
            cancellationToken);
    }

    private static bool IsRetryable(Exception exception) => exception switch
    {
        IOException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } =>
            (int)status == 408 || (int)status == 429 || (int)status >= 500,
        _ => false
    };

    private static void ValidateOptions(MasterDataR2UploadOptions options)
    {
        if (!Uri.TryCreate(options.Endpoint?.Trim(), UriKind.Absolute, out var endpoint) ||
            (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("R2 地址必须是 HTTP(S) 绝对地址。", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Bucket))
            throw new ArgumentException("R2 存储桶不能为空。", nameof(options));
        if (options.MaxRetries < 0)
            throw new ArgumentException("重试次数不能小于 0。", nameof(options));

        _ = NormalizePrefix(options.KeyPrefix);
    }

    private static void ValidateCredentials(MasterDataR2UploadOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessKeyId) ||
            string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            throw new InvalidOperationException(
                "缺少 R2 凭据，请填写访问密钥 ID 和秘密访问密钥，或设置 R2_ACCESS_KEY_ID、R2_SECRET_ACCESS_KEY 环境变量。");
        }
    }

    private static string RequireDirectory(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{description}：{value}");
        var path = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{description}：{path}");
        return path;
    }

    private static string BuildObjectKey(string relativeUri, string prefix)
    {
        if (string.IsNullOrWhiteSpace(relativeUri))
            throw new InvalidDataException("主数据清单中的 Uri 不能为空。");
        if (Uri.TryCreate(relativeUri, UriKind.Absolute, out _))
            throw new InvalidDataException($"主数据清单中的 Uri 不能是绝对地址：{relativeUri}");

        var segments = relativeUri.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
            throw new InvalidDataException($"主数据清单中的 Uri 无效：{relativeUri}");

        var key = relativeUri.StartsWith("master-data/production/", StringComparison.Ordinal)
            ? relativeUri
            : $"master-data/production/{relativeUri}";
        var normalizedPrefix = NormalizePrefix(prefix);
        return string.IsNullOrEmpty(normalizedPrefix) ? key : $"{normalizedPrefix}/{key}";
    }

    private static string NormalizePrefix(string? prefix)
    {
        var normalized = (prefix ?? string.Empty).Replace('\\', '/').Trim('/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("R2 对象前缀不能包含 . 或 ..。", nameof(prefix));
        }
        return normalized;
    }
}
