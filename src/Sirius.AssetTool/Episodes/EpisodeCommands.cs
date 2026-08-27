using Sirius.Tooling.Core.Episodes;
using Sirius.Tooling.Core.Episodes.Protocol;
using Sirius.Tooling.Core.IO;

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
            "inspect" => InspectCommand(args[1..]),
            _ => Fail($"未知命令：{args[0]}")
        };
    }

    private static int PackCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("pack 需要输入 JSON 文件。");
        }

        var inputPath = Path.GetFullPath(args[0]);
        var outputPath = GetOption(args, "-o", "--output");
        var force = HasOption(args, "-f", "--force");

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("找不到输入 JSON。", inputPath);
        }

        var episode = EpisodeCodec.ReadJson(inputPath);
        outputPath = outputPath is null
            ? Path.ChangeExtension(inputPath, ".bin")
            : Path.GetFullPath(outputPath);

        EnsureCanWrite(outputPath, force);
        var bytes = EpisodeCodec.Pack(episode.Details);
        EpisodeCodec.Verify(bytes, episode.Details);
        AtomicFile.WriteAllBytes(outputPath, bytes);

        Console.WriteLine($"已生成：{outputPath}");
        Console.WriteLine($"EpisodeId：{episode.EpisodeId}");
        Console.WriteLine($"剧情记录：{episode.Details.Length}");
        Console.WriteLine($"文件大小：{bytes.Length} bytes");
        Console.WriteLine("往返校验：通过");
        return 0;
    }

    private static int PackDirectoryCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("pack-dir 需要输入目录。");
        }

        var inputDirectory = Path.GetFullPath(args[0]);
        var outputDirectoryOption = GetOption(args, "-o", "--output");
        var outputDirectory = Path.GetFullPath(outputDirectoryOption ?? (inputDirectory.TrimEnd(Path.DirectorySeparatorChar) + "-bin"));
        var force = HasOption(args, "-f", "--force");

        if (!Directory.Exists(inputDirectory))
        {
            throw new DirectoryNotFoundException($"找不到输入目录：{inputDirectory}");
        }

        var files = Directory.EnumerateFiles(inputDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidDataException("输入目录中没有 JSON 文件。");
        }

        var succeeded = 0;
        var failed = 0;
        foreach (var inputPath in files)
        {
            try
            {
                var relative = Path.GetRelativePath(inputDirectory, inputPath);
                var outputPath = Path.Combine(outputDirectory, Path.ChangeExtension(relative, ".bin"));
                EnsureCanWrite(outputPath, force);

                var episode = EpisodeCodec.ReadJson(inputPath);
                var bytes = EpisodeCodec.Pack(episode.Details);
                EpisodeCodec.Verify(bytes, episode.Details);
                AtomicFile.WriteAllBytes(outputPath, bytes);
                succeeded++;
                Console.WriteLine($"[OK] {relative} -> {Path.GetRelativePath(outputDirectory, outputPath)} ({bytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[FAIL] {Path.GetRelativePath(inputDirectory, inputPath)}: {ex.Message}");
            }
        }

        Console.WriteLine($"完成：成功 {succeeded}，失败 {failed}，输出目录 {outputDirectory}");
        return failed == 0 ? 0 : 2;
    }

    private static int UnpackCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("unpack 需要输入 BIN 文件。");
        }

        var inputPath = Path.GetFullPath(args[0]);
        var outputPathOption = GetOption(args, "-o", "--output");
        var outputPath = Path.GetFullPath(outputPathOption ?? Path.ChangeExtension(inputPath, ".json"));
        var force = HasOption(args, "-f", "--force");

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("找不到输入 BIN。", inputPath);
        }

        EnsureCanWrite(outputPath, force);
        var bytes = File.ReadAllBytes(inputPath);
        ThrowIfProbablyTextCorrupted(bytes);
        var details = EpisodeCodec.Unpack(bytes);
        EpisodeCodec.WriteUnpackedJson(outputPath, details);

        Console.WriteLine($"已解包：{outputPath}");
        Console.WriteLine($"剧情记录：{details.Length}");
        return 0;
    }

    private static int InspectCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("inspect 需要输入 BIN 文件。");
        }

        var inputPath = Path.GetFullPath(args[0]);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("找不到输入 BIN。", inputPath);
        }

        var bytes = File.ReadAllBytes(inputPath);
        var replacements = BinaryDiagnostics.CountUtf8ReplacementSequences(bytes);
        Console.WriteLine($"文件：{inputPath}");
        Console.WriteLine($"大小：{bytes.Length} bytes");
        Console.WriteLine($"EF BF BD 替换序列：{replacements}");

        if (BinaryDiagnostics.IsProbablyUtf8TextCorrupted(bytes))
        {
            Console.WriteLine("判断：文件很可能曾被当作 UTF-8 文本读取并重新保存，原始二进制字节已丢失。" );
            return 2;
        }

        EpisodeDetailResult[] details;
        try
        {
            details = EpisodeCodec.Unpack(bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"反序列化：失败（{ex.GetBaseException().Message}）");
            return 2;
        }

        Console.WriteLine("反序列化：成功");
        Console.WriteLine($"剧情记录：{details.Length}");
        if (details.Length > 0)
        {
            Console.WriteLine($"EpisodeMasterId：{details[0].EpisodeMasterId}");
            Console.WriteLine($"Detail Id 范围：{details[0].Id} .. {details[^1].Id}");
        }
        return 0;
    }

    private static void ThrowIfProbablyTextCorrupted(byte[] bytes)
    {
        var replacements = BinaryDiagnostics.CountUtf8ReplacementSequences(bytes);
        if (BinaryDiagnostics.IsProbablyUtf8TextCorrupted(bytes))
        {
            throw new InvalidDataException(
                $"检测到 {replacements} 个 EF BF BD 替换序列。该文件不是原始抓包二进制，无法可靠恢复；请重新以 byte[]/二进制模式保存响应。" );
        }
    }

    private static void EnsureCanWrite(string outputPath, bool force)
    {
        if (File.Exists(outputPath) && !force)
        {
            throw new IOException($"输出文件已存在：{outputPath}。使用 --force 覆盖。" );
        }
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
        Console.WriteLine("  Sirius.AssetTool episode inspect <scene.bin>");
        Console.WriteLine();
        Console.WriteLine("pack 只序列化 JSON 中的 EpisodeDetail 数组；外层 EpisodeId/Title/Prev/Next 不进入 scenes BIN。" );
    }
}
