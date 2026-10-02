using System.Globalization;
using System.Text;
using System.Text.Json;
using Sirius.MasterData;
using Sirius.Toolbox.Charts;
using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.Master.Operations;
using Sirius.Toolbox.R2;

return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h") return Help();
            return args[0].ToLowerInvariant() switch
            {
                "chart" => RunChart(args[1..]),
                "episode" => RunEpisode(args[1..]),
                "master" => RunMaster(args[1..]),
                "r2" => await RunR2Async(args[1..]),
                _ => Help()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int RunChart(string[] args)
    {
        if (args.Length == 1 && args[0] == "self-test")
        {
            Console.WriteLine(new ChartToolService().SelfTest().EncodedLength);
            return 0;
        }
        if (args.Length < 3) return Usage("chart text <input.sus> <output.txt> [--strict]");
        var service = new ChartToolService();
        if (args[0] == "text")
        {
            var result = service.ConvertToText(args[1], args[2], new ChartConvertOptions(false, args.Contains("--strict")));
            Console.WriteLine($"wrote {result.OutputPath} ({result.NoteCount} notes)");
            return 0;
        }
        return Usage("chart text <input.sus> <output.txt>");
    }

    private static int RunEpisode(string[] args)
    {
        if (args.Length < 2) return Usage("episode pack|unpack|inspect <input> <output>");
        var service = new EpisodeToolService();
        return args[0] switch
        {
            "pack" => Write(service.Pack(args[1], args[2], overwrite: true).OutputPath),
            "unpack" => Write(service.Unpack(args[1], args[2], overwrite: true).OutputPath),
            "inspect" => InspectEpisode(service, args[1]),
            _ => Usage("episode pack|unpack|inspect <input> <output>")
        };
    }

    private static int RunMaster(string[] args)
    {
        if (args.Length < 2) return Usage("master verify|tables|get|add|update|delete|export-text <database> ...");
        var db = args[1];
        return args[0] switch
        {
            "verify" => Verify(db),
            "tables" => Tables(db),
            "get" when args.Length >= 4 => Get(db, args[2], args[3]),
            "add" when args.Length >= 5 => WriteResult(MasterMemoryDatabaseService.AddRecord(db, args[2], ReadJson(args[3]), args[4]).OutputPath),
            "update" when args.Length >= 6 => WriteResult(MasterMemoryDatabaseService.UpdateRecord(db, args[2], args[3], ReadJson(args[4]), args[5]).OutputPath),
            "delete" when args.Length >= 5 => WriteResult(MasterMemoryDatabaseService.DeleteRecord(db, args[2], args[3], args[4]).OutputPath),
            "export-text" when args.Length >= 3 => WriteCount(MasterMemoryDatabaseService.ExportText(db, args[2], includeEmpty: false, japaneseOnly: false)),
            _ => Usage("master verify|tables|get|add|update|delete|export-text <database> ...")
        };
    }

    private static async Task<int> RunR2Async(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("plan" or "sync")) return Usage("r2 plan|sync <directory>");
        var options = new R2SyncOptions(Path.GetFullPath(args[1]))
        {
            Endpoint = RequiredEnvironment("SIRIUS_R2_ENDPOINT"),
            Bucket = RequiredEnvironment("SIRIUS_R2_BUCKET"),
            AccessKeyId = RequiredEnvironment("R2_ACCESS_KEY_ID"),
            SecretAccessKey = RequiredEnvironment("R2_SECRET_ACCESS_KEY"),
            SessionToken = Environment.GetEnvironmentVariable("R2_SESSION_TOKEN"),
            DryRun = args[0] == "plan"
        };
        var result = await new R2AssetSyncService().SyncAsync(options, new Progress<R2SyncProgress>(x => Console.WriteLine(x.Message)));
        Console.WriteLine($"objects={result.ObjectCount} uploaded={result.UploadedCount} skipped={result.SkippedCount}");
        return 0;
    }

    private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"missing environment variable {name}");
    private static int Verify(string db) { var v = MasterMemoryDatabaseService.Verify(db); Console.WriteLine($"tables={v.TableCount} rows={v.RowCount} sha256={v.Sha256}"); return 0; }
    private static int Tables(string db) { foreach (var t in MasterMemoryDatabaseService.GetTables(db)) Console.WriteLine($"{t.Name}\t{t.RowCount}\t{string.Join(',', t.PrimaryKey)}"); return 0; }
    private static int Get(string db, string table, string key) { var record = MasterMemoryDatabaseService.GetRecord(db, table, key); Console.WriteLine(JsonSerializer.Serialize(record.Record)); return 0; }
    private static int InspectEpisode(EpisodeToolService service, string path) { var x = service.Inspect(path); Console.WriteLine($"decoded={x.Decoded} details={x.DetailCount}"); return x.Decoded ? 0 : 1; }
    private static int Write(string path) { Console.WriteLine(path); return 0; }
    private static int WriteCount(int count) { Console.WriteLine($"records={count}"); return 0; }
    private static int WriteResult(string path) { Console.WriteLine(path); return 0; }
    private static string ReadJson(string value) => File.Exists(value) ? File.ReadAllText(value) : value;
    private static int Usage(string text) { Console.Error.WriteLine($"usage: sirius-toolbox {text}"); return 2; }
    private static int Help() { Console.WriteLine("sirius-toolbox chart|episode|master|r2 ..."); return 0; }
}
