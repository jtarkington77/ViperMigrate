using System.Xml.Linq;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class WifiCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public WifiCollector(MigrationPackage package) => _package = package;

    public string Category => "WiFi";

    public async Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        var wifiDir = Path.Combine(packagePath, "WiFi");
        Directory.CreateDirectory(wifiDir);

        var tempDir = Path.Combine(Path.GetTempPath(), $"viper_wifi_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                "netsh", $"wlan export profile key=clear folder=\"{tempDir}\"", ct);

            if (exitCode != 0)
            {
                result.Status = CategoryStatus.PartialSuccess;
                result.Warnings.Add($"netsh wlan export returned exit code {exitCode}: {stderr.Trim()}");
                sw.Stop();
                result.Duration = sw.Elapsed;
                return result;
            }

            var xmlFiles = Directory.GetFiles(tempDir, "*.xml");
            foreach (var xmlFile in xmlFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var xml = File.ReadAllText(xmlFile);
                    var doc = XDocument.Parse(xml);
                    var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                    var ssid = doc.Descendants(ns + "name").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(xmlFile);
                    var auth = doc.Descendants(ns + "authentication").FirstOrDefault()?.Value ?? "Unknown";
                    var encryption = doc.Descendants(ns + "encryption").FirstOrDefault()?.Value ?? "Unknown";

                    // Copy XML to package
                    var destFile = Path.Combine(wifiDir, Path.GetFileName(xmlFile));
                    File.Copy(xmlFile, destFile, true);

                    _package.WifiProfiles.Add(new CapturedWifiProfile
                    {
                        Ssid = ssid,
                        AuthType = auth,
                        EncryptionType = encryption,
                        XmlProfile = xml,
                        HasPassword = auth != "open"
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to parse WiFi profile '{Path.GetFileName(xmlFile)}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"WiFi capture failed: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        result.ItemsTotal = _package.WifiProfiles.Count;
        result.ItemsProcessed = _package.WifiProfiles.Count;
        result.Status = result.Errors.Count > 0
            ? CategoryStatus.Failed
            : _package.WifiProfiles.Count > 0
                ? (result.Warnings.Count > 0 ? CategoryStatus.PartialSuccess : CategoryStatus.Success)
                : CategoryStatus.Success;

        progress.Report($"Captured {_package.WifiProfiles.Count} WiFi profile(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
