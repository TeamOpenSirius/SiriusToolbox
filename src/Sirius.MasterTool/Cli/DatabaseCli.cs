using System.Text.Json;
using Sirius.MasterData;
using Sirius.MasterTool.MasterMemory;

namespace Sirius.MasterTool;

internal static class DatabaseCli
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "verify" => Verify(args[1..]),
            "tables" => Tables(args[1..]),
            "schema" => Schema(args[1..]),
            "list" => List(args[1..]),
            "get" => Get(args[1..]),
            "add" => Add(args[1..]),
            "update" => Update(args[1..]),
            "delete" => Delete(args[1..]),
            "export-json" => await ExportJsonAsync(args[1..]),
            "export-text" => ExportText(args[1..]),
            "build-text" => BuildText(args[1..]),
            "roundtrip" => Roundtrip(args[1..]),
            _ => throw new ArgumentException($"Unknown db command: {args[0]}")
        };
    }

    private static int Verify(string[] args)
    {
        RequirePositional(args, 1, "db verify <mastermemory.db>");
        var result = MasterMemoryDatabaseService.Verify(args[0]);
        Console.WriteLine($"OK: {result.TableCount} tables / {result.RowCount:N0} rows");
        Console.WriteLine($"SHA-256: {result.Sha256}");
        return 0;
    }

    private static int Tables(string[] args)
    {
        RequirePositional(args, 1, "db tables <mastermemory.db> [--contains <text>]");
        var options = Args.Parse(args[1..]);
        var tables = MasterMemoryDatabaseService.GetTables(args[0], options.Single("--contains"));
        Console.WriteLine(JsonSerializer.Serialize(tables, JsonOptions));
        return 0;
    }

    private static int Schema(string[] args)
    {
        RequirePositional(args, 2, "db schema <mastermemory.db> <table>");
        var schema = MasterMemoryDatabaseService.GetSchema(args[0], args[1]);
        Console.WriteLine(JsonSerializer.Serialize(new { table = args[1], properties = schema }, JsonOptions));
        return 0;
    }

    private static int List(string[] args)
    {
        RequirePositional(args, 2, "db list <mastermemory.db> <table> [--offset N] [--limit N]");
        var options = Args.Parse(args[2..]);
        var offset = options.Int("--offset", 0);
        var limit = options.Int("--limit", 50);
        var records = MasterMemoryDatabaseService.ListRecords(args[0], args[1], offset, limit);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            table = args[1],
            offset,
            limit,
            count = records.Count,
            records
        }, JsonOptions));
        return 0;
    }

    private static int Get(string[] args)
    {
        RequirePositional(args, 2, "db get <mastermemory.db> <table> --key <value>");
        var options = Args.Parse(args[2..]);
        var key = options.Required("--key");
        var record = MasterMemoryDatabaseService.GetRecord(args[0], args[1], key);
        Console.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
        return 0;
    }

    private static int Add(string[] args)
    {
        RequirePositional(args, 2, "db add <mastermemory.db> <table> [--json file|--data json|--set P=V] (-o file|--in-place)");
        var options = Args.Parse(args[2..]);
        var sourceJson = MasterMemoryDatabaseService.ReadJsonArgument(options.Single("--json"), options.Single("--data"));
        var payload = MasterMemoryDatabaseService.MergeJsonWithSetValues(sourceJson, options.Many("--set"));
        return ExecuteWrite(args[0], options, output =>
            MasterMemoryDatabaseService.AddRecord(args[0], args[1], payload, output));
    }

    private static int Update(string[] args)
    {
        RequirePositional(args, 2, "db update <mastermemory.db> <table> --key <value> [--json file|--data json|--set P=V] (-o file|--in-place)");
        var options = Args.Parse(args[2..]);
        var key = options.Required("--key");
        var sourceJson = MasterMemoryDatabaseService.ReadJsonArgument(options.Single("--json"), options.Single("--data"));
        var payload = MasterMemoryDatabaseService.MergeJsonWithSetValues(sourceJson, options.Many("--set"));
        if (payload == "{}") throw new ArgumentException("No update fields supplied. Use --json, --data or --set.");
        return ExecuteWrite(args[0], options, output =>
            MasterMemoryDatabaseService.UpdateRecord(args[0], args[1], key, payload, output));
    }

    private static int Delete(string[] args)
    {
        RequirePositional(args, 2, "db delete <mastermemory.db> <table> --key <value> (-o file|--in-place)");
        var options = Args.Parse(args[2..]);
        var key = options.Required("--key");
        return ExecuteWrite(args[0], options, output =>
            MasterMemoryDatabaseService.DeleteRecord(args[0], args[1], key, output));
    }

    private static async Task<int> ExportJsonAsync(string[] args)
    {
        RequirePositional(args, 2, "db export-json <mastermemory.db> <output-directory>");
        await MasterMemoryDatabaseService.ExportAllJsonAsync(args[0], args[1], Console.WriteLine);
        Console.WriteLine($"Exported to: {Path.GetFullPath(args[1])}");
        return 0;
    }

    private static int ExportText(string[] args)
    {
        RequirePositional(args, 2, "db export-text <mastermemory.db> <translation.csv> [--japanese-only] [--include-empty]");
        var options = Args.Parse(args[2..]);
        var count = MasterMemoryDatabaseService.ExportText(
            args[0], args[1], options.Has("--include-empty"), options.Has("--japanese-only"));
        Console.WriteLine($"Exported {count:N0} string cells to {Path.GetFullPath(args[1])}");
        return 0;
    }

    private static int BuildText(string[] args)
    {
        RequirePositional(args, 3, "db build-text <mastermemory.db> <translation.csv> <output.db>");
        var result = MasterMemoryDatabaseService.BuildText(args[0], args[1], args[2]);
        PrintWriteResult(result, Path.GetFullPath(args[2]), null);
        return 0;
    }

    private static int Roundtrip(string[] args)
    {
        RequirePositional(args, 2, "db roundtrip <mastermemory.db> <output.db>");
        var input = File.ReadAllBytes(args[0]);
        _ = MasterMemoryDatabaseService.Verify(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllBytes(args[1], input);
        var output = File.ReadAllBytes(args[1]);
        var same = input.AsSpan().SequenceEqual(output);
        Console.WriteLine(same ? "BYTE-EXACT ROUNDTRIP: PASS" : "BYTE-EXACT ROUNDTRIP: FAIL");
        return same ? 0 : 3;
    }

    private static int ExecuteWrite(
        string inputPath,
        Args options,
        Func<string, MasterDatabaseWriteResult> action)
    {
        var output = options.Single("-o") ?? options.Single("--output");
        var inPlace = options.Has("--in-place");
        if (inPlace && output is not null)
            throw new ArgumentException("Use either -o/--output or --in-place, not both.");
        if (!inPlace && string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Specify -o <output.db> or --in-place.");

        var fullInput = Path.GetFullPath(inputPath);
        if (!inPlace)
        {
            var fullOutput = Path.GetFullPath(output!);
            if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Refusing to overwrite the input via -o. Use --in-place so a backup is created.");
            var result = action(fullOutput);
            PrintWriteResult(result, fullOutput, null);
            return 0;
        }

        var backup = fullInput + ".bak";
        var temporary = fullInput + ".edit-" + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(fullInput, backup, overwrite: true);
        try
        {
            var result = action(temporary);
            File.Move(temporary, fullInput, overwrite: true);
            PrintWriteResult(result, fullInput, backup);
            return 0;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void PrintWriteResult(MasterDatabaseWriteResult result, string outputPath, string? backupPath)
    {
        Console.WriteLine($"Updated: {result.Table}");
        Console.WriteLine($"Rows: {result.RowCount:N0}");
        Console.WriteLine($"Output: {outputPath}");
        if (backupPath is not null) Console.WriteLine($"Backup: {backupPath}");
        Console.WriteLine($"SHA-256: {result.Sha256}");
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static void RequirePositional(string[] args, int count, string usage)
    {
        if (args.Length < count) throw new ArgumentException($"Usage: Sirius.MasterTool {usage}");
    }

    private static void PrintHelp() => Console.WriteLine("""
Sirius Master Tool - MasterMemory database commands

Usage:
  Sirius.MasterTool db verify <mastermemory.db>
  Sirius.MasterTool db tables <mastermemory.db> [--contains <text>]
  Sirius.MasterTool db schema <mastermemory.db> <table>
  Sirius.MasterTool db list <mastermemory.db> <table> [--offset N] [--limit N]
  Sirius.MasterTool db get <mastermemory.db> <table> --key <value>
  Sirius.MasterTool db add <mastermemory.db> <table> [--json file|--data json|--set P=V ...] (-o file|--in-place)
  Sirius.MasterTool db update <mastermemory.db> <table> --key <value> [--json file|--data json|--set P=V ...] (-o file|--in-place)
  Sirius.MasterTool db delete <mastermemory.db> <table> --key <value> (-o file|--in-place)
  Sirius.MasterTool db export-json <mastermemory.db> <directory>
  Sirius.MasterTool db export-text <mastermemory.db> <translation.csv> [--japanese-only] [--include-empty]
  Sirius.MasterTool db build-text <mastermemory.db> <translation.csv> <output.db>
  Sirius.MasterTool db roundtrip <mastermemory.db> <output.db>

Keys:
  A table with one primary-key field accepts --key 123.
  Composite keys use --key "FieldA=123;FieldB=4".

Writes:
  -o, --output <file>  Write a new database file.
  --in-place           Replace the input after validation and create <input>.bak.

Record payloads:
  --json <file>        Read a JSON object from a file.
  --data <json>        Read a JSON object directly from the command line.
  --set Name=Value     Override/add one top-level property; may be repeated.
                       Value is parsed as JSON when possible, otherwise as a string.
""");

    internal sealed class Args
    {
        private readonly Dictionary<string, List<string?>> _values = new(StringComparer.OrdinalIgnoreCase);

        public static Args Parse(string[] args)
        {
            var result = new Args();
            for (var i = 0; i < args.Length; i++)
            {
                var name = args[i];
                if (!name.StartsWith('-'))
                    throw new ArgumentException($"Unexpected positional argument: {name}");

                string? value = null;
                if (name is not "--in-place" and not "--include-empty" and not "--japanese-only")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                        throw new ArgumentException($"Missing value after {name}");
                    value = args[++i];
                }
                result.Add(name, value);
            }
            return result;
        }

        private void Add(string name, string? value)
        {
            if (!_values.TryGetValue(name, out var list))
            {
                list = [];
                _values[name] = list;
            }
            list.Add(value);
        }

        public bool Has(string name) => _values.ContainsKey(name);

        public string? Single(string name)
        {
            if (!_values.TryGetValue(name, out var values)) return null;
            if (values.Count != 1) throw new ArgumentException($"Option may only be supplied once: {name}");
            return values[0];
        }

        public string Required(string name) =>
            Single(name) ?? throw new ArgumentException($"Missing required option: {name}");

        public IReadOnlyList<string> Many(string name) =>
            _values.TryGetValue(name, out var values)
                ? values.Select(x => x ?? "").ToArray()
                : [];

        public int Int(string name, int fallback)
        {
            var value = Single(name);
            return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
