using System.Text;
using Sirius.AssetTool.Charts.Conversion;
using Sirius.AssetTool.Charts.Sirius;
using SusParser = Sirius.AssetTool.Charts.Sus.SusParser;

namespace Sirius.AssetTool.Charts;

internal static class ChartCommands
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var options = CliOptions.Parse(args.Skip(1).ToArray());
        return command switch
        {
            "encode" => Encode(options),
            "text" => ExportText(options),
            "decode" => Decode(options),
            "selftest" => SelfTest(options),
            _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
        };
    }

    private static int Encode(CliOptions options)
    {
        options.RequirePositional(2, "encode <input.sus> <output.enc>");
        var input = options.Positionals[0];
        var output = options.Positionals[1];
        var key = options.GetKey();
        var result = Convert(input, options);
        var text = BuildChartText(result.Notes);
        File.WriteAllBytes(output, SiriusChartCrypto.EncodeChart(text, key));
        if (options.TextOutput is not null)
            File.WriteAllText(options.TextOutput, text, new UTF8Encoding(false));
        PrintResult(result, output);
        return 0;
    }

    private static int ExportText(CliOptions options)
    {
        options.RequirePositional(2, "text <input.sus> <output.txt>");
        var result = Convert(options.Positionals[0], options);
        var text = BuildChartText(result.Notes);
        File.WriteAllText(options.Positionals[1], text, new UTF8Encoding(false));
        PrintResult(result, options.Positionals[1]);
        return 0;
    }

    private static int Decode(CliOptions options)
    {
        options.RequirePositional(2, "decode <input.enc> <output.txt>");
        var text = SiriusChartCrypto.DecodeChart(File.ReadAllBytes(options.Positionals[0]), options.GetKey());
        File.WriteAllText(options.Positionals[1], text, new UTF8Encoding(false));
        Console.WriteLine($"decoded: {options.Positionals[1]}");
        return 0;
    }


    private static int SelfTest(CliOptions options)
    {
        options.RequirePositional(0, "selftest");
        const string key = "0123456789abcdef0123456789abcdef";
        const string source = "0.6,-1,10,2,2,0,0\n";
        var first = SiriusChartCrypto.EncodeChart(source, key);
        var second = SiriusChartCrypto.EncodeChart(source, key);
        var decoded = SiriusChartCrypto.DecodeChart(first, key);
        if (!first.SequenceEqual(second) || decoded != source)
            throw new InvalidOperationException("Crypto self-test failed.");
        Console.WriteLine($"selftest: ok ({first.Length} bytes)");
        return 0;
    }

    private static ConversionResult Convert(string input, CliOptions options)
    {
        var chart = SusParser.Parse(input);
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

    private static void PrintResult(ConversionResult result, string output)
    {
        Console.WriteLine($"notes: {result.Notes.Count}");
        Console.WriteLine($"output: {output}");
        foreach (var warning in result.Warnings)
            Console.Error.WriteLine($"warning: {warning}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Sirius.AssetTool chart - Ched/SUS to World Dai Star chart ENC converter

Usage:
  Sirius.AssetTool chart text   <input.sus> <output.txt> [options]
  Sirius.AssetTool chart encode <input.sus> <output.enc> --key <32-byte-key> [options]
  Sirius.AssetTool chart decode <input.enc> <output.txt> --key <32-byte-key>
  Sirius.AssetTool chart selftest

Options:
  --key <value>              Sirius chart AES key. Alternatively set SIRIUS_CHART_KEY.
  --text-out <path>          Also write the intermediate Sirius text while encoding.
  --ignore-wave-offset       Do not apply #WAVEOFFSET to note times.
  --strict                   Treat every unsupported/ambiguous SUS feature as an error.
  -h, --help                 Show help.
""");
    }

    private sealed class CliOptions
    {
        public List<string> Positionals { get; } = [];
        public string? Key { get; private set; } = "REMOVED_SECRET";
        public string? TextOutput { get; private set; }
        public bool IgnoreWaveOffset { get; private set; }
        public bool Strict { get; private set; }

        public static CliOptions Parse(string[] args)
        {
            var result = new CliOptions();
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--key":
                        result.Key = ReadValue(args, ref index, "--key");
                        break;
                    case "--text-out":
                        result.TextOutput = ReadValue(args, ref index, "--text-out");
                        break;
                    case "--ignore-wave-offset":
                        result.IgnoreWaveOffset = true;
                        break;
                    case "--strict":
                        result.Strict = true;
                        break;
                    default:
                        if (args[index].StartsWith('-'))
                            throw new ArgumentException($"Unknown option '{args[index]}'.");
                        result.Positionals.Add(args[index]);
                        break;
                }
            }
            return result;
        }

        public string GetKey()
        {
            var key = Key ?? Environment.GetEnvironmentVariable("SIRIUS_CHART_KEY");
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Missing --key. Pass the 32-byte chart key explicitly or set SIRIUS_CHART_KEY.");
            return key;
        }

        public void RequirePositional(int count, string usage)
        {
            if (Positionals.Count != count)
                throw new ArgumentException($"Usage: Sirius.AssetTool chart {usage}");
        }


        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length)
                throw new ArgumentException($"{option} requires a value.");
            return args[index];
        }
    }
}
