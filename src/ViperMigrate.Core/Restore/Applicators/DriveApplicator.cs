using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class DriveApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public DriveApplicator(MigrationPackage package) => _package = package;

    public string Category => "Mapped Drives";
    public RestoreStage Stage => RestoreStage.Connectivity;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        result.ItemsTotal = _package.MappedDrives.Count;
        int restored = 0;

        foreach (var drive in _package.MappedDrives)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report($"Mapping drive {drive.DriveLetter} to {drive.UncPath}...");

            var persistent = drive.Persistent ? "yes" : "no";
            var args = $"use {drive.DriveLetter} \"{drive.UncPath}\" /persistent:{persistent}";

            try
            {
                var (exitCode, _, stderr) = await ProcessRunner.RunAsync("net", args, ct);
                if (exitCode == 0)
                {
                    restored++;
                }
                else
                {
                    result.Warnings.Add($"Failed to map {drive.DriveLetter}: {stderr.Trim()}");
                    result.ManualActions.Add(new ManualAction
                    {
                        Category = Category,
                        Title = $"Map drive {drive.DriveLetter}",
                        Description = $"Run: net {args}",
                        Command = $"net {args}",
                        Priority = ManualActionPriority.High
                    });
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error mapping {drive.DriveLetter}: {ex.Message}");
                result.ManualActions.Add(new ManualAction
                {
                    Category = Category,
                    Title = $"Map drive {drive.DriveLetter}",
                    Description = $"Manually map {drive.DriveLetter} to {drive.UncPath}",
                    Priority = ManualActionPriority.High
                });
            }
        }

        result.ItemsProcessed = restored;
        result.Status = restored == result.ItemsTotal
            ? CategoryStatus.Success
            : restored > 0
                ? CategoryStatus.PartialSuccess
                : result.ItemsTotal == 0
                    ? CategoryStatus.Success
                    : CategoryStatus.Failed;

        progress.Report($"Mapped {restored}/{result.ItemsTotal} drive(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
