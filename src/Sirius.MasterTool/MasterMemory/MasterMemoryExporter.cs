
namespace Sirius.MasterTool.MasterMemory;

using Sirius.MasterData;

public sealed class MasterMemoryExporter(Action<string>? progress = null)
{
    private readonly Action<string> _progress = progress ?? Console.WriteLine;

    public Task ExportAsync(string databasePath, string outputDirectory, CancellationToken cancellationToken) =>
        MasterMemoryDatabaseService.ExportAllJsonAsync(databasePath, outputDirectory, _progress, cancellationToken);
}
