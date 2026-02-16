using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class SoftwareApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    private static readonly HashSet<string> RuntimePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.DotNet", "Microsoft.VCRedist", "Oracle.JavaRuntime",
        "OpenJS.NodeJS", "Python.Python", "Oracle.JDK"
    };

    private static readonly HashSet<string> BrowserWingetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Google.Chrome", "Mozilla.Firefox", "Vivaldi.Vivaldi",
        "Brave.Brave", "Opera.Opera", "Opera.OperaGX", "Microsoft.Edge"
    };

    public SoftwareApplicator(MigrationPackage package) => _package = package;

    public string Category => "Software";
    public RestoreStage Stage => RestoreStage.SoftwareInstall;

    private static int GetInstallPriority(CapturedSoftware s)
    {
        if (s.WingetId != null && RuntimePatterns.Any(p => s.WingetId.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return 0; // Runtimes first
        if (s.WingetId != null && BrowserWingetIds.Contains(s.WingetId))
            return 1; // Browsers second
        return 2; // Everything else
    }

    private static string GetHumanReadableSuggestion(string error)
    {
        if (error.Contains("No applicable installer", StringComparison.OrdinalIgnoreCase))
            return "Package may not support this Windows version or architecture.";
        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "Try installing manually in an elevated terminal.";
        if (error.Contains("already installed", StringComparison.OrdinalIgnoreCase))
            return "Already installed — may need repair or reinstall.";
        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "Package ID may have changed. Search winget: winget search <name>";
        return "Try installing manually in an elevated terminal.";
    }

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        result.ItemsTotal = _package.Software.Count;

        // Apply known winget IDs for older packages that might not have them
        WingetHelper.ApplyKnownWingetIds(_package.Software);

        var autoInstall = _package.Software
            .Where(s => s.CanAutoInstall)
            .OrderBy(GetInstallPriority)
            .ThenBy(s => s.DisplayName)
            .ToList();
        var manual = _package.Software.Where(s => !s.CanAutoInstall).ToList();

        int installed = 0;
        int skipped = 0;
        int failed = 0;

        var wingetAvailable = await WingetHelper.IsAvailableAsync(ct);

        if (wingetAvailable && autoInstall.Count > 0)
        {
            // Step 0: Update winget sources to ensure packages can be found
            progress.Report("Updating winget sources...");
            await WingetHelper.UpdateSourcesAsync(ct);

            // Step 1: Bulk install via FILTERED winget import
            var exportPath = Path.Combine(packagePath, "winget_export.json");
            if (File.Exists(exportPath))
            {
                // Build set of allowed winget IDs from the user's filtered selection
                var allowedIds = new HashSet<string>(
                    _package.Software
                        .Where(s => !string.IsNullOrEmpty(s.WingetId))
                        .Select(s => s.WingetId!),
                    StringComparer.OrdinalIgnoreCase);

                if (allowedIds.Count > 0)
                {
                    // Rewrite the export file to only include selected packages
                    var filteredExportPath = Path.Combine(packagePath, "winget_export_filtered.json");
                    try
                    {
                        var json = await File.ReadAllTextAsync(exportPath, ct);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);

                        // Rebuild the export JSON with only allowed packages
                        using var ms = new MemoryStream();
                        using (var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true }))
                        {
                            writer.WriteStartObject();

                            if (doc.RootElement.TryGetProperty("$schema", out var schema))
                                writer.WriteString("$schema", schema.GetString());

                            if (doc.RootElement.TryGetProperty("CreationDate", out var creationDate))
                                writer.WriteString("CreationDate", creationDate.GetString());

                            if (doc.RootElement.TryGetProperty("Sources", out var sources))
                            {
                                writer.WriteStartArray("Sources");
                                foreach (var source in sources.EnumerateArray())
                                {
                                    writer.WriteStartObject();

                                    if (source.TryGetProperty("SourceDetails", out var details))
                                    {
                                        writer.WritePropertyName("SourceDetails");
                                        details.WriteTo(writer);
                                    }

                                    writer.WriteStartArray("Packages");
                                    if (source.TryGetProperty("Packages", out var packages))
                                    {
                                        foreach (var pkg in packages.EnumerateArray())
                                        {
                                            if (pkg.TryGetProperty("PackageIdentifier", out var id))
                                            {
                                                var pkgId = id.GetString();
                                                if (pkgId != null && allowedIds.Contains(pkgId))
                                                    pkg.WriteTo(writer);
                                            }
                                        }
                                    }
                                    writer.WriteEndArray();

                                    writer.WriteEndObject();
                                }
                                writer.WriteEndArray();
                            }

                            if (doc.RootElement.TryGetProperty("WinGetVersion", out var version))
                                writer.WriteString("WinGetVersion", version.GetString());

                            writer.WriteEndObject();
                        }

                        await File.WriteAllBytesAsync(filteredExportPath, ms.ToArray(), ct);

                        progress.Report($"Running filtered winget import ({allowedIds.Count} selected packages)...");
                        try
                        {
                            var (exitCode, stdout, stderr) = await WingetHelper.ImportAsync(filteredExportPath, ct);
                            if (exitCode == 0)
                                progress.Report("Winget import completed. Verifying individual packages...");
                            else
                                progress.Report("Winget import finished with warnings. Checking individual packages...");
                        }
                        catch (TimeoutException)
                        {
                            progress.Report("Winget import timed out. Falling back to individual installs...");
                        }
                        catch (Exception ex)
                        {
                            progress.Report($"Winget import error: {ex.Message}. Falling back to individual installs...");
                        }
                        finally
                        {
                            try { File.Delete(filteredExportPath); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        progress.Report($"Could not filter winget export: {ex.Message}. Skipping bulk import.");
                    }
                }
                else
                {
                    progress.Report("No winget packages selected — skipping bulk import.");
                }
            }

            // Step 2: Check each package and install individually if not yet present
            for (int i = 0; i < autoInstall.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var software = autoInstall[i];
                progress.Report($"Checking {software.DisplayName} ({i + 1}/{autoInstall.Count})...");

                var alreadyInstalled = await WingetHelper.IsInstalledAsync(software.WingetId!, ct);
                if (alreadyInstalled)
                {
                    skipped++;
                    continue;
                }

                // Not installed yet — try individual install
                progress.Report($"Installing {software.DisplayName} ({i + 1}/{autoInstall.Count})...");
                var installResult = await WingetHelper.InstallAsync(software.WingetId!, progress, ct);
                if (installResult.Success)
                {
                    installed++;
                }
                else
                {
                    failed++;
                    result.Warnings.Add($"Failed to install {software.DisplayName}: {installResult.Error}");

                    var suggestion = GetHumanReadableSuggestion(installResult.Error);
                    var desc = $"Automatic install failed: {installResult.Error}\n\n{suggestion}\n\nCommand: winget install --id \"{software.WingetId}\" -e";
                    if (!string.IsNullOrEmpty(software.LicenseKey))
                        desc += $"\nLicense Key: {software.LicenseKey}";

                    result.ManualActions.Add(new ManualAction
                    {
                        Category = Category,
                        Title = $"Install {software.DisplayName}",
                        Description = desc,
                        Command = $"winget install --id \"{software.WingetId}\" -e",
                        Priority = ManualActionPriority.High
                    });
                }
            }
        }
        else if (!wingetAvailable && autoInstall.Count > 0)
        {
            result.Warnings.Add("Winget is not available. Software requires manual installation.");
            foreach (var software in autoInstall)
            {
                var desc = $"{software.DisplayName} {software.DisplayVersion}";
                if (!string.IsNullOrEmpty(software.LicenseKey))
                    desc += $"\nLicense Key: {software.LicenseKey}";

                result.ManualActions.Add(new ManualAction
                {
                    Category = Category,
                    Title = $"Install {software.DisplayName}",
                    Description = desc,
                    Priority = ManualActionPriority.Medium
                });
            }
        }

        // Manual-only software (no winget ID) — only these should appear in manual actions
        foreach (var software in manual)
        {
            var desc = !string.IsNullOrEmpty(software.Publisher)
                ? $"{software.DisplayName} {software.DisplayVersion} by {software.Publisher}"
                : $"{software.DisplayName} {software.DisplayVersion}";
            if (!string.IsNullOrEmpty(software.LicenseKey))
                desc += $"\nLicense Key: {software.LicenseKey}";

            result.ManualActions.Add(new ManualAction
            {
                Category = Category,
                Title = $"Install {software.DisplayName}",
                Description = desc,
                Priority = ManualActionPriority.Medium
            });
        }

        result.ItemsProcessed = installed + skipped;
        result.Status = (installed + skipped) > 0 || _package.Software.Count == 0
            ? (failed > 0 ? CategoryStatus.PartialSuccess : CategoryStatus.Success)
            : CategoryStatus.Failed;

        var summary = new List<string>();
        if (installed > 0) summary.Add($"{installed} installed");
        if (skipped > 0) summary.Add($"{skipped} already present");
        if (failed > 0) summary.Add($"{failed} failed");
        if (manual.Count > 0) summary.Add($"{manual.Count} need manual install");
        progress.Report($"Software: {string.Join(", ", summary)}.");

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
