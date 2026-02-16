using Microsoft.Win32;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class SoftwareCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public SoftwareCollector(MigrationPackage package) => _package = package;

    public string Category => "Software";

    // Registry value names commonly used for license/serial keys
    private static readonly string[] LicenseValueNames =
    {
        "Serial", "SerialNumber", "License", "LicenseKey", "ProductKey",
        "Registration", "CDKey", "Key", "RegistrationKey", "ProductId"
    };

    public async Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        var paths = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine, true),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine, false),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.CurrentUser, true),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (subKey, baseKey, isX64) in paths)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var name in RegistryHelper.GetSubKeyNames(baseKey, subKey))
            {
                var keyPath = $@"{subKey}\{name}";
                var displayName = RegistryHelper.ReadString(baseKey, keyPath, "DisplayName");
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var systemComponent = RegistryHelper.ReadInt(baseKey, keyPath, "SystemComponent");
                if (systemComponent == 1)
                    continue;

                if (!seen.Add(displayName))
                    continue;

                var software = new CapturedSoftware
                {
                    DisplayName = displayName,
                    DisplayVersion = RegistryHelper.ReadString(baseKey, keyPath, "DisplayVersion") ?? "",
                    Publisher = RegistryHelper.ReadString(baseKey, keyPath, "Publisher") ?? "",
                    InstallLocation = RegistryHelper.ReadString(baseKey, keyPath, "InstallLocation") ?? "",
                    UninstallString = RegistryHelper.ReadString(baseKey, keyPath, "UninstallString") ?? "",
                    InstallDate = RegistryHelper.ReadString(baseKey, keyPath, "InstallDate") ?? "",
                    IsX64 = isX64
                };

                // Try to find license key in the uninstall registry key
                software.LicenseKey = ExtractLicenseKey(baseKey, keyPath);

                _package.Software.Add(software);
            }
        }

        // Try to extract Windows product key
        ExtractWindowsKey(progress);

        // Try to extract Office keys
        ExtractOfficeKeys(progress);

        progress.Report($"Found {_package.Software.Count} installed applications.");

        // Attempt winget matching — uses fast methods only (no per-app search)
        if (_package.Software.Count > 0)
        {
            try
            {
                var wingetAvailable = await WingetHelper.IsAvailableAsync(ct);
                if (wingetAvailable)
                {
                    // Step 1: winget list — primary method, returns ALL packages winget recognizes
                    progress.Report("Running winget list to match package identifiers...");
                    var listEntries = await WingetHelper.ListInstalledAsync(ct);
                    if (listEntries.Count > 0)
                    {
                        WingetHelper.MatchSoftwareToWingetList(_package.Software, listEntries);
                        var listMatched = _package.Software.Count(s => s.CanAutoInstall);
                        progress.Report($"Matched {listMatched} applications via winget list.");
                    }

                    // Step 2: winget export — catches packages winget list may miss
                    progress.Report("Running winget export...");
                    var exportResult = await WingetHelper.ExportAsync(packagePath, ct);
                    if (exportResult.Success && exportResult.PackageIds.Count > 0)
                    {
                        WingetHelper.MatchSoftwareToWinget(_package.Software, exportResult.PackageIds);
                        _package.WingetExportCaptured = true;
                    }

                    // Step 3: Known winget IDs map — instant, no process calls
                    WingetHelper.ApplyKnownWingetIds(_package.Software);

                    // Step 4: Pattern-based resolution for VC++ redistributables, .NET runtimes, etc.
                    foreach (var item in _package.Software.Where(s => string.IsNullOrEmpty(s.WingetId)))
                    {
                        item.WingetId = WingetHelper.ResolveByNamingPattern(item.DisplayName, item.IsX64);
                    }

                    var matched = _package.Software.Count(s => s.CanAutoInstall);
                    progress.Report($"Matched {matched}/{_package.Software.Count} applications to winget packages.");
                }
                else
                {
                    result.Warnings.Add("Winget not available — software will require manual installation on restore.");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Winget matching failed: {ex.Message}");
            }
        }

        var keysFound = _package.Software.Count(s => !string.IsNullOrEmpty(s.LicenseKey));
        if (keysFound > 0)
            progress.Report($"Extracted {keysFound} license key(s).");

        result.ItemsTotal = _package.Software.Count;
        result.ItemsProcessed = _package.Software.Count;
        result.Status = _package.Software.Count > 0 ? CategoryStatus.Success : CategoryStatus.PartialSuccess;
        if (_package.Software.Count == 0)
            result.Warnings.Add("No installed software found.");

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static string? ExtractLicenseKey(RegistryKey baseKey, string keyPath)
    {
        foreach (var valueName in LicenseValueNames)
        {
            var value = RegistryHelper.ReadString(baseKey, keyPath, valueName);
            if (!string.IsNullOrWhiteSpace(value) && value.Length >= 5 && value.Length <= 100)
                return value;
        }
        return null;
    }

    private void ExtractWindowsKey(IProgress<string> progress)
    {
        try
        {
            // Try OA3 key (OEM systems)
            var task = ProcessRunner.RunAsync("cmd", "/c \"wmic path SoftwareLicensingService get OA3xOriginalProductKey /value\"",
                CancellationToken.None, 10_000);
            task.Wait();
            var (exitCode, stdout, _) = task.Result;

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var line = stdout.Split('\n').FirstOrDefault(l => l.Contains("OA3xOriginalProductKey="));
                if (line != null)
                {
                    var key = line.Split('=', 2).Last().Trim();
                    if (!string.IsNullOrWhiteSpace(key) && key.Length >= 25)
                    {
                        // Find or create a Windows entry
                        var windowsEntry = _package.Software.FirstOrDefault(s =>
                            s.DisplayName.Contains("Windows", StringComparison.OrdinalIgnoreCase) &&
                            s.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true);

                        if (windowsEntry != null)
                            windowsEntry.LicenseKey = key;
                    }
                }
            }
        }
        catch { }
    }

    private void ExtractOfficeKeys(IProgress<string> progress)
    {
        try
        {
            // Check Office registration keys for versions 14.0-16.0
            var versions = new[] { "14.0", "15.0", "16.0" };
            foreach (var ver in versions)
            {
                var regPath = $@"SOFTWARE\Microsoft\Office\{ver}\Registration";
                foreach (var subKey in RegistryHelper.GetSubKeyNames(Registry.LocalMachine, regPath))
                {
                    var fullPath = $@"{regPath}\{subKey}";
                    var productKey = RegistryHelper.ReadString(Registry.LocalMachine, fullPath, "ProductID")
                        ?? RegistryHelper.ReadString(Registry.LocalMachine, fullPath, "DigitalProductID");

                    if (!string.IsNullOrWhiteSpace(productKey))
                    {
                        var officeName = RegistryHelper.ReadString(Registry.LocalMachine, fullPath, "ProductName");
                        if (!string.IsNullOrEmpty(officeName))
                        {
                            var officeEntry = _package.Software.FirstOrDefault(s =>
                                s.DisplayName.Contains(officeName, StringComparison.OrdinalIgnoreCase) ||
                                s.DisplayName.Contains("Office", StringComparison.OrdinalIgnoreCase));

                            if (officeEntry != null && string.IsNullOrEmpty(officeEntry.LicenseKey))
                                officeEntry.LicenseKey = productKey;
                        }
                    }
                }
            }
        }
        catch { }
    }
}
