namespace Sirius.AssetTool.R2;

internal static class R2Commands
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].Equals("cdn", StringComparison.OrdinalIgnoreCase)
            ? "sync"
            : args[0].ToLowerInvariant();
        if (command is not ("masterdata" or "sync"))
            return Fail($"未知 R2 命令：{args[0]}");
        if (args.Length < 2 || IsHelp(args[1]))
        {
            PrintHelp();
            return args.Length < 2 ? 1 : 0;
        }
        if (args[2..].Any(IsHelp))
        {
            PrintHelp();
            return 0;
        }

        var parsed = Parse(args[1], args[2..]);
        if (command == "masterdata")
            return await RunMasterDataAsync(parsed);
        return await RunSyncAsync(parsed);
    }

    private static async Task<int> RunMasterDataAsync(ParsedOptions parsed)
    {
        var options = new MasterDataR2UploadOptions(parsed.RootDirectory)
        {
            Endpoint = parsed.Endpoint,
            Bucket = parsed.Bucket,
            KeyPrefix = parsed.Prefix,
            MaxRetries = parsed.Retries,
            Force = parsed.Force,
            DryRun = parsed.DryRun,
            AccessKeyId = parsed.AccessKeyId,
            SecretAccessKey = parsed.SecretAccessKey,
            SessionToken = parsed.SessionToken
        };
        var lastStage = string.Empty;
        var progress = new Progress<MasterDataR2UploadProgress>(item =>
        {
            if (!string.Equals(lastStage, item.Stage, StringComparison.Ordinal))
            {
                lastStage = item.Stage;
                Console.WriteLine(item.Message);
            }
        });
        var result = await new MasterDataR2UploadService().UploadAsync(options, progress);
        Console.WriteLine($"对象键：{result.ObjectKey}");
        Console.WriteLine($"文件大小：{result.ByteCount:N0} bytes");
        if (result.DryRun)
            Console.WriteLine("预览完成：没有计算哈希，也没有发送 R2 请求。");
        else if (result.Skipped)
            Console.WriteLine($"已跳过：远端对象已是最新版本（SHA-256：{result.Sha256}）。");
        else
            Console.WriteLine($"已上传：SHA-256 {result.Sha256}");
        return 0;
    }

    private static async Task<int> RunSyncAsync(ParsedOptions parsed)
    {
        var options = new R2SyncOptions(parsed.RootDirectory)
        {
            Endpoint = parsed.Endpoint,
            Bucket = parsed.Bucket,
            KeyPrefix = parsed.Prefix,
            Concurrency = parsed.Concurrency,
            MaxRetries = parsed.Retries,
            Force = parsed.Force,
            DryRun = parsed.DryRun,
            AccessKeyId = parsed.AccessKeyId,
            SecretAccessKey = parsed.SecretAccessKey,
            SessionToken = parsed.SessionToken
        };
        var lastReportedCompleted = -1;
        var progress = new Progress<R2SyncProgress>(item =>
        {
            var shouldReport = item.Completed == item.Total ||
                               item.Completed != lastReportedCompleted &&
                               (item.Completed == 0 || item.Completed % Math.Max(1, parsed.Concurrency) == 0);
            if (shouldReport)
            {
                lastReportedCompleted = item.Completed;
                Console.WriteLine(item.Message);
            }
        });
        var result = await new R2AssetSyncService().SyncAsync(options, progress);
        Console.WriteLine($"对象数量：{result.ObjectCount}");
        Console.WriteLine($"总大小：{result.TotalBytes:N0} bytes");
        if (result.DryRun)
        {
            Console.WriteLine($"预览完成：映射清单 {result.MappingManifestPath}");
            Console.WriteLine("预览模式不会计算哈希，也不会发送 R2 请求。");
        }
        else
        {
            Console.WriteLine($"同步完成：上传 {result.UploadedCount}，跳过 {result.SkippedCount}，哈希缓存 {result.CachedHashCount}，已发送 {result.UploadedBytes:N0} bytes");
            Console.WriteLine($"哈希缓存：{result.HashCachePath}");
        }
        return 0;
    }

    private static ParsedOptions Parse(string rootDirectory, string[] args)
    {
        var endpoint = MasterDataR2UploadDefaults.Endpoint;
        var bucket = MasterDataR2UploadDefaults.Bucket;
        var prefix = string.Empty;
        var concurrency = 16;
        var retries = MasterDataR2UploadDefaults.MaxRetries;
        var force = false;
        var dryRun = false;
        var accessKeyId = Environment.GetEnvironmentVariable("R2_ACCESS_KEY_ID");
        var secretAccessKey = Environment.GetEnvironmentVariable("R2_SECRET_ACCESS_KEY");
        var sessionToken = Environment.GetEnvironmentVariable("R2_SESSION_TOKEN");

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--endpoint":
                case "--r2-endpoint":
                    endpoint = NextValue(args, ref index);
                    break;
                case "--bucket":
                case "--r2-bucket":
                    bucket = NextValue(args, ref index);
                    break;
                case "--prefix":
                case "--r2-prefix":
                    prefix = NextValue(args, ref index);
                    break;
                case "--concurrency":
                case "--r2-concurrency":
                    concurrency = int.Parse(NextValue(args, ref index));
                    break;
                case "--retries":
                case "--r2-retries":
                    retries = int.Parse(NextValue(args, ref index));
                    break;
                case "--access-key-id":
                    accessKeyId = NextValue(args, ref index);
                    break;
                case "--secret-access-key":
                    secretAccessKey = NextValue(args, ref index);
                    break;
                case "--session-token":
                    sessionToken = NextValue(args, ref index);
                    break;
                case "--force":
                case "--r2-force":
                    force = true;
                    break;
                case "--dry-run":
                case "--r2-dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"未知选项：{args[index]}");
            }
        }

        return new ParsedOptions(rootDirectory, endpoint, bucket, prefix, concurrency, retries, force, dryRun, accessKeyId, secretAccessKey, sessionToken);
    }

    private static string NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"选项 {args[index]} 缺少值。");
        return args[++index];
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("运行 Sirius.AssetTool r2 --help 查看用法。");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Sirius.AssetTool R2 同步工具");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  Sirius.AssetTool r2 sync <输出目录> [选项]");
        Console.WriteLine("  Sirius.AssetTool r2 cdn <输出目录> [选项]（sync 的别名）");
        Console.WriteLine("  Sirius.AssetTool r2 masterdata <输出目录> [选项]（旧脚本兼容入口）");
        Console.WriteLine();
        Console.WriteLine("sync 会在同一次操作中同步 master、assets\\catalogs 和 assets\\files，并维护 assets\\r2-hash-cache.json。");
        Console.WriteLine("选项：");
        Console.WriteLine("  --endpoint <地址>           R2 S3 兼容接口地址");
        Console.WriteLine("  --bucket <名称>              存储桶名称");
        Console.WriteLine("  --prefix <路径>              对象键前缀");
        Console.WriteLine("  --concurrency <数量>         并发上传数（默认 16）");
        Console.WriteLine("  --retries <次数>             单次请求失败后的重试次数（默认 5）");
        Console.WriteLine("  --force                      不检查远端版本，直接上传");
        Console.WriteLine("  --dry-run                    生成 assets\\r2-object-map.tsv，不访问 R2");
        Console.WriteLine("  --access-key-id <值>         访问密钥 ID（也可使用环境变量）");
        Console.WriteLine("  --secret-access-key <值>     秘密访问密钥（也可使用环境变量）");
        Console.WriteLine("  --session-token <值>         临时凭据令牌");
        Console.WriteLine();
        Console.WriteLine("环境变量：R2_ACCESS_KEY_ID、R2_SECRET_ACCESS_KEY、R2_SESSION_TOKEN");
        Console.WriteLine("兼容旧参数名：--r2-endpoint --r2-bucket --r2-prefix --r2-concurrency --r2-retries --r2-force --r2-dry-run");
        Console.WriteLine("兼容旧入口：--r2-sync --dir <输出目录> [--r2-*]");
    }

    private sealed record ParsedOptions(
        string RootDirectory,
        string Endpoint,
        string Bucket,
        string Prefix,
        int Concurrency,
        int Retries,
        bool Force,
        bool DryRun,
        string? AccessKeyId,
        string? SecretAccessKey,
        string? SessionToken);
}
