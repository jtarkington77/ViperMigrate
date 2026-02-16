using Microsoft.Win32;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class ShortcutCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public ShortcutCollector(MigrationPackage package) => _package = package;

    public string Category => "Shortcuts";

    public Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        var shortcutsDir = Path.Combine(packagePath, "Shortcuts");

        var desktopDir = Path.Combine(shortcutsDir, "Desktop");
        var startMenuDir = Path.Combine(shortcutsDir, "StartMenu");
        var taskbarDir = Path.Combine(shortcutsDir, "Taskbar");
        var startupDir = Path.Combine(shortcutsDir, "Startup");
        Directory.CreateDirectory(desktopDir);
        Directory.CreateDirectory(startMenuDir);
        Directory.CreateDirectory(taskbarDir);
        Directory.CreateDirectory(startupDir);

        // 1. User Desktop
        progress.Report("Capturing desktop shortcuts...");
        CopyShortcutsFlat(PathResolver.Desktop, desktopDir, s =>
        {
            s.IsDesktopShortcut = true;
            s.Location = "User Desktop";
        }, result, ct);

        // 2. Public Desktop
        CopyShortcutsFlat(PathResolver.PublicDesktop, desktopDir, s =>
        {
            s.IsDesktopShortcut = true;
            s.Location = "Public Desktop";
        }, result, ct);

        // 3. User Start Menu (preserve folder structure)
        progress.Report("Capturing Start Menu shortcuts...");
        CopyStartMenuRecursive(PathResolver.UserStartMenuPrograms, startMenuDir, "", s =>
        {
            s.IsStartMenu = true;
            s.Location = "User Start Menu";
        }, result, ct);

        // 4. Public/Common Start Menu
        CopyStartMenuRecursive(PathResolver.CommonStartMenuPrograms, startMenuDir, "", s =>
        {
            s.IsStartMenu = true;
            s.Location = "Common Start Menu";
        }, result, ct);

        // 5. Taskbar — legacy path
        CopyShortcutsFlat(PathResolver.TaskbarPinsPath, taskbarDir, s =>
        {
            s.IsTaskbarPinned = true;
            s.Location = "Taskbar";
        }, result, ct);

        // 6. Quick Launch shortcuts (top-level only, skip User Pinned subfolder)
        CopyShortcutsFlat(PathResolver.QuickLaunchPath, taskbarDir, s =>
        {
            s.IsQuickLaunch = true;
            s.Location = "Quick Launch";
        }, result, ct);

        // 7. ImplicitAppShortcuts (modern taskbar pins)
        if (Directory.Exists(PathResolver.ImplicitAppShortcutsPath))
        {
            try
            {
                foreach (var subDir in Directory.GetDirectories(PathResolver.ImplicitAppShortcutsPath))
                {
                    ct.ThrowIfCancellationRequested();
                    CopyShortcutsFlat(subDir, taskbarDir, s =>
                    {
                        s.IsTaskbarPinned = true;
                        s.Location = "Taskbar (Implicit)";
                    }, result, ct);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error reading implicit taskbar shortcuts: {ex.Message}");
            }
        }

        // 7b. Taskband registry data (required for taskbar pin restoration on Win10/11)
        try
        {
            var taskbandPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
            var favorites = RegistryHelper.ReadBinary(Registry.CurrentUser, taskbandPath, "Favorites");
            if (favorites != null)
            {
                File.WriteAllBytes(Path.Combine(taskbarDir, "Taskband_Favorites.bin"), favorites);
            }

            var favoritesResolve = RegistryHelper.ReadBinary(Registry.CurrentUser, taskbandPath, "FavoritesResolve");
            if (favoritesResolve != null)
            {
                File.WriteAllBytes(Path.Combine(taskbarDir, "Taskband_FavoritesResolve.bin"), favoritesResolve);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Could not export Taskband registry data: {ex.Message}");
        }

        // 8. Startup folder shortcuts
        progress.Report("Capturing startup entries...");
        CopyShortcutsFlat(PathResolver.UserStartupFolder, startupDir, s =>
        {
            s.IsStartup = true;
            s.Location = "User Startup Folder";
        }, result, ct);

        CopyShortcutsFlat(PathResolver.CommonStartupFolder, startupDir, s =>
        {
            s.IsStartup = true;
            s.Location = "Common Startup Folder";
        }, result, ct);

        // 9. Registry startup entries (with enabled/disabled state from StartupApproved)
        var hkcuApproved = ReadStartupApproved(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        var hklmApproved = ReadStartupApproved(Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");

        CaptureRegistryStartup(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Registry-HKCU", true, hkcuApproved, result, ct);
        CaptureRegistryStartup(Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Registry-HKLM", false, hklmApproved, result, ct);

        var totalItems = _package.Shortcuts.Count + _package.StartupEntries.Count;
        result.ItemsTotal = totalItems;
        result.ItemsProcessed = totalItems;
        result.Status = result.Errors.Count > 0
            ? CategoryStatus.PartialSuccess
            : CategoryStatus.Success;

        progress.Report($"Captured {_package.Shortcuts.Count} shortcut(s), {_package.StartupEntries.Count} startup entry(ies).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }

    private void CopyShortcutsFlat(string sourceDir, string destDir, Action<CapturedShortcut> configure,
        CategoryResult result, CancellationToken ct)
    {
        if (!Directory.Exists(sourceDir))
            return;

        try
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*.lnk"))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);

                // Skip if same shortcut already captured from another source (e.g. User Desktop + Public Desktop)
                if (File.Exists(destFile))
                    continue;

                try
                {
                    File.Copy(file, destFile, true);
                    var shortcut = new CapturedShortcut
                    {
                        Name = Path.GetFileNameWithoutExtension(file)
                    };
                    configure(shortcut);
                    _package.Shortcuts.Add(shortcut);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to copy shortcut '{fileName}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Failed to enumerate shortcuts in '{sourceDir}': {ex.Message}");
        }
    }

    private void CopyStartMenuRecursive(string sourceDir, string destBase, string relativePath,
        Action<CapturedShortcut> configure, CategoryResult result, CancellationToken ct)
    {
        if (!Directory.Exists(sourceDir))
            return;

        // Skip the Startup subfolder — captured separately
        var dirName = Path.GetFileName(sourceDir);
        if (dirName.Equals("Startup", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(relativePath))
            return;

        try
        {
            var destDir = Path.Combine(destBase, relativePath);
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*.lnk"))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);

                try
                {
                    File.Copy(file, destFile, true);
                    var shortcut = new CapturedShortcut
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        StartMenuFolder = relativePath
                    };
                    configure(shortcut);
                    _package.Shortcuts.Add(shortcut);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to copy Start Menu shortcut '{fileName}': {ex.Message}");
                }
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var subRelative = string.IsNullOrEmpty(relativePath)
                    ? Path.GetFileName(dir)
                    : Path.Combine(relativePath, Path.GetFileName(dir));
                CopyStartMenuRecursive(dir, destBase, subRelative, configure, result, ct);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error capturing Start Menu from '{sourceDir}': {ex.Message}");
        }
    }

    private void CaptureRegistryStartup(RegistryKey baseKey, string subKeyPath, string source,
        bool isPerUser, Dictionary<string, bool> approvedState, CategoryResult result, CancellationToken ct)
    {
        var names = RegistryHelper.GetValueNames(baseKey, subKeyPath);
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var command = RegistryHelper.ReadString(baseKey, subKeyPath, name);
            if (!string.IsNullOrEmpty(command))
            {
                bool? isEnabled = approvedState.TryGetValue(name, out var approved) ? approved : null;
                _package.StartupEntries.Add(new CapturedStartupEntry
                {
                    Name = name,
                    Command = command,
                    Source = source,
                    IsPerUser = isPerUser,
                    IsEnabled = isEnabled
                });
            }
        }
    }

    /// <summary>
    /// Reads StartupApproved\Run registry key to determine enabled/disabled state.
    /// Each value is a binary blob where the first byte determines state:
    /// 02/06 = enabled, 03/07 = disabled.
    /// </summary>
    private static Dictionary<string, bool> ReadStartupApproved(RegistryKey baseKey, string subKeyPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            if (key == null) return result;

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] data && data.Length >= 1)
                {
                    // First byte: 02 or 06 = enabled, 03 or 07 = disabled
                    result[name] = (data[0] & 0x01) == 0; // Even = enabled, odd = disabled
                }
            }
        }
        catch { }
        return result;
    }
}
