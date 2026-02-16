using System.Text.Json;
using System.Text.RegularExpressions;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class PrinterCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    private static readonly HashSet<string> BuiltInPrinters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Print to PDF",
        "Microsoft XPS Document Writer",
        "Fax",
        "OneNote",
        "OneNote (Desktop)",
        "OneNote for Windows 10",
        "Send To OneNote 2016"
    };

    public PrinterCollector(MigrationPackage package) => _package = package;

    public string Category => "Printers";

    public async Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        try
        {
            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                "powershell",
                "-NoProfile -Command \"Get-Printer | Select-Object Name,DriverName,PortName,ShareName,ComputerName,Type | ConvertTo-Json -Depth 3\"",
                ct,
                60_000);

            progress.Report($"Get-Printer exit code: {exitCode}");
            if (exitCode != 0)
                progress.Report($"Get-Printer stderr: {stderr?.Trim()}");

            string defaultPrinterName = "";
            try
            {
                var (defExit, defOut, _) = await ProcessRunner.RunAsync(
                    "powershell",
                    "-NoProfile -Command \"(Get-CimInstance -ClassName Win32_Printer -Filter 'Default=True' -ErrorAction SilentlyContinue).Name\"",
                    ct, 15_000);
                if (defExit == 0)
                    defaultPrinterName = defOut?.Trim() ?? "";
            }
            catch { }

            if (exitCode != 0)
            {
                progress.Report("Get-Printer failed, trying Win32_Printer fallback...");
                (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                    "powershell",
                    "-NoProfile -Command \"Get-CimInstance -ClassName Win32_Printer | Select-Object Name,DriverName,PortName,ShareName,SystemName,@{N='IsDefault';E={$_.Default}} | ConvertTo-Json -Depth 3\"",
                    ct, 60_000);
                progress.Report($"Win32_Printer fallback exit code: {exitCode}");
                if (exitCode != 0)
                    progress.Report($"Win32_Printer fallback stderr: {stderr?.Trim()}");
            }

            if (exitCode != 0)
            {
                result.Status = CategoryStatus.Failed;
                result.Errors.Add($"Get-Printer failed: {stderr?.Trim()}");
                sw.Stop();
                result.Duration = sw.Elapsed;
                return result;
            }

            if (string.IsNullOrWhiteSpace(stdout) || stdout.Trim() == "")
            {
                result.Status = CategoryStatus.Success;
                progress.Report("No printers found.");
                sw.Stop();
                result.Duration = sw.Elapsed;
                return result;
            }

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            // Handle both single object and array
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var el in elements)
            {
                ct.ThrowIfCancellationRequested();
                var name = el.GetProperty("Name").GetString() ?? "";
                if (BuiltInPrinters.Contains(name))
                    continue;

                var portName = el.GetProperty("PortName").GetString() ?? "";
                var serverName = "";
                if (el.TryGetProperty("ComputerName", out var cn))
                    serverName = cn.GetString() ?? "";
                else if (el.TryGetProperty("SystemName", out var sn2))
                    serverName = sn2.GetString() ?? "";
                var isNetwork = !string.IsNullOrEmpty(serverName) || portName.StartsWith(@"\\");
                var uncPath = isNetwork
                    ? (!string.IsNullOrEmpty(serverName) ? $@"\\{serverName}\{name}" : portName)
                    : "";

                var isDefault = false;
                if (!string.IsNullOrEmpty(defaultPrinterName))
                {
                    isDefault = name.Equals(defaultPrinterName, StringComparison.OrdinalIgnoreCase);
                }
                else if (el.TryGetProperty("IsDefault", out var defProp))
                {
                    isDefault = defProp.ValueKind == JsonValueKind.True;
                }

                var printer = new CapturedPrinter
                {
                    Name = name,
                    DriverName = el.GetProperty("DriverName").GetString() ?? "",
                    PortName = portName,
                    ShareName = el.TryGetProperty("ShareName", out var sn) ? sn.GetString() ?? "" : "",
                    ServerName = serverName,
                    IsDefault = isDefault,
                    IsNetworkPrinter = isNetwork,
                    UncPath = uncPath
                };

                // Resolve printer host address from port (multi-method)
                printer.PrinterHostAddress = await ResolveHostAddress(portName, name, ct, progress);

                progress.Report($"  Captured: '{printer.Name}' | Driver: '{printer.DriverName}' | Port: '{printer.PortName}' | IP: '{printer.PrinterHostAddress}' | Network: {printer.IsNetworkPrinter} | UNC: '{printer.UncPath}'");

                _package.Printers.Add(printer);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Printer enumeration failed: {ex.Message}");
        }

        result.ItemsTotal = _package.Printers.Count;
        result.ItemsProcessed = _package.Printers.Count;
        result.Status = result.Errors.Count > 0
            ? (_package.Printers.Count > 0 ? CategoryStatus.PartialSuccess : CategoryStatus.Failed)
            : CategoryStatus.Success;

        progress.Report($"Found {_package.Printers.Count} printer(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static readonly Regex IpRegex = new(@"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b", RegexOptions.Compiled);

    private static async Task<string> ResolveHostAddress(string portName, string printerName, CancellationToken ct, IProgress<string>? progress = null)
    {
        if (string.IsNullOrEmpty(portName))
            return "";

        progress?.Report($"  Resolving address for '{printerName}' (port: {portName})...");

        // Method 1: IP directly in port name (standard TCP/IP ports like "IP_10.0.0.50")
        var ipMatch = IpRegex.Match(portName);
        if (ipMatch.Success)
            return ipMatch.Groups[1].Value;

        // Method 2: Query PrinterPort for its host address (works for standard TCP/IP ports)
        try
        {
            var safeName = portName.Replace("'", "''");
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"(Get-PrinterPort -Name '{safeName}' -ErrorAction SilentlyContinue).PrinterHostAddress\"",
                ct, 10_000);
            var address = stdout?.Trim();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(address))
                return address;
        }
        catch { }

        // Method 3: Query the WSD/vendor port's DeviceURL (contains IP for network printers)
        try
        {
            var safeName = portName.Replace("'", "''");
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"(Get-PrinterPort -Name '{safeName}' -ErrorAction SilentlyContinue).DeviceURL\"",
                ct, 10_000);
            var deviceUrl = stdout?.Trim();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(deviceUrl))
            {
                var urlIp = IpRegex.Match(deviceUrl);
                if (urlIp.Success)
                    return urlIp.Groups[1].Value;
            }
        }
        catch { }

        // Method 4: Query MSFT_PrinterPort CIM class (different from Get-PrinterPort)
        try
        {
            var safeName = portName.Replace("'", "''");
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "powershell",
                $"-NoProfile -Command \"Get-CimInstance -Namespace root/StandardCimv2 -ClassName MSFT_PrinterPort -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -eq '{safeName}' }} | Select-Object -ExpandProperty PrinterHostAddress\"",
                ct, 10_000);
            var address = stdout?.Trim();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(address))
                return address;
        }
        catch { }

        // Method 5: Get port name from Win32_Printer, then resolve via Win32_TCPIPPrinterPort
        try
        {
            // Step 1: Get ALL printers and their port names (no filter = no escaping risk)
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "powershell",
                "-NoProfile -Command \"Get-CimInstance -ClassName Win32_Printer | Select-Object Name,PortName | ConvertTo-Json -Depth 2\"",
                ct, 10_000);

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                // Step 2: Find this printer's port name from the JSON
                using var doc = System.Text.Json.JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                var printers = root.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? root.EnumerateArray().ToList()
                    : new List<System.Text.Json.JsonElement> { root };

                string? matchedPort = null;
                foreach (var p in printers)
                {
                    var pName = p.TryGetProperty("Name", out var n) ? n.GetString() : null;
                    if (pName != null && pName.Equals(printerName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedPort = p.TryGetProperty("PortName", out var pn) ? pn.GetString() : null;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(matchedPort))
                {
                    // Step 3: Resolve the port to an IP via Win32_TCPIPPrinterPort
                    var safePort = matchedPort.Replace("'", "''");
                    var (exitCode2, stdout2, _) = await ProcessRunner.RunAsync(
                        "powershell",
                        $"-NoProfile -Command \"(Get-CimInstance -ClassName Win32_TCPIPPrinterPort -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -eq '{safePort}' }}).HostAddress\"",
                        ct, 10_000);
                    var address = stdout2?.Trim();
                    if (exitCode2 == 0 && !string.IsNullOrWhiteSpace(address))
                        return address;
                }
            }
        }
        catch { }

        // Method 6: For hostname-style ports, resolve via DNS/ping
        // Only for ports that look like hostnames (not UUIDs, not USB/LPT/COM)
        var hostPart = portName.Split(':')[0].Trim();
        if (!string.IsNullOrEmpty(hostPart) && hostPart.Length >= 4 &&
            !hostPart.StartsWith("USB", StringComparison.OrdinalIgnoreCase) &&
            !hostPart.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) &&
            !hostPart.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            // Strip WSD- prefix if present, then check if remainder is a UUID
            var stripped = hostPart.StartsWith("WSD-", StringComparison.OrdinalIgnoreCase)
                ? hostPart[4..] : hostPart;
            var looksLikeUuid = Guid.TryParse(stripped, out _) ||
                (stripped.Length >= 32 && stripped.Count(c => c == '-') >= 3);

            if (!looksLikeUuid)
            {
                try
                {
                    var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                        "ping", $"-4 -n 1 -w 2000 {hostPart}", ct, 5_000);
                    if (exitCode == 0)
                    {
                        var pingIp = IpRegex.Match(stdout ?? "");
                        if (pingIp.Success)
                            return pingIp.Groups[1].Value;
                    }
                }
                catch { }
            }
        }

        // No IP found — restore will create a manual action with port details
        progress?.Report($"  No IP address resolved for '{printerName}'.");
        return "";
    }
}
