namespace Sirius.AssetTool.Episodes;

internal static class EpisodeCommands
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "pack" => PackCommand(args[1..]),
            "pack-dir" => PackDirectoryCommand(args[1..]),
            "unpack" => UnpackCommand(args[1..]),
            "unpack-dir" => UnpackDirectoryCommand(args[1..]),
            "inspect" => InspectCommand(args[1..]),
            "cache" => CacheCommand(args[1..]),
            _ => Fail($"未知命令：{args[0]}")
        };
    }

    private static int PackCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("pack 需要输入 JSON 文件。");
        }

        var outputPath = GetOption(args, "-o", "--output");
        var force = HasOption(args, "-f", "--force");

        var result = new EpisodeToolService().Pack(args[0], outputPath, force);
        Console.WriteLine($"已生成：{result.OutputPath}");
        Console.WriteLine($"EpisodeId：{result.EpisodeId}");
        Console.WriteLine($"剧情记录：{result.DetailCount}");
        Console.WriteLine($"文件大小：{result.ByteCount} bytes");
        Console.WriteLine("往返校验：通过");
        return 0;
    }

    private static int PackDirectoryCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("pack-dir 需要输入目录。");
        }

        var outputDirectoryOption = GetOption(args, "-o", "--output");
        var force = HasOption(args, "-f", "--force");

        var result = new EpisodeToolService().PackDirectory(args[0], outputDirectoryOption, force);
        foreach (var item in result.Items)
        {
            var relativeInput = Path.GetRelativePath(Path.GetFullPath(args[0]), item.InputPath);
            if (item.Succeeded)
            {
                var relativeOutput = Path.GetRelativePath(result.OutputDirectory, item.OutputPath!);
                Console.WriteLine($"[OK] {relativeInput} -> {relativeOutput} ({item.ByteCount} bytes)");
            }
            else
            {
                Console.Error.WriteLine($"[FAIL] {relativeInput}: {item.Error}");
            }
        }

        var succeeded = result.Items.Count(item => item.Succeeded);
        var failed = result.Items.Count - succeeded;
        Console.WriteLine($"完成：成功 {succeeded}，失败 {failed}，输出目录 {result.OutputDirectory}");
        return failed == 0 ? 0 : 2;
    }

    private static int UnpackCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("unpack 需要输入 BIN 文件。");
        }

        var outputPathOption = GetOption(args, "-o", "--output");
        var force = HasOption(args, "-f", "--force");

        var result = new EpisodeToolService().Unpack(args[0], outputPathOption, force);
        Console.WriteLine($"已解包：{result.OutputPath}");
        Console.WriteLine($"剧情记录：{result.DetailCount}");
        return 0;
    }

    private static int InspectCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("inspect 需要输入 BIN 文件。");
        }

        var result = new EpisodeToolService().Inspect(args[0]);
        Console.WriteLine($"文件：{result.InputPath}");
        Console.WriteLine($"大小：{result.ByteCount} bytes");
        Console.WriteLine($"EF BF BD 替换序列：{result.ReplacementCount}");
        if (result.IsProbablyCorrupt)
        {
            Console.WriteLine($"判断：{result.Error}");
            return 2;
        }
        if (!result.Decoded)
        {
            Console.WriteLine($"反序列化：失败（{result.Error}）");
            return 2;
        }
        Console.WriteLine("反序列化：成功");
        Console.WriteLine($"剧情记录：{result.DetailCount}");
        return 0;
    }

    private static int UnpackDirectoryCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("unpack-dir 需要输入目录。");
        }

        var outputDirectory = GetOption(args, "-o", "--output");
        var force = HasOption(args, "-f", "--force");
        var result = new EpisodeToolService().UnpackDirectory(args[0], outputDirectory, force);
        foreach (var item in result.Items)
        {
            var relativeInput = Path.GetRelativePath(Path.GetFullPath(args[0]), item.InputPath);
            if (item.Succeeded)
            {
                var relativeOutput = Path.GetRelativePath(result.OutputDirectory, item.OutputPath!);
                Console.WriteLine($"[OK] {relativeInput} -> {relativeOutput} ({item.ByteCount} bytes)");
            }
            else
            {
                Console.Error.WriteLine($"[FAIL] {relativeInput}: {item.Error}");
            }
        }

        var succeeded = result.Items.Count(item => item.Succeeded);
        var failed = result.Items.Count - succeeded;
        Console.WriteLine($"完成：成功 {succeeded}，失败 {failed}，输出目录 {result.OutputDirectory}");
        return failed == 0 ? 0 : 2;
    }

    private static int CacheCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("cache 需要剧情 JSON 目录和场景 BIN 目录。");
        }

        var outputPath = GetOption(args, "-o", "--output")
            ?? (args.Length > 2 && !args[2].StartsWith("-", StringComparison.Ordinal) ? args[2] : "scene-assets.json");
        var masterDataVersion = GetOption(args, "--master-data-version");
        var sourceRevision = GetOption(args, "--source-revision");
        var episodePrefix = GetOption(args, "--episode-prefix") ?? "episode";
        var scenePrefix = GetOption(args, "--scene-prefix") ?? "scenes";
        var metadataOnly = HasOption(args, "--metadata-only");
        var force = HasOption(args, "-f", "--force");

        var result = new SceneAssetCacheService().Build(
            args[0],
            args[1],
            outputPath,
            new SceneAssetCacheOptions(metadataOnly, masterDataVersion, sourceRevision, episodePrefix, scenePrefix),
            force);
        Console.WriteLine($"已生成：{result.OutputPath}");
        Console.WriteLine($"资源数量：{result.AssetCount}");
        Console.WriteLine($"匹配 JSON：{result.MatchedJsonCount}");
        Console.WriteLine($"缺失 JSON：{result.MissingJsonCount}");
        Console.WriteLine($"总大小：{result.TotalBytes} bytes");
        return 0;
    }

    private static string? GetOption(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"选项 {args[i]} 缺少值。" );
            }
            return args[i + 1];
        }
        return null;
    }

    private static bool HasOption(string[] args, params string[] names)
        => args.Any(arg => names.Contains(arg, StringComparer.OrdinalIgnoreCase));

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("运行 --help 查看用法。");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Sirius.AssetTool episode");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  Sirius.AssetTool episode pack <episode.json> [-o output.bin] [--force]");
        Console.WriteLine("  Sirius.AssetTool episode pack-dir <episode目录> [-o output目录] [--force]");
        Console.WriteLine("  Sirius.AssetTool episode unpack <scene.bin> [-o output.json] [--force]");
        Console.WriteLine("  Sirius.AssetTool episode unpack-dir <bin目录> [-o output目录] [--force]");
        Console.WriteLine("  Sirius.AssetTool episode inspect <scene.bin>");
        Console.WriteLine("  Sirius.AssetTool episode cache <episode-json目录> <scene-bin目录> [-o scene-assets.json] [选项]");
        Console.WriteLine();
        Console.WriteLine("  cache 选项：--metadata-only --master-data-version <版本> --source-revision <修订号> --force");
        Console.WriteLine();
        Console.WriteLine("pack 只序列化 JSON 中的 EpisodeDetail 数组；外层 EpisodeId/Title/Prev/Next 不进入 scenes BIN。" );
    }
}
