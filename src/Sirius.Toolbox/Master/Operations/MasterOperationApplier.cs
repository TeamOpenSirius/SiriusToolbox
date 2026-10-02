using Sirius.MasterData;
using Sirius.Toolbox.IO;

namespace Sirius.Toolbox.Master.Operations;

public sealed class MasterOperationApplier
{
    public MasterOperationApplyResult Apply(string databasePath, MasterOperationPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(plan);
        if (!File.Exists(databasePath)) throw new FileNotFoundException("MasterMemory database not found.", databasePath);
        if (plan.IsEmpty)
        {
            var current = MasterMemoryDatabaseService.Verify(databasePath);
            return new MasterOperationApplyResult(databasePath, 0, current.Sha256);
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("Database path has no parent directory.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.ops-{Guid.NewGuid():N}.tmp");

        File.Copy(fullPath, tempPath, overwrite: true);
        try
        {
            foreach (var change in plan.Changes)
            {
                MasterMemoryDatabaseService.UpdateRecord(
                    tempPath,
                    change.Table,
                    change.Key,
                    change.PatchJson,
                    tempPath);
            }

            var verification = MasterMemoryDatabaseService.Verify(tempPath);
            AtomicFile.WriteAllBytes(fullPath, File.ReadAllBytes(tempPath));
            return new MasterOperationApplyResult(fullPath, plan.Changes.Count, verification.Sha256);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }
}
