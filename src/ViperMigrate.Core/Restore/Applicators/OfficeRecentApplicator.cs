using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class OfficeRecentApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public OfficeRecentApplicator(MigrationPackage package) => _package = package;

    public string Category => "Office Recent";
    public RestoreStage Stage => RestoreStage.Configuration;

    public Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        if (_package.OfficeRecentFiles.Count == 0)
        {
            result.Status = CategoryStatus.Success;
            progress.Report("No Office recent files to report.");
            sw.Stop();
            result.Duration = sw.Elapsed;
            return Task.FromResult(result);
        }

        // Group by application
        var grouped = _package.OfficeRecentFiles.GroupBy(f => f.ApplicationName);

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();
            var fileList = string.Join("\n", group.Select(f => $"  - {f.FilePath}{(f.IsPinned ? " (pinned)" : "")}"));

            result.ManualActions.Add(new ManualAction
            {
                Category = Category,
                Title = $"{group.Key} recent files ({group.Count()})",
                Description = $"Previously accessed files:\n{fileList}",
                Priority = ManualActionPriority.Low
            });
        }

        result.ItemsTotal = _package.OfficeRecentFiles.Count;
        result.ItemsProcessed = _package.OfficeRecentFiles.Count;
        result.Status = CategoryStatus.Success;

        progress.Report($"Generated reference list for {_package.OfficeRecentFiles.Count} Office file(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }
}
