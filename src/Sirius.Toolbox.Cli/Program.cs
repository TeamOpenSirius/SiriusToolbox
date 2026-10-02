using System.CommandLine;
using System.Text.Json;
using Sirius.MasterData;
using Sirius.Toolbox.Charts;
using Sirius.Toolbox.Episodes;
using Sirius.Toolbox.R2;

return await Cli.Build().Parse(args).InvokeAsync();

internal static class Cli
{
    public static RootCommand Build()
    {
        var root = new RootCommand("Sirius Toolbox CLI：MasterMemory、谱面、Episode 和 R2 工作流。");
        root.Add(BuildChart()); root.Add(BuildEpisode()); root.Add(BuildMaster()); root.Add(BuildR2());
        root.SetAction(_ => { Console.WriteLine("Sirius Toolbox CLI 1.0.0"); return 0; }); return root;
    }
    private static Command BuildChart()
    {
        var c = new Command("chart", "转换和自测 Sirius 谱面文件。"); var text = new Command("text", "将 SUS 谱面转换为文本。"); var i = new Argument<string>("input"); var o = new Argument<string>("output"); var strict = new Option<bool>("--strict", "严格模式"); text.Add(i); text.Add(o); text.Add(strict); text.SetAction(p => { var x = new ChartToolService().ConvertToText(p.GetValue(i)!, p.GetValue(o)!, new ChartConvertOptions(false, p.GetValue(strict))); Console.WriteLine($"已写入 {x.OutputPath}（{x.NoteCount} 个音符）"); return 0; }); var self = new Command("self-test", "运行谱面加解密自测。"); self.SetAction(_ => { Console.WriteLine($"自测通过，编码长度：{new ChartToolService().SelfTest().EncodedLength}"); return 0; }); c.Add(text); c.Add(self); return c;
    }
    private static Command BuildEpisode()
    {
        var c = new Command("episode", "打包、解包和检查 Episode 文件。"); var i = new Argument<string>("input"); var o = new Argument<string>("output"); var pack = new Command("pack", "JSON 打包为 BIN"); pack.Add(i); pack.Add(o); pack.SetAction(p => { Console.WriteLine(new EpisodeToolService().Pack(p.GetValue(i)!, p.GetValue(o)!, true).OutputPath); return 0; }); var unpack = new Command("unpack", "BIN 解包为 JSON"); unpack.Add(i); unpack.Add(o); unpack.SetAction(p => { Console.WriteLine(new EpisodeToolService().Unpack(p.GetValue(i)!, p.GetValue(o)!, true).OutputPath); return 0; }); var inspect = new Command("inspect", "检查 BIN"); inspect.Add(i); inspect.SetAction(p => { var x = new EpisodeToolService().Inspect(p.GetValue(i)!); Console.WriteLine($"decoded={x.Decoded} details={x.DetailCount}"); return x.Decoded ? 0 : 1; }); c.Add(pack); c.Add(unpack); c.Add(inspect); return c;
    }
    private static Command BuildMaster()
    {
        var c = new Command("master", "验证和编辑 MasterMemory 数据库。"); var db = new Argument<string>("database"); var table = new Argument<string>("table"); var key = new Argument<string>("key"); var json = new Argument<string>("json"); var output = new Argument<string>("output"); var verify = new Command("verify", "验证数据库"); verify.Add(db); verify.SetAction(p => { var x = MasterMemoryDatabaseService.Verify(p.GetValue(db)!); Console.WriteLine($"表={x.TableCount} 行={x.RowCount} SHA-256={x.Sha256}"); return 0; }); var tables = new Command("tables", "列出表"); tables.Add(db); tables.SetAction(p => { foreach (var x in MasterMemoryDatabaseService.GetTables(p.GetValue(db)!)) Console.WriteLine($"{x.Name}\t{x.RowCount}\t{string.Join(',', x.PrimaryKey)}"); return 0; }); var get = new Command("get", "读取记录"); get.Add(db); get.Add(table); get.Add(key); get.SetAction(p => { Console.WriteLine(JsonSerializer.Serialize(MasterMemoryDatabaseService.GetRecord(p.GetValue(db)!, p.GetValue(table)!, p.GetValue(key)!).Record)); return 0; }); var add = new Command("add", "添加记录"); add.Add(db); add.Add(table); add.Add(json); add.Add(output); add.SetAction(p => { Console.WriteLine(MasterMemoryDatabaseService.AddRecord(p.GetValue(db)!, p.GetValue(table)!, File.ReadAllText(p.GetValue(json)!), p.GetValue(output)!).OutputPath); return 0; }); var update = new Command("update", "更新记录"); update.Add(db); update.Add(table); update.Add(key); update.Add(json); update.Add(output); update.SetAction(p => { Console.WriteLine(MasterMemoryDatabaseService.UpdateRecord(p.GetValue(db)!, p.GetValue(table)!, p.GetValue(key)!, File.ReadAllText(p.GetValue(json)!), p.GetValue(output)!).OutputPath); return 0; }); var delete = new Command("delete", "删除记录"); delete.Add(db); delete.Add(table); delete.Add(key); delete.Add(output); delete.SetAction(p => { Console.WriteLine(MasterMemoryDatabaseService.DeleteRecord(p.GetValue(db)!, p.GetValue(table)!, p.GetValue(key)!, p.GetValue(output)!).OutputPath); return 0; }); var export = new Command("export-text", "导出文本脱敏/翻译 CSV"); export.Add(db); export.Add(output); export.SetAction(p => { Console.WriteLine($"记录数={MasterMemoryDatabaseService.ExportText(p.GetValue(db)!, p.GetValue(output)!, false, false)}"); return 0; }); c.Add(verify); c.Add(tables); c.Add(get); c.Add(add); c.Add(update); c.Add(delete); c.Add(export); return c;
    }
    private static Command BuildR2()
    {
        var c = new Command("r2", "规划或执行 R2 同步。"); var d = new Argument<string>("directory"); var plan = new Command("plan", "仅生成计划"); plan.Add(d); plan.SetAction(async p => await RunR2(p.GetValue(d)!, true)); var sync = new Command("sync", "执行同步"); sync.Add(d); sync.SetAction(async p => await RunR2(p.GetValue(d)!, false)); c.Add(plan); c.Add(sync); return c;
    }
    private static async Task<int> RunR2(string directory, bool dryRun)
    {
        var o = new R2SyncOptions(Path.GetFullPath(directory)) { Endpoint = Required("SIRIUS_R2_ENDPOINT"), Bucket = Required("SIRIUS_R2_BUCKET"), AccessKeyId = Required("R2_ACCESS_KEY_ID"), SecretAccessKey = Required("R2_SECRET_ACCESS_KEY"), SessionToken = Environment.GetEnvironmentVariable("R2_SESSION_TOKEN"), DryRun = dryRun }; var x = await new R2AssetSyncService().SyncAsync(o, new Progress<R2SyncProgress>(p => Console.WriteLine(p.Message))); Console.WriteLine($"对象={x.ObjectCount} 上传={x.UploadedCount} 跳过={x.SkippedCount}"); return 0;
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"缺少环境变量：{name}");
}
