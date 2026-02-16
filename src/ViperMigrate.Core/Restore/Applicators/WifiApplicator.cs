using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class WifiApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public WifiApplicator(MigrationPackage package) => _package = package;

    public string Category => "WiFi";
    public RestoreStage Stage => RestoreStage.Connectivity;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        result.ItemsTotal = _package.WifiProfiles.Count;
        int restored = 0;

        // Detect WiFi adapter
        if (!await HasWifiAdapterAsync(ct))
        {
            progress.Report("No WiFi adapter detected — skipping WiFi restore.");
            result.Warnings.Add("No WiFi adapter detected on this machine. WiFi profiles cannot be imported.");
            result.Status = CategoryStatus.Skipped;
            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        var wifiDir = Path.Combine(packagePath, "WiFi");
        var tempFiles = new List<string>();

        try
        {
            foreach (var wifi in _package.WifiProfiles)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report($"Adding WiFi profile '{wifi.Ssid}'...");

                string? xmlFilePath = null;

                // Strategy 1: Look for package XML file matching SSID
                if (Directory.Exists(wifiDir))
                {
                    var candidates = Directory.GetFiles(wifiDir, "*.xml");
                    xmlFilePath = candidates.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f)
                            .Contains(wifi.Ssid, StringComparison.OrdinalIgnoreCase));
                }

                // If package is on network path, copy to local temp
                if (xmlFilePath != null && xmlFilePath.StartsWith(@"\\"))
                {
                    var localCopy = Path.Combine(Path.GetTempPath(), $"viper_wifi_{Guid.NewGuid()}.xml");
                    File.Copy(xmlFilePath, localCopy);
                    xmlFilePath = localCopy;
                    tempFiles.Add(localCopy);
                }

                // Strategy 2: Fall back to XmlProfile string from manifest
                if (xmlFilePath == null && !string.IsNullOrEmpty(wifi.XmlProfile))
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), $"viper_wifi_{Guid.NewGuid()}.xml");
                    File.WriteAllText(tempFile, wifi.XmlProfile);
                    xmlFilePath = tempFile;
                    tempFiles.Add(tempFile);
                }

                if (xmlFilePath == null)
                {
                    result.Warnings.Add($"No XML profile data available for WiFi '{wifi.Ssid}'.");
                    result.ManualActions.Add(new ManualAction
                    {
                        Category = Category,
                        Title = $"Add WiFi network '{wifi.Ssid}'",
                        Description = $"Manually connect to WiFi network '{wifi.Ssid}' (Auth: {wifi.AuthType})",
                        Priority = ManualActionPriority.Medium
                    });
                    continue;
                }

                // Import via netsh
                var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                    "netsh", $"wlan add profile filename=\"{xmlFilePath}\" user=all", ct, 30_000);

                if (exitCode == 0)
                {
                    restored++;
                }
                else
                {
                    // Capture full error — both stdout and stderr
                    var errorMsg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim()
                        : !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim()
                        : $"netsh exited with code {exitCode}";

                    result.Warnings.Add($"Failed to add WiFi '{wifi.Ssid}': {errorMsg}");
                    result.ManualActions.Add(new ManualAction
                    {
                        Category = Category,
                        Title = $"Add WiFi network '{wifi.Ssid}'",
                        Description = $"Manually connect to WiFi network '{wifi.Ssid}' (Auth: {wifi.AuthType})\nError: {errorMsg}",
                        Priority = ManualActionPriority.Medium
                    });
                }
            }
        }
        finally
        {
            // Clean up temp files only (not package files)
            foreach (var temp in tempFiles)
            {
                try { File.Delete(temp); } catch { }
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

        progress.Report($"Added {restored}/{result.ItemsTotal} WiFi profile(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static async Task<bool> HasWifiAdapterAsync(CancellationToken ct)
    {
        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "netsh", "wlan show interfaces", ct, 10_000);
            // If no adapter, netsh exits with error or output contains "no wireless"
            return exitCode == 0 &&
                   !stdout.Contains("no wireless", StringComparison.OrdinalIgnoreCase) &&
                   stdout.Contains("Name", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
