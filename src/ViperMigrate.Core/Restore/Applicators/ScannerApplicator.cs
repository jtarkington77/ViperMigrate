using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class ScannerApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public ScannerApplicator(MigrationPackage package) => _package = package;

    public string Category => "Scanners";
    public RestoreStage Stage => RestoreStage.Peripherals;

    public Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        foreach (var scanner in _package.Scanners)
        {
            ct.ThrowIfCancellationRequested();
            result.ManualActions.Add(new ManualAction
            {
                Category = Category,
                Title = $"Install scanner '{scanner.DeviceName}'",
                Description = !string.IsNullOrEmpty(scanner.Manufacturer)
                    ? $"Manufacturer: {scanner.Manufacturer}, Device ID: {scanner.DeviceId}"
                    : $"Device ID: {scanner.DeviceId}",
                Priority = ManualActionPriority.Medium
            });
        }

        result.ItemsTotal = _package.Scanners.Count;
        result.ItemsProcessed = _package.Scanners.Count;
        result.Status = CategoryStatus.Success;

        progress.Report($"Generated {result.ManualActions.Count} scanner installation action(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }
}
