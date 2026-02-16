using Microsoft.Win32;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class OfficeRecentCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    private static readonly (string App, string Label)[] OfficeApps =
    {
        ("Word", "Word"),
        ("Excel", "Excel"),
        ("PowerPoint", "PowerPoint")
    };

    public OfficeRecentCollector(MigrationPackage package) => _package = package;

    public string Category => "Office Recent";

    public Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        foreach (var (app, label) in OfficeApps)
        {
            ct.ThrowIfCancellationRequested();

            // Try 16.0, then 15.0
            foreach (var version in new[] { "16.0", "15.0" })
            {
                var mruPath = PathResolver.OfficeMruRegistryBase(version, app);
                var valueNames = RegistryHelper.GetValueNames(Registry.CurrentUser, mruPath);

                foreach (var valueName in valueNames)
                {
                    if (!valueName.StartsWith("Item", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rawValue = RegistryHelper.ReadString(Registry.CurrentUser, mruPath, valueName);
                    if (string.IsNullOrEmpty(rawValue))
                        continue;

                    // Format: [F00000000][T{hex}]*{filepath}
                    var filePath = ParseMruValue(rawValue);
                    if (string.IsNullOrEmpty(filePath))
                        continue;

                    _package.OfficeRecentFiles.Add(new CapturedOfficeRecent
                    {
                        ApplicationName = label,
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        IsPinned = rawValue.Contains("[F00000001]"),
                        LastAccessed = DateTime.UtcNow // MRU doesn't store accurate timestamps
                    });
                }

                // If we found items for this version, don't check the older version
                if (_package.OfficeRecentFiles.Any(f => f.ApplicationName == label))
                    break;
            }
        }

        result.ItemsTotal = _package.OfficeRecentFiles.Count;
        result.ItemsProcessed = _package.OfficeRecentFiles.Count;
        result.Status = CategoryStatus.Success;

        progress.Report($"Found {_package.OfficeRecentFiles.Count} recent Office file(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }

    private static string? ParseMruValue(string rawValue)
    {
        // Format: [F00000000][T01D8A2B3C4D5E6F7]*C:\path\to\file.docx
        var asteriskIndex = rawValue.IndexOf('*');
        if (asteriskIndex < 0 || asteriskIndex + 1 >= rawValue.Length)
            return null;

        return rawValue[(asteriskIndex + 1)..];
    }
}
