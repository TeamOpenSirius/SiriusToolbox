using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.Episodes.Protocol;
using Sirius.Toolbox.IO;

namespace Sirius.Toolbox.Episodes;

public sealed class EpisodeEditorDocument
{
    internal EpisodeEditorDocument(bool isWrapper, JsonObject? wrapperNode, List<EpisodeDetailResult> details)
    {
        IsWrapper = isWrapper;
        WrapperNode = wrapperNode;
        Details = details;
    }

    public bool IsWrapper { get; }

    public List<EpisodeDetailResult> Details { get; }

    internal JsonObject? WrapperNode { get; }
}

public sealed record EpisodeEditorSaveResult(string OutputPath, int DetailCount);

public sealed record EpisodeEditorPackResult(string OutputPath, int DetailCount, long ByteCount);

public sealed class EpisodeEditorService
{
    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EpisodeEditorDocument Load(string inputPath)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
            throw new FileNotFoundException("找不到剧情 JSON。", fullInputPath);

        var json = File.ReadAllText(fullInputPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        using var root = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var input = EpisodeCodec.ReadJson(fullInputPath);
        var details = input.Details.ToList();

        return root.RootElement.ValueKind switch
        {
            JsonValueKind.Array => new EpisodeEditorDocument(false, null, details),
            JsonValueKind.Object => new EpisodeEditorDocument(true, ParseWrapper(json), details),
            _ => throw new InvalidDataException("JSON 根节点必须是剧情包装对象或 EpisodeDetail 数组。")
        };
    }

    public EpisodeEditorSaveResult Save(EpisodeEditorDocument document, string outputPath, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fullOutputPath = Path.GetFullPath(outputPath);
        EnsureCanWrite(fullOutputPath, overwrite);

        JsonNode outputNode;
        if (document.IsWrapper)
        {
            if (document.WrapperNode is null)
                throw new InvalidDataException("剧情包装对象内容已丢失，无法保存。请重新打开文件。");

            outputNode = document.WrapperNode.DeepClone();
            var wrapper = outputNode.AsObject();
            var detailPropertyName = wrapper
                .Select(static property => property.Key)
                .FirstOrDefault(static name => string.Equals(name, "EpisodeDetail", StringComparison.OrdinalIgnoreCase))
                ?? "EpisodeDetail";
            wrapper[detailPropertyName] = JsonSerializer.SerializeToNode(document.Details, OutputJsonOptions)
                ?? throw new InvalidDataException("无法序列化 EpisodeDetail。");
        }
        else
        {
            outputNode = JsonSerializer.SerializeToNode(document.Details, OutputJsonOptions)
                ?? throw new InvalidDataException("无法序列化 EpisodeDetail。");
        }

        AtomicFile.WriteAllText(fullOutputPath, outputNode.ToJsonString(OutputJsonOptions) + Environment.NewLine);
        return new EpisodeEditorSaveResult(fullOutputPath, document.Details.Count);
    }

    public EpisodeEditorPackResult Pack(EpisodeEditorDocument document, string outputPath, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fullOutputPath = Path.GetFullPath(outputPath);
        EnsureCanWrite(fullOutputPath, overwrite);
        var details = document.Details.ToArray();
        var bytes = EpisodeCodec.Pack(details);
        EpisodeCodec.Verify(bytes, details);
        AtomicFile.WriteAllBytes(fullOutputPath, bytes);
        return new EpisodeEditorPackResult(fullOutputPath, details.Length, bytes.Length);
    }

    private static JsonObject ParseWrapper(string json)
    {
        var node = JsonNode.Parse(json, nodeOptions: null, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        if (node is not JsonObject wrapper)
            throw new InvalidDataException("剧情包装 JSON 不是对象。");

        return wrapper;
    }

    private static void EnsureCanWrite(string outputPath, bool overwrite)
    {
        if (File.Exists(outputPath) && !overwrite)
            throw new IOException($"输出文件已存在：{outputPath}。请启用覆盖选项。");
    }
}
