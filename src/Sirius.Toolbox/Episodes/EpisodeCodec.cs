using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sirius.Toolbox.Episodes.Protocol;
using Sirius.Toolbox.IO;

namespace Sirius.Toolbox.Episodes;

public static class EpisodeCodec
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static EpisodeInput ReadJson(string path)
    {
        var json = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        using var root = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        EpisodeJsonDocument? wrapper = null;
        EpisodeDetailResult[]? details;

        if (root.RootElement.ValueKind == JsonValueKind.Array)
        {
            details = JsonSerializer.Deserialize<EpisodeDetailResult[]>(json, InputJsonOptions);
        }
        else if (root.RootElement.ValueKind == JsonValueKind.Object)
        {
            wrapper = JsonSerializer.Deserialize<EpisodeJsonDocument>(json, InputJsonOptions);
            details = wrapper?.EpisodeDetail;
        }
        else
        {
            throw new InvalidDataException("JSON 根节点必须是 episode 包装对象或 EpisodeDetail 数组。");
        }

        if (details is null)
        {
            throw new InvalidDataException("JSON 中没有可用的 EpisodeDetail 数组。");
        }

        Validate(details, wrapper?.EpisodeId ?? 0);
        return new EpisodeInput(wrapper, details);
    }

    public static byte[] Pack(EpisodeDetailResult[] details)
        => EpisodeMessagePack.Serialize(details, lz4: true);

    public static EpisodeDetailResult[] Unpack(byte[] bytes)
        => EpisodeMessagePack.Deserialize<EpisodeDetailResult[]>(bytes, lz4: true);

    public static void Verify(byte[] bytes, EpisodeDetailResult[] expected)
    {
        EpisodeDetailResult[] decoded;
        try
        {
            decoded = Unpack(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("生成结果无法用 episode MessagePack 的 LZ4BlockArray 配置反序列化。", ex);
        }

        if (decoded.Length != expected.Length)
        {
            throw new InvalidDataException($"往返校验失败：输入 {expected.Length} 条，解码后 {decoded.Length} 条。");
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (decoded[i].Id != expected[i].Id ||
                decoded[i].EpisodeMasterId != expected[i].EpisodeMasterId ||
                decoded[i].GroupOrder != expected[i].GroupOrder ||
                !string.Equals(decoded[i].Phrase, expected[i].Phrase, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"往返校验失败：第 {i + 1} 条剧情记录发生变化。" );
            }

            var expectedMotionCount = expected[i].CharacterMotions?.Length ?? 0;
            var decodedMotionCount = decoded[i].CharacterMotions?.Length ?? 0;
            if (expectedMotionCount != decodedMotionCount)
            {
                throw new InvalidDataException($"往返校验失败：第 {i + 1} 条角色动作数量发生变化。" );
            }
        }
    }

    public static void WriteUnpackedJson(string outputPath, EpisodeDetailResult[] details)
    {
        var episodeId = details.Length == 0 ? 0 : details[0].EpisodeMasterId;
        var output = new
        {
            EpisodeId = episodeId,
            EpisodeDetail = details
        };

        var json = JsonSerializer.Serialize(output, OutputJsonOptions);
        AtomicFile.WriteAllText(outputPath, json + Environment.NewLine);
    }

    private static void Validate(EpisodeDetailResult[] details, long wrapperEpisodeId)
    {
        for (var i = 0; i < details.Length; i++)
        {
            var detail = details[i] ?? throw new InvalidDataException($"EpisodeDetail[{i}] 为 null。");

            if (detail.CharacterMotions is null)
            {
                throw new InvalidDataException($"EpisodeDetail[{i}].CharacterMotions 缺失。客户端模型要求该字段存在。" );
            }

            if (wrapperEpisodeId != 0 && detail.EpisodeMasterId != wrapperEpisodeId)
            {
                throw new InvalidDataException(
                    $"EpisodeDetail[{i}].EpisodeMasterId={detail.EpisodeMasterId} 与外层 EpisodeId={wrapperEpisodeId} 不一致。" );
            }
        }
    }
}

public sealed class EpisodeInput
{
    public EpisodeInput(EpisodeJsonDocument? wrapper, EpisodeDetailResult[] details)
    {
        Wrapper = wrapper;
        Details = details;
    }

    public EpisodeJsonDocument? Wrapper { get; }
    public EpisodeDetailResult[] Details { get; }

    public long EpisodeId
    {
        get
        {
            if (Wrapper is { EpisodeId: > 0 })
            {
                return Wrapper.EpisodeId;
            }

            return Details.FirstOrDefault()?.EpisodeMasterId ?? 0;
        }
    }
}
