using System.Text.RegularExpressions;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class PrinterApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    // Virtual printers to skip — expanded list
    private static readonly string[] VirtualPrinterPatterns =
    {
        "Microsoft Print to PDF", "Microsoft XPS Document Writer",
        "OneNote", "Send to OneNote", "Fax", "Microsoft Fax",
        "Foxit", "CutePDF", "Adobe PDF", "PDFCreator",
        "PDF24", "doPDF", "Bullzip PDF", "PDF-XChange",
        "Snagit", "PrimoPDF"
    };

    private static readonly string[] UniversalFallbackDrivers =
    {
        "Microsoft IPP Class Driver",
        "HP Universal Printing PCL 6"
    };

    private static readonly Regex IpPattern = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);

    public PrinterApplicator(MigrationPackage package) => _package = package;

    public string Category => "Printers";
    public RestoreStage Stage => RestoreStage.Peripherals;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        // Filter out virtual printers
        var realPrinters = _package.Printers
            .Where(p => !VirtualPrinterPatterns.Any(v =>
                p.Name.Contains(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        result.ItemsTotal = realPrinters.Count;
        int restored = 0;
        CapturedPrinter? defaultPrinter = null;

        foreach (var printer in realPrinters)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report($"Adding printer '{printer.Name}'...");

            bool added = false;

            if (printer.IsNetworkPrinter && !string.IsNullOrEmpty(printer.UncPath))
            {
                added = await TryAddNetworkPrinter(printer, result, ct, progress);
            }
            else
            {
                // Check PrinterHostAddress first (captured from source), then fallback to port name regex
                var hostAddress = printer.PrinterHostAddress;
                if (string.IsNullOrEmpty(hostAddress))
                {
                    var ipMatch = IpPattern.Match(printer.PortName);
                    if (ipMatch.Success)
                        hostAddress = ipMatch.Value;
                }

                if (!string.IsNullOrEmpty(hostAddress))
                {
                    added = await TryAddIpPrinter(printer, hostAddress, result, ct, progress);
                }
                else
                {
                    // Last resort: try resolving printer name as a hostname via DNS
                    var resolvedIp = await TryResolvePrinterName(printer.Name, ct);
                    if (!string.IsNullOrEmpty(resolvedIp))
                    {
                        progress.Report($"  Resolved '{printer.Name}' to {resolvedIp} via DNS lookup.");
                        added = await TryAddIpPrinter(printer, resolvedIp, result, ct, progress);
                    }
                    else
                    {
                        added = await TryAddLocalPrinter(printer, result, ct, progress);
                    }
                }
            }

            if (added)
            {
                restored++;
                if (printer.IsDefault)
                    defaultPrinter = printer;
            }
        }

        // Set default printer
        if (defaultPrinter != null)
        {
            try
            {
                // Use PowerShell pipeline with Where-Object to avoid WMI filter escaping issues
                var safeName = defaultPrinter.Name.Replace("'", "''");
                var (defExit, _, defErr) = await ProcessRunner.RunAsync(
                    "powershell",
                    $"-NoProfile -Command \"Get-CimInstance -ClassName Win32_Printer | Where-Object {{ $_.Name -eq '{safeName}' }} | Invoke-CimMethod -MethodName SetDefaultPrinter | Out-Null\"",
                    ct, 30_000);

                if (defExit != 0)
                {
                    // Fallback: use rundll32
                    await ProcessRunner.RunAsync(
                        "rundll32",
                        $"printui.dll,PrintUIEntry /y /n \"{defaultPrinter.Name}\"",
                        ct, 15_000);
                }
            }
            catch
            {
                result.Warnings.Add($"Could not set '{defaultPrinter.Name}' as default printer.");
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

        progress.Report($"Restored {restored}/{result.ItemsTotal} printer(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    /// <summary>
    /// Attempts to ensure the printer driver is available. Tries:
    /// 1. Check if already installed
    /// 2. Add from Windows Update
    /// 3. Fall back to universal drivers
    /// Returns (success, effectiveDriverName)
    /// </summary>
    private static async Task<(bool Success, string DriverName)> TryEnsureDriverAsync(
        string driverName, CategoryResult result, CancellationToken ct)
    {
        var safeName = driverName.Replace("'", "''");

        // Check if driver already exists
        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Get-PrinterDriver -Name '{safeName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name\"",
                ct, 15_000);

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                return (true, driverName);
        }
        catch { }

        // Try to add from Windows Update
        try
        {
            var (exitCode, _, stderr) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Add-PrinterDriver -Name '{safeName}' -ErrorAction Stop\"",
                ct, 60_000);

            if (exitCode == 0)
                return (true, driverName);
        }
        catch { }

        // Try universal fallback drivers
        foreach (var fallback in UniversalFallbackDrivers)
        {
            try
            {
                var fallbackSafe = fallback.Replace("'", "''");
                var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                    "powershell",
                    $"-NoProfile -Command \"Get-PrinterDriver -Name '{fallbackSafe}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name\"",
                    ct, 15_000);

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    result.Warnings.Add($"Using fallback driver '{fallback}' instead of '{driverName}'.");
                    return (true, fallback);
                }

                // Try to add the universal driver
                var (addExit, _, _) = await ProcessRunner.RunAsync(
                    "powershell",
                    $"-NoProfile -Command \"Add-PrinterDriver -Name '{fallbackSafe}' -ErrorAction Stop\"",
                    ct, 60_000);

                if (addExit == 0)
                {
                    result.Warnings.Add($"Installed fallback driver '{fallback}' instead of '{driverName}'.");
                    return (true, fallback);
                }
            }
            catch { }
        }

        return (false, driverName);
    }

    private static async Task<bool> TryAddNetworkPrinter(CapturedPrinter printer, CategoryResult result, CancellationToken ct, IProgress<string>? progress = null)
    {
        try
        {
            // Test server connectivity before attempting connection
            var serverName = printer.UncPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(serverName))
            {
                var (pingExit, _, _) = await ProcessRunner.RunAsync(
                    "ping", $"-n 1 -w 2000 {serverName}", ct, 5000);
                if (pingExit != 0)
                {
                    result.Warnings.Add($"Print server '{serverName}' is not reachable.");
                    result.ManualActions.Add(new ManualAction
                    {
                        Category = "Printers",
                        Title = $"Add printer '{printer.Name}'",
                        Description = $"Print server '{serverName}' is not reachable from this machine.\nOriginal UNC: {printer.UncPath}, Driver: {printer.DriverName}.\nTo add when server is available: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
                        Priority = ManualActionPriority.High
                    });
                    return false;
                }
            }

            // Try Point and Print first (handles driver installation automatically)
            progress?.Report($"  Trying Point and Print for '{printer.UncPath}'...");
            try
            {
                var (ppExit, _, _) = await ProcessRunner.RunAsync(
                    "rundll32", $"printui.dll,PrintUIEntry /in /n \"{printer.UncPath}\"",
                    ct, 60_000);
                if (ppExit == 0)
                    return true;
            }
            catch { }

            progress?.Report($"  Point and Print failed, trying Add-Printer -ConnectionName...");
            var (exitCode, _, stderr) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Add-Printer -ConnectionName '{printer.UncPath.Replace("'", "''")}'\"",
                ct, 60_000);

            if (exitCode == 0)
                return true;

            result.Warnings.Add($"Failed to add network printer '{printer.Name}' (UNC: {printer.UncPath}, Driver: {printer.DriverName}): {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error adding network printer '{printer.Name}' (UNC: {printer.UncPath}): {ex.Message}");
        }

        result.ManualActions.Add(new ManualAction
        {
            Category = "Printers",
            Title = $"Add printer '{printer.Name}'",
            Description = $"This printer was a network printer at {printer.UncPath}. Original driver: {printer.DriverName}.\nTo add: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
            Priority = ManualActionPriority.High
        });
        return false;
    }

    private static async Task<bool> TryAddIpPrinter(CapturedPrinter printer, string ip, CategoryResult result, CancellationToken ct, IProgress<string>? progress = null)
    {
        try
        {
            // Try IPP auto-negotiate first (works for most modern printers)
            progress?.Report($"  Attempting IPP auto-negotiate for '{printer.Name}' at {ip}...");
            try
            {
                var ippPortName = printer.PortName.Replace("'", "''");
                var ippPrinterName = printer.Name.Replace("'", "''");

                // Ensure port exists for IPP attempt
                var (portExitIpp, _, _) = await ProcessRunner.RunAsync(
                    "powershell",
                    $"-NoProfile -Command \"Add-PrinterPort -Name '{ippPortName}' -PrinterHostAddress '{ip}' -ErrorAction SilentlyContinue\"",
                    ct, 30_000);

                var (ippExit, _, ippErr) = await ProcessRunner.RunAsync(
                    "powershell",
                    $"-NoProfile -Command \"Add-Printer -Name '{ippPrinterName}' -PortName '{ippPortName}' -DriverName 'Microsoft IPP Class Driver' -ErrorAction Stop\"",
                    ct, 30_000);
                if (ippExit == 0)
                    return true;
            }
            catch { }

            // IPP failed — try with the original driver
            progress?.Report($"  IPP failed, trying original driver '{printer.DriverName}'...");
            // Step 1: Ensure driver is available
            var (driverOk, effectiveDriver) = await TryEnsureDriverAsync(printer.DriverName, result, ct);
            if (!driverOk)
            {
                result.Warnings.Add($"No driver available for '{printer.Name}' (requested: {printer.DriverName}, port: {printer.PortName}).");
                result.ManualActions.Add(new ManualAction
                {
                    Category = "Printers",
                    Title = $"Add printer '{printer.Name}'",
                    Description = $"This printer was connected at IP {ip}. Original driver: {printer.DriverName}.\nDriver not found. Install the driver from the manufacturer's website, then add the printer.\nTo add: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
                    Command = $"Add-Printer -Name \"{printer.Name}\" -DriverName \"{printer.DriverName}\" -PortName \"{printer.PortName}\"",
                    Priority = ManualActionPriority.High
                });
                return false;
            }

            // Step 2: Create the printer port
            progress?.Report($"  Creating port '{printer.PortName}' → {ip}...");
            var portName = printer.PortName.Replace("'", "''");
            var (portExit, _, portErr) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Add-PrinterPort -Name '{portName}' -PrinterHostAddress '{ip}'\"",
                ct, 30_000);

            if (portExit != 0 && !portErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                result.Warnings.Add($"Port creation warning for '{printer.Name}' (port: {portName}): {portErr.Trim()}");

            // Step 3: Add the printer
            progress?.Report($"  Adding printer with driver '{effectiveDriver}'...");
            var printerName = printer.Name.Replace("'", "''");
            var driverSafe = effectiveDriver.Replace("'", "''");
            var (printerExit, _, printerErr) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Add-Printer -Name '{printerName}' -DriverName '{driverSafe}' -PortName '{portName}'\"",
                ct, 60_000);

            if (printerExit == 0)
                return true;

            result.Warnings.Add($"Failed to add printer '{printer.Name}' (driver: {effectiveDriver}, port: {portName}): {printerErr.Trim()}");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error adding IP printer '{printer.Name}' (IP: {ip}): {ex.Message}");
        }

        result.ManualActions.Add(new ManualAction
        {
            Category = "Printers",
            Title = $"Add printer '{printer.Name}'",
            Description = $"This printer was connected at IP {ip}. Original driver: {printer.DriverName}, port: {printer.PortName}.\nTo add: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
            Priority = ManualActionPriority.High
        });
        return false;
    }

    private static async Task<bool> TryAddLocalPrinter(CapturedPrinter printer, CategoryResult result, CancellationToken ct, IProgress<string>? progress = null)
    {
        try
        {
            // Step 1: Ensure driver is available
            progress?.Report($"  Ensuring driver '{printer.DriverName}' is available...");
            var (driverOk, effectiveDriver) = await TryEnsureDriverAsync(printer.DriverName, result, ct);
            if (!driverOk)
            {
                result.Warnings.Add($"No driver available for '{printer.Name}' (requested: {printer.DriverName}, port: {printer.PortName}).");
                var hostInfo = !string.IsNullOrEmpty(printer.PrinterHostAddress) ? $" Host: {printer.PrinterHostAddress}." : "";
                result.ManualActions.Add(new ManualAction
                {
                    Category = "Printers",
                    Title = $"Add printer '{printer.Name}'",
                    Description = $"This printer was connected via port '{printer.PortName}'.{hostInfo} Original driver: {printer.DriverName}.\nDriver not found. Install the driver from the manufacturer's website, then add the printer.\nTo add: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
                    Command = $"Add-Printer -Name \"{printer.Name}\" -DriverName \"{printer.DriverName}\" -PortName \"{printer.PortName}\"",
                    Priority = ManualActionPriority.High
                });
                return false;
            }

            var portName = printer.PortName.Replace("'", "''");

            // Step 2: Ensure port exists
            progress?.Report($"  Checking port '{printer.PortName}'...");
            var portExists = await CheckPortExists(portName, ct);
            if (!portExists)
            {
                // Try to create port — extract IP from port name or use PrinterHostAddress
                var ip = "";
                var ipMatch = IpPattern.Match(printer.PortName);
                if (ipMatch.Success)
                    ip = ipMatch.Value;
                else if (!string.IsNullOrEmpty(printer.PrinterHostAddress))
                    ip = printer.PrinterHostAddress;

                if (!string.IsNullOrEmpty(ip))
                {
                    progress?.Report($"  Creating port '{printer.PortName}' → {ip}...");
                    var (portExit, _, portErr) = await ProcessRunner.RunAsync(
                        "powershell",
                        $"-NoProfile -Command \"Add-PrinterPort -Name '{portName}' -PrinterHostAddress '{ip}'\"",
                        ct, 30_000);

                    if (portExit != 0 && !portErr.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                        result.Warnings.Add($"Port creation warning for '{printer.Name}' (port: {printer.PortName}): {portErr.Trim()}");
                }
                else
                {
                    // No IP available — fall through to manual action
                    result.Warnings.Add($"Port '{printer.PortName}' does not exist and no host address available for '{printer.Name}'.");
                    result.ManualActions.Add(new ManualAction
                    {
                        Category = "Printers",
                        Title = $"Add printer '{printer.Name}'",
                        Description = $"This printer was connected via port '{printer.PortName}'. Original driver: {effectiveDriver}.\nThe port does not exist on this machine and no host address was captured.\nTo add: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
                        Priority = ManualActionPriority.High
                    });
                    return false;
                }
            }

            // Step 3: Add the printer
            progress?.Report($"  Adding printer '{printer.Name}' with driver '{effectiveDriver}'...");
            var printerName = printer.Name.Replace("'", "''");
            var driverSafe = effectiveDriver.Replace("'", "''");

            var (exitCode, _, stderr) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Add-Printer -Name '{printerName}' -DriverName '{driverSafe}' -PortName '{portName}'\"",
                ct, 60_000);

            if (exitCode == 0)
                return true;

            result.Warnings.Add($"Failed to add local printer '{printer.Name}' (driver: {effectiveDriver}, port: {printer.PortName}): {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error adding local printer '{printer.Name}' (port: {printer.PortName}): {ex.Message}");
        }

        var hostInfo2 = !string.IsNullOrEmpty(printer.PrinterHostAddress) ? $" Host: {printer.PrinterHostAddress}." : "";
        result.ManualActions.Add(new ManualAction
        {
            Category = "Printers",
            Title = $"Add printer '{printer.Name}'",
            Description = $"This printer was connected via port '{printer.PortName}'.{hostInfo2} Original driver: {printer.DriverName}.\nTo add: Settings \u2192 Bluetooth & devices \u2192 Printers & scanners \u2192 Add device.",
            Priority = ManualActionPriority.High
        });
        return false;
    }

    private static async Task<bool> CheckPortExists(string portName, CancellationToken ct)
    {
        try
        {
            var safeName = portName.Replace("'", "''");
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Get-PrinterPort -Name '{safeName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name\"",
                ct, 15_000);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Last-resort DNS/mDNS lookup — many network printers register their name via DNS.
    /// </summary>
    private static async Task<string?> TryResolvePrinterName(string printerName, CancellationToken ct)
    {
        var candidates = new[]
        {
            printerName,
            printerName.Replace(" ", "-"),
            printerName.Replace(" ", "")
        };

        foreach (var candidate in candidates)
        {
            // Skip if it has characters that can't be hostnames
            if (candidate.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '.'))
                continue;

            try
            {
                var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                    "ping", $"-4 -n 1 -w 2000 {candidate}", ct, 5_000);
                if (exitCode == 0 && stdout != null)
                {
                    var ipMatch = IpPattern.Match(stdout);
                    if (ipMatch.Success)
                        return ipMatch.Value;
                }
            }
            catch { }
        }

        return null;
    }
}
