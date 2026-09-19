using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.Episodes.Protocol;
using Sirius.Toolbox.IO;

namespace Sirius.AssetTool.Episodes;

public sealed record EpisodePackResult(
    string OutputPath,
    string EpisodeId,
    int DetailCount,
    long ByteCount);

public sealed record EpisodeBatchItemResult(
    string InputPath,
    string? OutputPath,
    bool Succeeded,
    long ByteCount,
    string? Error);

public sealed record EpisodeBatchResult(
    IReadOnlyList<EpisodeBatchItemResult> Items,
    string OutputDirectory);

public sealed record EpisodeUnpackResult(string OutputPath, int DetailCount);

public sealed record EpisodeInspectResult(
    string InputPath,
    long ByteCount,
    int ReplacementCount,
    bool IsProbablyCorrupt,
    bool Decoded,
    int DetailCount,
    string? Error);

public sealed class EpisodeToolService
{
    public EpisodePackResult Pack(string inputPath, string? outputPath, bool overwrite)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
            throw new FileNotFoundException("找不到输入 JSON。", fullInputPath);

        var fullOutputPath = Path.GetFullPath(outputPath ?? Path.ChangeExtension(fullInputPath, ".bin"));
        EnsureCanWrite(fullOutputPath, overwrite);

        var episode = EpisodeCodec.ReadJson(fullInputPath);
        var bytes = EpisodeCodec.Pack(episode.Details);
        EpisodeCodec.Verify(bytes, episode.Details);
        AtomicFile.WriteAllBytes(fullOutputPath, bytes);

        return new EpisodePackResult(fullOutputPath, episode.EpisodeId.ToString(), episode.Details.Length, bytes.Length);
    }

    public EpisodeBatchResult PackDirectory(string inputDirectory, string? outputDirectory, bool overwrite)
    {
        var fullInputDirectory = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(fullInputDirectory))
            throw new DirectoryNotFoundException($"找不到输入目录：{fullInputDirectory}");

        var fullOutputDirectory = Path.GetFullPath(outputDirectory
            ?? (fullInputDirectory.TrimEnd(Path.DirectorySeparatorChar) + "-bin"));
        var files = Directory.EnumerateFiles(fullInputDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidDataException("输入目录中没有 JSON 文件。");

        var items = new List<EpisodeBatchItemResult>(files.Length);
        foreach (var inputPath in files)
        {
            var relative = Path.GetRelativePath(fullInputDirectory, inputPath);
            var outputPath = Path.Combine(fullOutputDirectory, Path.ChangeExtension(relative, ".bin"));
            try
            {
                EnsureCanWrite(outputPath, overwrite);
                var episode = EpisodeCodec.ReadJson(inputPath);
                var bytes = EpisodeCodec.Pack(episode.Details);
                EpisodeCodec.Verify(bytes, episode.Details);
                AtomicFile.WriteAllBytes(outputPath, bytes);
                items.Add(new EpisodeBatchItemResult(inputPath, outputPath, true, bytes.Length, null));
            }
            catch (Exception exception)
            {
                items.Add(new EpisodeBatchItemResult(inputPath, outputPath, false, 0, exception.Message));
            }
        }

        return new EpisodeBatchResult(items, fullOutputDirectory);
    }

    public EpisodeUnpackResult Unpack(string inputPath, string? outputPath, bool overwrite)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
            throw new FileNotFoundException("找不到输入 BIN。", fullInputPath);

        var fullOutputPath = Path.GetFullPath(outputPath ?? Path.ChangeExtension(fullInputPath, ".json"));
        EnsureCanWrite(fullOutputPath, overwrite);
        var bytes = File.ReadAllBytes(fullInputPath);
        ThrowIfProbablyTextCorrupted(bytes);
        var details = EpisodeCodec.Unpack(bytes);
        EpisodeCodec.WriteUnpackedJson(fullOutputPath, details);
        return new EpisodeUnpackResult(fullOutputPath, details.Length);
    }

    public EpisodeBatchResult UnpackDirectory(string inputDirectory, string? outputDirectory, bool overwrite)
    {
        var fullInputDirectory = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(fullInputDirectory))
            throw new DirectoryNotFoundException($"找不到输入目录：{fullInputDirectory}");

        var fullOutputDirectory = Path.GetFullPath(outputDirectory
            ?? (fullInputDirectory.TrimEnd(Path.DirectorySeparatorChar) + "-json"));
        var files = Directory.EnumerateFiles(fullInputDirectory, "*.bin", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidDataException("输入目录中没有 BIN 文件。");

        var items = new List<EpisodeBatchItemResult>(files.Length);
        foreach (var inputPath in files)
        {
            var relative = Path.GetRelativePath(fullInputDirectory, inputPath);
            var outputPath = Path.Combine(fullOutputDirectory, Path.ChangeExtension(relative, ".json"));
            try
            {
                EnsureCanWrite(outputPath, overwrite);
                var bytes = File.ReadAllBytes(inputPath);
                ThrowIfProbablyTextCorrupted(bytes);
                var details = EpisodeCodec.Unpack(bytes);
                EpisodeCodec.WriteUnpackedJson(outputPath, details);
                items.Add(new EpisodeBatchItemResult(inputPath, outputPath, true, bytes.Length, null));
            }
            catch (Exception exception)
            {
                items.Add(new EpisodeBatchItemResult(inputPath, outputPath, false, 0, exception.Message));
            }
        }

        return new EpisodeBatchResult(items, fullOutputDirectory);
    }

    public EpisodeInspectResult Inspect(string inputPath)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
            throw new FileNotFoundException("找不到输入 BIN。", fullInputPath);

        var bytes = File.ReadAllBytes(fullInputPath);
        var replacements = BinaryDiagnostics.CountUtf8ReplacementSequences(bytes);
        var isProbablyCorrupt = BinaryDiagnostics.IsProbablyUtf8TextCorrupted(bytes);
        if (isProbablyCorrupt)
        {
            return new EpisodeInspectResult(
                fullInputPath,
                bytes.Length,
                replacements,
                true,
                false,
                0,
                "文件很可能曾被当作 UTF-8 文本读取并重新保存，原始二进制字节已丢失。");
        }

        try
        {
            var details = EpisodeCodec.Unpack(bytes);
            return new EpisodeInspectResult(
                fullInputPath,
                bytes.Length,
                replacements,
                false,
                true,
                details.Length,
                null);
        }
        catch (Exception exception)
        {
            return new EpisodeInspectResult(
                fullInputPath,
                bytes.Length,
                replacements,
                false,
                false,
                0,
                exception.GetBaseException().Message);
        }
    }

    private static void ThrowIfProbablyTextCorrupted(byte[] bytes)
    {
        var replacements = BinaryDiagnostics.CountUtf8ReplacementSequences(bytes);
        if (BinaryDiagnostics.IsProbablyUtf8TextCorrupted(bytes))
        {
            throw new InvalidDataException(
                $"检测到 {replacements} 个 EF BF BD 替换序列。该文件不是原始抓包二进制，无法可靠恢复；请重新以 byte[]/二进制模式保存响应。");
        }
    }

    private static void EnsureCanWrite(string outputPath, bool overwrite)
    {
        if (File.Exists(outputPath) && !overwrite)
            throw new IOException($"输出文件已存在：{outputPath}。请启用覆盖选项。");
    }
}
