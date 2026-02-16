using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore;

public interface IRestoreApplicator
{
    string Category { get; }
    RestoreStage Stage { get; }
    Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct);
}
