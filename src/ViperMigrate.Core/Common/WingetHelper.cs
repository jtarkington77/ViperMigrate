using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Common;

public static class WingetHelper
{
    public static async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync("winget", "--version", ct, 10_000);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    public static async Task UpdateSourcesAsync(CancellationToken ct = default)
    {
        try
        {
            await ProcessRunner.RunAsync(
                "winget", "source update --accept-source-agreements",
                ct, 60_000);
        }
        catch
        {
            // Non-fatal — installs may still work with stale cache
        }
    }

    public static async Task<WingetExportResult> ExportAsync(string packagePath, CancellationToken ct = default)
    {
        var exportPath = Path.Combine(packagePath, "winget_export.json");
        try
        {
            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                "winget", $"export -o \"{exportPath}\" --accept-source-agreements",
                ct, 60_000);

            if (exitCode == 0 && File.Exists(exportPath))
            {
                var json = await File.ReadAllTextAsync(exportPath, ct);
                var data = JsonSerializer.Deserialize<WingetExportData>(json);
                var packageIds = data?.Sources?
                    .SelectMany(s => s.Packages ?? Enumerable.Empty<WingetExportPackage>())
                    .Select(p => p.PackageIdentifier)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList() ?? new List<string>();

                return new WingetExportResult { Success = true, PackageIds = packageIds! };
            }

            return new WingetExportResult { Success = false, Error = stderr.Trim() };
        }
        catch (Exception ex)
        {
            return new WingetExportResult { Success = false, Error = ex.Message };
        }
    }

    public static void MatchSoftwareToWinget(List<CapturedSoftware> software, List<string> packageIds)
    {
        // Build lookup structures for matching
        var exactLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in packageIds)
        {
            // Exact: "Google.Chrome" keyed by "Google Chrome" (dots replaced with spaces)
            var displayForm = id.Replace(".", " ");
            exactLookup.TryAdd(displayForm, id);

            // Normalized: strip common suffixes, lowercase
            var normalized = NormalizeName(displayForm);
            normalizedLookup.TryAdd(normalized, id);
        }

        foreach (var sw in software)
        {
            if (!string.IsNullOrEmpty(sw.WingetId))
                continue;

            var displayName = sw.DisplayName;

            // Strategy 1: Exact match (display name matches winget ID with dots as spaces)
            if (exactLookup.TryGetValue(displayName, out var exactMatch))
            {
                sw.WingetId = exactMatch;
                continue;
            }

            // Strategy 2: Normalized match
            var normalizedDisplay = NormalizeName(displayName);
            if (!string.IsNullOrEmpty(normalizedDisplay) && normalizedLookup.TryGetValue(normalizedDisplay, out var normalizedMatch))
            {
                sw.WingetId = normalizedMatch;
                continue;
            }

            // Strategy 3: Fuzzy substring — winget ID segments found in display name
            foreach (var id in packageIds)
            {
                var segments = id.Split('.');
                if (segments.Length < 2) continue;

                // Use the last segment (usually the product name) for matching
                var productName = segments[^1];
                if (productName.Length >= 4 &&
                    displayName.Contains(productName, StringComparison.OrdinalIgnoreCase))
                {
                    // Also verify the publisher segment matches if available
                    var publisher = segments[0];
                    if (displayName.Contains(publisher, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(sw.Publisher) &&
                         sw.Publisher.Contains(publisher, StringComparison.OrdinalIgnoreCase)))
                    {
                        sw.WingetId = id;
                        break;
                    }
                }
            }
        }
    }

    public static async Task<bool> IsInstalledAsync(string wingetId, CancellationToken ct = default)
    {
        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "winget", $"list --id \"{wingetId}\" -e --accept-source-agreements",
                ct, 30_000);
            return exitCode == 0 && stdout.Contains(wingetId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<WingetInstallResult> InstallAsync(
        string wingetId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var timeoutMs = LargePackages.Contains(wingetId) ? 1_800_000 : 900_000; // 30 or 15 min
        var timeoutDesc = LargePackages.Contains(wingetId) ? "30 minutes" : "15 minutes";
        var command = $"install --id \"{wingetId}\" -e --silent --accept-source-agreements --accept-package-agreements";

        try
        {
            progress?.Report($"Installing {wingetId}...");
            var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(
                "winget", command, ct, timeoutMs);

            if (exitCode == 0)
            {
                progress?.Report($"Successfully installed {wingetId}.");
                return new WingetInstallResult { Success = true, WingetId = wingetId };
            }

            // Check for "already installed" message
            if (stdout.Contains("already installed", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("already installed", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report($"{wingetId} is already installed.");
                return new WingetInstallResult { Success = true, WingetId = wingetId, AlreadyInstalled = true };
            }

            progress?.Report($"Failed to install {wingetId}.");
            var errorOutput = FilterProgressNoise(!string.IsNullOrWhiteSpace(stderr) ? stderr : stdout);
            return new WingetInstallResult
            {
                Success = false,
                WingetId = wingetId,
                Error = !string.IsNullOrWhiteSpace(errorOutput)
                    ? errorOutput
                    : $"winget exited with code {exitCode}. Command: winget {command}"
            };
        }
        catch (TimeoutException)
        {
            return new WingetInstallResult
            {
                Success = false,
                WingetId = wingetId,
                Error = $"Installation timed out after {timeoutDesc}. Try manually: winget {command}"
            };
        }
        catch (Exception ex)
        {
            return new WingetInstallResult
            {
                Success = false,
                WingetId = wingetId,
                Error = ex.Message
            };
        }
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> ImportAsync(
        string exportFilePath, CancellationToken ct = default)
    {
        return await ProcessRunner.RunAsync(
            "winget",
            $"import -i \"{exportFilePath}\" --accept-package-agreements --accept-source-agreements --ignore-unavailable",
            ct, 600_000); // 10 minute timeout
    }

    private static readonly Dictionary<string, string> KnownWingetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browsers
        { "Google Chrome", "Google.Chrome" },
        { "Mozilla Firefox", "Mozilla.Firefox" },
        { "Vivaldi", "Vivaldi.Vivaldi" },
        { "Brave", "Brave.Brave" },
        { "Opera Stable", "Opera.Opera" },
        { "Opera GX", "Opera.OperaGX" },
        { "Microsoft Edge", "Microsoft.Edge" },
        // Common apps
        { "7-Zip", "7zip.7zip" },
        { "Git", "Git.Git" },
        { "Docker Desktop", "Docker.DockerDesktop" },
        { "Visual Studio Code", "Microsoft.VisualStudioCode" },
        { "VS Code", "Microsoft.VisualStudioCode" },
        { "Python 3", "Python.Python.3.12" },
        { "Python", "Python.Python.3.12" },
        { "Node.js", "OpenJS.NodeJS" },
        { "Notepad++", "Notepad++.Notepad++" },
        { "VLC media player", "VideoLAN.VLC" },
        { "VLC", "VideoLAN.VLC" },
        { "PuTTY", "PuTTY.PuTTY" },
        { "WinSCP", "WinSCP.WinSCP" },
        { "FileZilla", "TimKosse.FileZilla.Client" },
        { "Zoom", "Zoom.Zoom" },
        { "Slack", "SlackTechnologies.Slack" },
        { "Discord", "Discord.Discord" },
        { "KeePass", "DominikReichl.KeePass" },
        { "Greenshot", "Greenshot.Greenshot" },
        { "Everything", "voidtools.Everything" },
        { "PowerToys", "Microsoft.PowerToys" },
        { "Windows Terminal", "Microsoft.WindowsTerminal" },
        { "OBS Studio", "OBSProject.OBSStudio" },
        { "Audacity", "Audacity.Audacity" },
        { "GIMP", "GIMP.GIMP" },
        { "Inkscape", "Inkscape.Inkscape" },
        { "LibreOffice", "TheDocumentFoundation.LibreOffice" },
        { "Wireshark", "WiresharkFoundation.Wireshark" },
        { "WinRAR", "RARLab.WinRAR" },
        { "Bitwarden", "Bitwarden.Bitwarden" },
        { "1Password", "AgileBits.1Password" },
        { "Spotify", "Spotify.Spotify" },
        { "Steam", "Valve.Steam" },
        { "ShareX", "ShareX.ShareX" },
        { "Postman", "Postman.Postman" },
        { "Insomnia", "Kong.Insomnia" },
        { "Beyond Compare", "ScooterSoftware.BeyondCompare5" },
        { "WinMerge", "WinMerge.WinMerge" },
    };

    private static readonly HashSet<string> LargePackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unity.UnityHub", "Anaconda.Anaconda3", "Anaconda.Miniconda3",
        "Microsoft.VisualStudio.2022.Community", "Microsoft.VisualStudio.2022.Professional",
        "Microsoft.VisualStudio.2022.Enterprise", "Docker.DockerDesktop",
        "JetBrains.IntelliJIDEA.Ultimate", "JetBrains.IntelliJIDEA.Community",
        "JetBrains.Rider", "JetBrains.PyCharm.Professional", "JetBrains.WebStorm",
        "JetBrains.CLion", "JetBrains.GoLand", "JetBrains.DataGrip",
        "EpicGames.EpicGamesLauncher", "Valve.Steam",
        "Microsoft.SQLServer.2022.Developer", "Microsoft.SQLServer.2022.Express",
        "Oracle.VirtualBox", "VMware.WorkstationPlayer"
    };

    public static void ApplyKnownWingetIds(List<CapturedSoftware> software)
    {
        foreach (var sw in software)
        {
            if (!string.IsNullOrEmpty(sw.WingetId))
                continue;

            foreach (var (pattern, wingetId) in KnownWingetIds)
            {
                if (sw.DisplayName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    sw.WingetId = wingetId;
                    break;
                }
            }
        }
    }

    public static async Task<List<WingetListEntry>> ListInstalledAsync(CancellationToken ct = default)
    {
        var entries = new List<WingetListEntry>();
        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "winget", "list --accept-source-agreements --disable-interactivity",
                ct, 60_000);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return entries;

            entries = ParseWingetListOutput(stdout);
        }
        catch
        {
            // Non-fatal — matching will proceed without list data
        }
        return entries;
    }

    public static List<WingetListEntry> ParseWingetListOutput(string output)
    {
        var entries = new List<WingetListEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Find header line containing "Name" and "Id" and "Version"
        int headerIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains("Name", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("Id", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("Version", StringComparison.OrdinalIgnoreCase))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
            return entries;

        var header = lines[headerIndex];
        var idCol = header.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
        var versionCol = header.IndexOf("Version", StringComparison.OrdinalIgnoreCase);

        if (idCol < 0 || versionCol < 0)
            return entries;

        // Skip separator line (dashes)
        int dataStart = headerIndex + 1;
        if (dataStart < lines.Length && lines[dataStart].TrimStart().StartsWith("-"))
            dataStart++;

        for (int i = dataStart; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length < versionCol + 1)
                continue;

            // Skip progress noise lines
            if (line.TrimStart().StartsWith("-") || line.Contains("[=") || line.Contains("█"))
                continue;

            var name = line[..Math.Min(idCol, line.Length)].Trim();
            var idEnd = Math.Min(versionCol, line.Length);
            var id = line[Math.Min(idCol, line.Length)..idEnd].Trim();
            var version = line[Math.Min(versionCol, line.Length)..].Trim();

            // Trim version at first whitespace (there may be "Available" and "Source" columns after)
            var spaceInVersion = version.IndexOf(' ');
            if (spaceInVersion > 0)
                version = version[..spaceInVersion];

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id) && id.Contains('.'))
            {
                entries.Add(new WingetListEntry { Name = name, Id = id, Version = version });
            }
        }

        return entries;
    }

    public static void MatchSoftwareToWingetList(List<CapturedSoftware> software, List<WingetListEntry> entries)
    {
        if (entries.Count == 0) return;

        // Build lookups
        var exactNameLookup = new Dictionary<string, WingetListEntry>(StringComparer.OrdinalIgnoreCase);
        var normalizedLookup = new Dictionary<string, WingetListEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            exactNameLookup.TryAdd(entry.Name, entry);
            var normalized = NormalizeName(entry.Name);
            if (!string.IsNullOrEmpty(normalized))
                normalizedLookup.TryAdd(normalized, entry);
        }

        foreach (var sw in software)
        {
            if (!string.IsNullOrEmpty(sw.WingetId))
                continue;

            // Strategy 1: Exact name match
            if (exactNameLookup.TryGetValue(sw.DisplayName, out var exact))
            {
                sw.WingetId = exact.Id;
                continue;
            }

            // Strategy 2: Normalized name match
            var normalizedDisplay = NormalizeName(sw.DisplayName);
            if (!string.IsNullOrEmpty(normalizedDisplay) && normalizedLookup.TryGetValue(normalizedDisplay, out var normalized))
            {
                sw.WingetId = normalized.Id;
                continue;
            }

            // Strategy 3: Containment match — entry name contains display name or vice versa
            foreach (var entry in entries)
            {
                if (entry.Name.Length >= 4 && sw.DisplayName.Length >= 4)
                {
                    if (entry.Name.Contains(sw.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                        sw.DisplayName.Contains(entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        sw.WingetId = entry.Id;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Search winget for a package matching the given display name.
    /// Universal fallback — works for any app winget knows about.
    /// </summary>
    public static async Task<string?> SearchWingetIdAsync(string displayName, CancellationToken ct)
    {
        var searchName = NormalizeName(displayName);
        if (string.IsNullOrWhiteSpace(searchName) || searchName.Length < 3)
            return null;

        // Use first 2-3 meaningful words to avoid overly narrow searches
        var words = searchName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .Take(3)
            .ToArray();

        if (words.Length == 0)
            return null;

        var query = string.Join(' ', words);

        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync(
                "winget",
                $"search --name \"{query}\" --count 5 --accept-source-agreements --disable-interactivity",
                ct, 15_000);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return null;

            var entries = ParseWingetSearchOutput(stdout);
            if (entries.Count == 0)
                return null;

            var normalizedDisplay = NormalizeName(displayName);

            // Priority 1: Exact normalized name match
            foreach (var entry in entries)
            {
                if (NormalizeName(entry.Name).Equals(normalizedDisplay, StringComparison.OrdinalIgnoreCase))
                    return entry.Id;
            }

            // Priority 2: One name contains the other
            foreach (var entry in entries)
            {
                var normalizedEntry = NormalizeName(entry.Name);
                if (normalizedEntry.Length >= 4 && normalizedDisplay.Length >= 4)
                {
                    if (normalizedDisplay.Contains(normalizedEntry, StringComparison.OrdinalIgnoreCase) ||
                        normalizedEntry.Contains(normalizedDisplay, StringComparison.OrdinalIgnoreCase))
                        return entry.Id;
                }
            }

            // Priority 3: If there's exactly 1 result, it's probably right
            if (entries.Count == 1)
                return entries[0].Id;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Pattern-based winget ID resolution for common naming conventions.
    /// Resolves based on naming PATTERNS, not per-app hardcoding.
    /// </summary>
    public static string? ResolveByNamingPattern(string displayName, bool isX64)
    {
        // Pattern: "Microsoft Visual C++ {year/version} Redistributable"
        if (displayName.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) &&
            displayName.Contains("Redistributable", StringComparison.OrdinalIgnoreCase))
        {
            var arch = isX64 || displayName.Contains("x64", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";

            if (ContainsAny(displayName, "v14", "2022", "2019", "2017", "2015"))
                return $"Microsoft.VCRedist.2015+.{arch}";
            if (displayName.Contains("2013")) return $"Microsoft.VCRedist.2013.{arch}";
            if (displayName.Contains("2012")) return $"Microsoft.VCRedist.2012.{arch}";
            if (displayName.Contains("2010")) return $"Microsoft.VCRedist.2010.{arch}";
            if (displayName.Contains("2008")) return $"Microsoft.VCRedist.2008.{arch}";
            if (displayName.Contains("2005")) return $"Microsoft.VCRedist.2005.{arch}";
        }

        // Pattern: "Microsoft .NET {type} {version}"
        if (displayName.Contains(".NET", StringComparison.OrdinalIgnoreCase))
        {
            var verMatch = Regex.Match(displayName, @"(\d+)\.\d+");
            if (verMatch.Success)
            {
                var major = verMatch.Groups[1].Value;
                if (displayName.Contains("SDK", StringComparison.OrdinalIgnoreCase))
                    return $"Microsoft.DotNet.SDK.{major}";
                if (displayName.Contains("Desktop Runtime", StringComparison.OrdinalIgnoreCase))
                    return $"Microsoft.DotNet.DesktopRuntime.{major}";
                if (displayName.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase))
                    return $"Microsoft.DotNet.AspNetCore.{major}";
                if (displayName.Contains("Runtime", StringComparison.OrdinalIgnoreCase))
                    return $"Microsoft.DotNet.Runtime.{major}";
            }
        }

        return null;
    }

    /// <summary>
    /// Parse winget search output. Handles variable column widths and truncated names.
    /// </summary>
    public static List<WingetSearchEntry> ParseWingetSearchOutput(string output)
    {
        var results = new List<WingetSearchEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Find the header line with "Name" and "Id" columns
        int headerLine = -1;
        int nameCol = -1, idCol = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var nameIdx = line.IndexOf("Name", StringComparison.OrdinalIgnoreCase);
            var idIdx = line.IndexOf("Id", StringComparison.OrdinalIgnoreCase);

            if (nameIdx >= 0 && idIdx > nameIdx)
            {
                headerLine = i;
                nameCol = nameIdx;
                idCol = idIdx;
                break;
            }
        }

        if (headerLine < 0)
            return results;

        // Skip separator line (dashes)
        int dataStart = headerLine + 1;
        if (dataStart < lines.Length && lines[dataStart].TrimStart().StartsWith('-'))
            dataStart++;

        for (int i = dataStart; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length < idCol + 3)
                continue;
            if (line.TrimStart().StartsWith('-'))
                continue;

            var name = line[nameCol..Math.Min(idCol, line.Length)].TrimEnd();

            // ID: extract from idCol to next whitespace-delimited token
            var idAndRest = line[Math.Min(idCol, line.Length)..].TrimStart();
            var id = idAndRest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id) && id.Contains('.'))
            {
                // Remove truncation characters
                name = name.TrimEnd('\u2026', '.').TrimEnd(); // \u2026 = …
                results.Add(new WingetSearchEntry { Name = name, Id = id });
            }
        }

        return results;
    }

    private static bool ContainsAny(string text, params string[] patterns)
        => patterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string FilterProgressNoise(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return output;
        var lines = output.Split('\n')
            .Where(l => !l.Contains("[=") && !l.Contains("█") &&
                        !Regex.IsMatch(l.Trim(), @"^\d+%?\s*$"))
            .ToArray();
        return string.Join('\n', lines).Trim();
    }

    /// <summary>
    /// Strip version numbers, architecture info, and noise from display names.
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";

        // Remove parenthetical content: (x64), (64-bit), (KB5073031), etc.
        var result = Regex.Replace(name, @"\s*\([^)]*\)", "");

        // Remove version numbers: "7.56.2.0", "v14", "25.01", "- 14.50.35719"
        result = Regex.Replace(result, @"\s*-?\s*v?\d+(\.\d+)+\s*$", "");
        result = Regex.Replace(result, @"\s+v\d+\b", "");
        result = Regex.Replace(result, @"\s+\d{4,}$", ""); // trailing build numbers

        // Remove architecture suffixes
        result = Regex.Replace(result, @"\s*(x64|x86|64-bit|32-bit|amd64|arm64)\s*", " ",
            RegexOptions.IgnoreCase);

        // Remove "- en-us" language tags
        result = Regex.Replace(result, @"\s*-\s*[a-z]{2}-[a-z]{2}\s*$", "",
            RegexOptions.IgnoreCase);

        // Clean up whitespace
        result = Regex.Replace(result, @"\s+", " ").Trim();

        return result;
    }
}

public class WingetExportResult
{
    public bool Success { get; set; }
    public List<string> PackageIds { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}

public class WingetInstallResult
{
    public bool Success { get; set; }
    public string WingetId { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool AlreadyInstalled { get; set; }
}

public class WingetExportData
{
    [JsonPropertyName("Sources")]
    public List<WingetExportSource>? Sources { get; set; }
}

public class WingetExportSource
{
    [JsonPropertyName("Packages")]
    public List<WingetExportPackage>? Packages { get; set; }

    [JsonPropertyName("SourceDetails")]
    public WingetSourceDetails? SourceDetails { get; set; }
}

public class WingetExportPackage
{
    [JsonPropertyName("PackageIdentifier")]
    public string PackageIdentifier { get; set; } = string.Empty;
}

public class WingetSourceDetails
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
}

public class WingetListEntry
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public class WingetSearchEntry
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}
