using System.Text;
using Sirius.AssetTool.Charts.Conversion;
using Sirius.AssetTool.Charts.Sirius;
using SusParser = Sirius.AssetTool.Charts.Sus.SusParser;

namespace Sirius.AssetTool.Charts;

public sealed record ChartConvertOptions(bool IgnoreWaveOffset, bool Strict);

public sealed record ChartTextResult(
    string OutputPath,
    int NoteCount,
    IReadOnlyList<string> Warnings);

public sealed record ChartEncodeResult(
    string OutputPath,
    string? TextOutputPath,
    int NoteCount,
    IReadOnlyList<string> Warnings);

public sealed record ChartDecodeResult(string OutputPath);

public sealed record ChartSelfTestResult(int EncodedLength);

public sealed class ChartToolService
{
    public ChartTextResult ConvertToText(
        string inputPath,
        string outputPath,
        ChartConvertOptions options)
    {
        var result = Convert(inputPath, options);
        var text = BuildChartText(result.Notes);
        File.WriteAllText(outputPath, text, new UTF8Encoding(false));
        return new ChartTextResult(outputPath, result.Notes.Count, result.Warnings);
    }

    public ChartEncodeResult Encode(
        string inputPath,
        string outputPath,
        string key,
        string? textOutputPath,
        ChartConvertOptions options)
    {
        var result = Convert(inputPath, options);
        var text = BuildChartText(result.Notes);
        File.WriteAllBytes(outputPath, SiriusChartCrypto.EncodeChart(text, key));
        if (textOutputPath is not null)
            File.WriteAllText(textOutputPath, text, new UTF8Encoding(false));

        return new ChartEncodeResult(outputPath, textOutputPath, result.Notes.Count, result.Warnings);
    }

    public ChartDecodeResult Decode(string inputPath, string outputPath, string key)
    {
        var text = SiriusChartCrypto.DecodeChart(File.ReadAllBytes(inputPath), key);
        File.WriteAllText(outputPath, text, new UTF8Encoding(false));
        return new ChartDecodeResult(outputPath);
    }

    public ChartSelfTestResult SelfTest(string key = "0123456789abcdef0123456789abcdef")
    {
        const string source = "0.6,-1,10,2,2,0,0\n";
        var first = SiriusChartCrypto.EncodeChart(source, key);
        var second = SiriusChartCrypto.EncodeChart(source, key);
        var decoded = SiriusChartCrypto.DecodeChart(first, key);
        if (!first.SequenceEqual(second) || decoded != source)
            throw new InvalidOperationException("Crypto self-test failed.");
        return new ChartSelfTestResult(first.Length);
    }

    private static ConversionResult Convert(string inputPath, ChartConvertOptions options)
    {
        var chart = SusParser.Parse(inputPath);
        return SusToSiriusConverter.Convert(chart, new ConversionOptions
        {
            ApplyWaveOffset = !options.IgnoreWaveOffset,
            Strict = options.Strict,
        });
    }

    private static string BuildChartText(IReadOnlyList<SiriusNote> notes)
    {
        var builder = new StringBuilder();
        foreach (var note in notes)
            builder.AppendLine(note.ToChartLine());
        return builder.ToString();
    }
}
