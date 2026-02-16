using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class VerificationApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public VerificationApplicator(MigrationPackage package) => _package = package;

    public string Category => "Verification";
    public RestoreStage Stage => RestoreStage.Verification;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        var autoInstalled = _package.Software.Where(s => s.CanAutoInstall).ToList();
        if (autoInstalled.Count == 0)
        {
            result.Status = CategoryStatus.Success;
            progress.Report("No winget-installed software to verify.");
            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        var wingetAvailable = await WingetHelper.IsAvailableAsync(ct);
        if (!wingetAvailable)
        {
            result.Status = CategoryStatus.Skipped;
            result.Warnings.Add("Winget not available — cannot verify installations.");
            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        result.ItemsTotal = autoInstalled.Count;
        int verified = 0;
        int issues = 0;

        progress.Report($"Verifying {autoInstalled.Count} software installations...");

        foreach (var software in autoInstalled)
        {
            ct.ThrowIfCancellationRequested();

            var isInstalled = await WingetHelper.IsInstalledAsync(software.WingetId!, ct);
            if (isInstalled)
            {
                verified++;
            }
            else
            {
                issues++;
                result.Warnings.Add($"{software.DisplayName} ({software.WingetId}) not detected after install.");
            }
        }

        result.ItemsProcessed = verified;
        result.Status = issues == 0
            ? CategoryStatus.Success
            : verified > 0
                ? CategoryStatus.PartialSuccess
                : CategoryStatus.Failed;

        progress.Report($"Verification: {verified} confirmed, {issues} issue(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
