using System.Text.Json;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class ScannerCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public ScannerCollector(MigrationPackage package) => _package = package;

    public string Category => "Scanners";

    public async Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        try
        {
            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                "powershell",
                "-NoProfile -Command \"Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPClass -eq 'Image' } | Select-Object Name,Manufacturer,DeviceID | ConvertTo-Json -Depth 3\"",
                ct,
                60_000);

            if (exitCode != 0)
            {
                result.Status = CategoryStatus.Failed;
                result.Errors.Add($"Scanner enumeration failed: {stderr.Trim()}");
                sw.Stop();
                result.Duration = sw.Elapsed;
                return result;
            }

            if (string.IsNullOrWhiteSpace(stdout) || stdout.Trim() == "")
            {
                result.Status = CategoryStatus.Success;
                progress.Report("No scanners found.");
                sw.Stop();
                result.Duration = sw.Elapsed;
                return result;
            }

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var el in elements)
            {
                ct.ThrowIfCancellationRequested();
                _package.Scanners.Add(new CapturedScanner
                {
                    DeviceName = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                    Manufacturer = el.TryGetProperty("Manufacturer", out var m) ? m.GetString() ?? "" : "",
                    DeviceId = el.TryGetProperty("DeviceID", out var d) ? d.GetString() ?? "" : ""
                });
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Scanner capture failed: {ex.Message}");
        }

        result.ItemsTotal = _package.Scanners.Count;
        result.ItemsProcessed = _package.Scanners.Count;
        result.Status = result.Errors.Count > 0
            ? (_package.Scanners.Count > 0 ? CategoryStatus.PartialSuccess : CategoryStatus.Failed)
            : CategoryStatus.Success;

        progress.Report($"Found {_package.Scanners.Count} scanner(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
