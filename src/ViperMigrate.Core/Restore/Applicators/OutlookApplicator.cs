using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class OutlookApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public OutlookApplicator(MigrationPackage package) => _package = package;

    public string Category => "Outlook";
    public RestoreStage Stage => RestoreStage.Configuration;

    public Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        var config = _package.OutlookConfig;

        if (config == null)
        {
            result.Status = CategoryStatus.Success;
            sw.Stop();
            result.Duration = sw.Elapsed;
            return Task.FromResult(result);
        }

        // Check if Outlook signatures folder can be accessed (implies Office is installed)
        var sigDest = PathResolver.OutlookSignaturesPath;

        int itemsTotal = 0;
        int restored = 0;

        // Restore signatures
        if (config.SignaturesCaptured && config.SignatureFiles.Count > 0)
        {
            var sigSource = Path.Combine(packagePath, "Outlook", "Signatures");
            Directory.CreateDirectory(sigDest);

            if (Directory.Exists(sigSource))
            {
                // Copy files
                foreach (var file in Directory.GetFiles(sigSource))
                {
                    ct.ThrowIfCancellationRequested();
                    itemsTotal++;
                    try
                    {
                        File.Copy(file, Path.Combine(sigDest, Path.GetFileName(file)), true);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to copy signature '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }

                // Copy subdirectories (HTML resource folders)
                foreach (var dir in Directory.GetDirectories(sigSource))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        CopyDirectory(dir, Path.Combine(sigDest, Path.GetFileName(dir)), ct);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to copy signature folder '{Path.GetFileName(dir)}': {ex.Message}");
                    }
                }
            }

            progress.Report($"Restored {restored} signature file(s).");
        }

        // Account setup: manual actions
        foreach (var account in config.Accounts)
        {
            itemsTotal++;
            result.ManualActions.Add(new ManualAction
            {
                Category = Category,
                Title = $"Configure email account '{account.EmailAddress}'",
                Description = $"Type: {account.AccountType}, Server: {account.ServerName}",
                Priority = ManualActionPriority.Critical
            });
        }

        // PST files: manual actions
        foreach (var pstPath in config.PstPaths)
        {
            itemsTotal++;
            result.ManualActions.Add(new ManualAction
            {
                Category = Category,
                Title = "Reattach PST file",
                Description = $"Add PST data file: {pstPath}",
                Priority = ManualActionPriority.High
            });
        }

        result.ItemsTotal = itemsTotal;
        result.ItemsProcessed = restored + config.Accounts.Count + config.PstPaths.Count;
        result.Status = result.Warnings.Count > 0
            ? CategoryStatus.PartialSuccess
            : CategoryStatus.Success;

        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }

    private static void CopyDirectory(string source, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), ct);
        }
    }
}
