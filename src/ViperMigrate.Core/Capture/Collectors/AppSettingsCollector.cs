using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class AppSettingsCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    /// <summary>Max size per app in bytes (50 MB default, 100 MB for known critical apps)</summary>
    private const long MaxAppSizeBytes = 50L * 1024 * 1024;
    private const long MaxCriticalAppSizeBytes = 100L * 1024 * 1024;

    private static readonly HashSet<string> CriticalAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thunderbird", "Code", "obs-studio", "obsidian", "Notion"
    };

    /// <summary>Folder names to always skip (case-insensitive)</summary>
    private static readonly HashSet<string> SkipFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browser/Electron cache
        "Cache", "Code Cache", "GPUCache", "GpuCache", "ShaderCache", "DawnCache",
        "blob_storage", "Service Worker", "CacheStorage", "ScriptCache",
        "DawnGraphiteCache", "DawnWebGPUCache",
        // General cache
        "cache", "cache2", "caches", "__pycache__",
        // Temp
        "Temp", "tmp",
        // Logs
        "logs", "Logs", "CrashDumps", "Crashpad",
        // Large data dirs
        "IndexedDB", "Local Storage", "Session Storage",
        // Package managers
        "node_modules", ".npm", ".nuget", "packages",
        // Build output
        "bin", "obj",
    };

    /// <summary>Top-level folders in %APPDATA% / %LOCALAPPDATA% to always skip (major apps handled by other collectors)</summary>
    private static readonly HashSet<string> SkipAppFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browsers — handled by BrowserCollector
        "Google", "Microsoft", "BraveSoftware", "Vivaldi", "Opera Software",
        "Mozilla", // Firefox handled by BrowserCollector
        // System/OS folders
        "Microsoft", "Windows", "Packages",
        // Very large / not useful
        "Steam", "Epic Games", "GOG.com",
        "NVIDIA", "NVIDIA Corporation", "AMD", "Intel",
        "Docker", "Containers",
        // Game-related
        "Riot Games", "Blizzard Entertainment", "EA", "Ubisoft",
        "Mojang", "miHoYo", "Wargaming.net", "Rockstar Games",
        "Square Enix", "2K Games", "Paradox Interactive",
        // Package managers / runtimes
        "npm", "pip", "NuGet", "dotnet", "Python", "Go", "Rust", "cargo",
        "node_modules",
    };

    /// <summary>File extensions to skip</summary>
    private static readonly HashSet<string> SkipExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".tmp", ".bak", ".old", ".dmp", ".etl",
    };

    public AppSettingsCollector(MigrationPackage package) => _package = package;

    public string Category => "App Settings";

    public async Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        var settings = new CapturedAppSettings();
        var baseDir = Path.Combine(packagePath, "AppSettings");
        Directory.CreateDirectory(baseDir);

        // Build software inventory lookup for matching
        var softwareNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _package.Software)
        {
            if (!string.IsNullOrEmpty(s.DisplayName))
                softwareNames.Add(s.DisplayName);
        }

        // 1) Scan %APPDATA%
        progress.Report("Scanning %APPDATA% for application configs...");
        await ScanDirectory(PathResolver.AppData, "AppData", baseDir, settings, softwareNames, progress, result, ct);

        // 2) Scan %LOCALAPPDATA%
        progress.Report("Scanning %LOCALAPPDATA% for application configs...");
        await ScanDirectory(PathResolver.LocalAppData, "LocalAppData", baseDir, settings, softwareNames, progress, result, ct);

        // 3) Scan %USERPROFILE% dotfiles
        progress.Report("Scanning user profile for dotfiles...");
        await ScanDotfiles(PathResolver.UserProfile, baseDir, settings, softwareNames, progress, result, ct);

        // 4) Scan HKCU\Software for registry-based app configs
        progress.Report("Scanning HKCU\\Software for application registry data...");
        await ScanRegistry(baseDir, settings, softwareNames, progress, result, ct);

        _package.AppSettings = settings;

        result.ItemsTotal = settings.CapturedApps.Count;
        result.ItemsProcessed = settings.CapturedApps.Count;
        result.Status = settings.CapturedApps.Count > 0 ? CategoryStatus.Success : CategoryStatus.Skipped;
        progress.Report($"Captured settings for {settings.CapturedApps.Count} application(s).");

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static Task ScanDirectory(string rootDir, string sourceType, string baseDir,
        CapturedAppSettings settings, HashSet<string> softwareNames,
        IProgress<string> progress, CategoryResult result, CancellationToken ct)
    {
        if (!Directory.Exists(rootDir))
            return Task.CompletedTask;

        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(rootDir);
        }
        catch
        {
            return Task.CompletedTask;
        }

        foreach (var subdir in subdirs)
        {
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(subdir);

            // Skip known exclusions
            if (SkipAppFolderNames.Contains(folderName))
                continue;

            // Skip hidden system folders starting with '.'
            if (folderName.StartsWith('.'))
                continue;

            // Check if this folder has meaningful config files (not just empty or cache-only)
            if (!HasConfigContent(subdir))
                continue;

            // Calculate size (with cache-skip)
            var (totalSize, fileCount) = CalculateSize(subdir, ct);

            if (fileCount == 0)
                continue;

            var sizeLimit = CriticalAppNames.Contains(folderName) ? MaxCriticalAppSizeBytes : MaxAppSizeBytes;
            if (totalSize > sizeLimit)
            {
                result.Warnings.Add($"Skipped '{folderName}' ({sourceType}): {totalSize / (1024 * 1024)}MB exceeds {sizeLimit / (1024 * 1024)}MB limit.");
                continue;
            }

            // Copy to package
            var safeName = SanitizeFolderName(folderName);
            // Disambiguate AppData vs LocalAppData
            var destFolderName = sourceType == "LocalAppData" ? $"{safeName}_Local" : safeName;
            var destDir = Path.Combine(baseDir, destFolderName);

            // Avoid name collisions
            if (Directory.Exists(destDir))
                destDir = Path.Combine(baseDir, $"{destFolderName}_{Guid.NewGuid():N8}");

            try
            {
                progress.Report($"Capturing {folderName} ({sourceType})...");
                CopyDirectoryFiltered(subdir, destDir, ct);

                var matched = FuzzyMatchSoftware(folderName, softwareNames);

                settings.CapturedApps.Add(new CapturedAppConfig
                {
                    AppName = folderName,
                    SourceType = sourceType,
                    SourcePath = subdir,
                    PackageFolder = Path.GetFileName(destDir),
                    SizeBytes = totalSize,
                    FileCount = fileCount,
                    MatchedToInventory = matched
                });
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to capture '{folderName}' ({sourceType}): {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task ScanDotfiles(string userProfile, string baseDir,
        CapturedAppSettings settings, HashSet<string> softwareNames,
        IProgress<string> progress, CategoryResult result, CancellationToken ct)
    {
        if (!Directory.Exists(userProfile))
            return Task.CompletedTask;

        // Scan dotfiles (files starting with '.')
        try
        {
            foreach (var file in Directory.GetFiles(userProfile, ".*"))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);

                // Skip very common non-app files and backup artifacts
                if (fileName is ".DS_Store" or ".localized")
                    continue;
                if (fileName.StartsWith(".claude.json.backup", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith(".claude.json.corrupted", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > MaxAppSizeBytes)
                    continue;
                if (fileInfo.Length == 0)
                    continue;

                try
                {
                    // Name the folder after the dotfile, strip leading dot
                    var appName = fileName.TrimStart('.');
                    var safeName = SanitizeFolderName(appName);
                    var destDir = Path.Combine(baseDir, $"dotfile_{safeName}");
                    Directory.CreateDirectory(destDir);
                    File.Copy(file, Path.Combine(destDir, fileName), true);

                    progress.Report($"Capturing dotfile {fileName}...");

                    settings.CapturedApps.Add(new CapturedAppConfig
                    {
                        AppName = fileName,
                        SourceType = "UserProfile",
                        SourcePath = file,
                        PackageFolder = Path.GetFileName(destDir),
                        SizeBytes = fileInfo.Length,
                        FileCount = 1,
                        MatchedToInventory = FuzzyMatchSoftware(appName, softwareNames)
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to capture dotfile '{fileName}': {ex.Message}");
                }
            }

            // Scan dot-directories (e.g. .ssh, .aws, .config)
            foreach (var dir in Directory.GetDirectories(userProfile, ".*"))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = Path.GetFileName(dir);

                // Skip system/git large dirs
                if (dirName is ".nuget" or ".dotnet" or ".npm" or ".cargo" or ".rustup"
                    or ".gradle" or ".m2" or ".cache" or ".local" or ".vscode"
                    or ".docker" or ".kube")
                    continue;

                var (totalSize, fileCount) = CalculateSize(dir, ct);
                if (fileCount == 0 || totalSize > MaxAppSizeBytes)
                    continue;

                try
                {
                    var appName = dirName.TrimStart('.');
                    var safeName = SanitizeFolderName(appName);
                    var destDir = Path.Combine(baseDir, $"dotdir_{safeName}");
                    CopyDirectoryFiltered(dir, destDir, ct);

                    progress.Report($"Capturing {dirName}...");

                    settings.CapturedApps.Add(new CapturedAppConfig
                    {
                        AppName = dirName,
                        SourceType = "UserProfile",
                        SourcePath = dir,
                        PackageFolder = Path.GetFileName(destDir),
                        SizeBytes = totalSize,
                        FileCount = fileCount,
                        MatchedToInventory = FuzzyMatchSoftware(appName, softwareNames)
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to capture dotdir '{dirName}': {ex.Message}");
                }
            }
        }
        catch
        {
            // Access denied to user profile dir
        }

        return Task.CompletedTask;
    }

    private static async Task ScanRegistry(string baseDir,
        CapturedAppSettings settings, HashSet<string> softwareNames,
        IProgress<string> progress, CategoryResult result, CancellationToken ct)
    {
        // Known registry-based app configs worth exporting
        var registryTargets = new (string SubKey, string AppName)[]
        {
            (@"Software\SimonTatham\PuTTY", "PuTTY"),
            (@"Software\Martin Prikryl\WinSCP 2", "WinSCP"),
            (@"Software\Mobatek\MobaXterm", "MobaXterm"),
            (@"Software\KeePass", "KeePass"),
            (@"Software\FlexibleSoft\TeraCopy", "TeraCopy"),
        };

        foreach (var (subKey, appName) in registryTargets)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey);
                if (key == null)
                    continue;

                var safeName = SanitizeFolderName(appName);
                var destDir = Path.Combine(baseDir, $"reg_{safeName}");
                Directory.CreateDirectory(destDir);

                var regFile = Path.Combine(destDir, $"{safeName}.reg");
                var (exitCode, _, _) = await ProcessRunner.RunAsync(
                    "reg", $"export \"HKCU\\{subKey}\" \"{regFile}\" /y",
                    ct, 10_000);

                if (exitCode == 0 && File.Exists(regFile))
                {
                    var fileInfo = new FileInfo(regFile);
                    progress.Report($"Captured {appName} registry settings.");

                    settings.CapturedApps.Add(new CapturedAppConfig
                    {
                        AppName = appName,
                        SourceType = "Registry",
                        SourcePath = $"HKCU\\{subKey}",
                        PackageFolder = Path.GetFileName(destDir),
                        SizeBytes = fileInfo.Length,
                        FileCount = 1,
                        MatchedToInventory = FuzzyMatchSoftware(appName, softwareNames)
                    });
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to export {appName} registry: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Check if a directory has files that look like actual config (not just cache/logs).
    /// </summary>
    private static bool HasConfigContent(string dir)
    {
        try
        {
            // Look for common config file patterns in top level
            var configExtensions = new[] { ".json", ".xml", ".ini", ".cfg", ".conf", ".yaml", ".yml",
                                           ".toml", ".properties", ".reg", ".db", ".sqlite", ".plist",
                                           ".js", ".css", ".dat" };

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (configExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // Also check one level deep for config subdirs
            foreach (var subdir in Directory.EnumerateDirectories(dir).Take(20))
            {
                var subName = Path.GetFileName(subdir);
                if (SkipFolderNames.Contains(subName))
                    continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(subdir).Take(10))
                    {
                        var ext = Path.GetExtension(file);
                        if (configExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Calculate total size and file count, excluding cache folders and skip extensions.
    /// </summary>
    private static (long TotalSize, int FileCount) CalculateSize(string dir, CancellationToken ct)
    {
        long total = 0;
        int count = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file);
                if (SkipExtensions.Contains(ext))
                    continue;

                try
                {
                    var fi = new FileInfo(file);
                    total += fi.Length;
                    count++;
                }
                catch { }

                // Early exit if already over limit
                if (total > MaxAppSizeBytes)
                    return (total, count);
            }

            foreach (var subdir in Directory.EnumerateDirectories(dir))
            {
                ct.ThrowIfCancellationRequested();
                var subName = Path.GetFileName(subdir);
                if (SkipFolderNames.Contains(subName))
                    continue;

                var (subSize, subCount) = CalculateSize(subdir, ct);
                total += subSize;
                count += subCount;

                if (total > MaxAppSizeBytes)
                    return (total, count);
            }
        }
        catch { }

        return (total, count);
    }

    /// <summary>
    /// Copy directory recursively, skipping cache folders, skip extensions, and files over limit.
    /// </summary>
    private static void CopyDirectoryFiltered(string source, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file);
            if (SkipExtensions.Contains(ext))
                continue;

            try
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            }
            catch { } // Skip locked files silently
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(dir);
            if (SkipFolderNames.Contains(dirName))
                continue;

            CopyDirectoryFiltered(dir, Path.Combine(dest, dirName), ct);
        }
    }

    /// <summary>
    /// Fuzzy match a folder name against the software inventory.
    /// </summary>
    private static bool FuzzyMatchSoftware(string folderName, HashSet<string> softwareNames)
    {
        // Direct match
        if (softwareNames.Contains(folderName))
            return true;

        // Check if any software name contains the folder name or vice versa
        foreach (var name in softwareNames)
        {
            if (name.Contains(folderName, StringComparison.OrdinalIgnoreCase) ||
                folderName.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Trim('.', ' ');
    }
}
