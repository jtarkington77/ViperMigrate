using System.Diagnostics;
using Microsoft.Win32;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class ShortcutApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    public ShortcutApplicator(MigrationPackage package) => _package = package;

    public string Category => "Shortcuts";
    public RestoreStage Stage => RestoreStage.Configuration;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        var totalItems = _package.Shortcuts.Count + _package.StartupEntries.Count;
        result.ItemsTotal = totalItems;
        int restored = 0;
        int skippedNoTarget = 0;

        var isWin11 = OsHelper.IsWindows11(OsHelper.GetCurrentBuildNumber());

        var desktopSource = Path.Combine(packagePath, "Shortcuts", "Desktop");
        var startMenuSource = Path.Combine(packagePath, "Shortcuts", "StartMenu");
        var taskbarSource = Path.Combine(packagePath, "Shortcuts", "Taskbar");
        var startupSource = Path.Combine(packagePath, "Shortcuts", "Startup");

        // 1. Restore desktop shortcuts — skip if already exists (from installer)
        progress.Report("Restoring desktop shortcuts...");
        if (Directory.Exists(desktopSource))
        {
            // Build set of shortcut names already present on BOTH desktops
            var existingShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(PathResolver.Desktop))
                foreach (var existing in Directory.GetFiles(PathResolver.Desktop, "*.lnk"))
                    existingShortcuts.Add(Path.GetFileNameWithoutExtension(existing));
            if (Directory.Exists(PathResolver.PublicDesktop))
                foreach (var existing in Directory.GetFiles(PathResolver.PublicDesktop, "*.lnk"))
                    existingShortcuts.Add(Path.GetFileNameWithoutExtension(existing));

            foreach (var file in Directory.GetFiles(desktopSource, "*.lnk"))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var shortcutName = Path.GetFileNameWithoutExtension(file);

                // On Win11, skip IE shortcuts
                if (isWin11 && fileName.Contains("Internet Explorer", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip GUID-suffixed duplicates from old captures (e.g. "Thunderbird_a1b2c3d4.lnk")
                if (shortcutName.Length > 9 && shortcutName[^9] == '_' &&
                    shortcutName[^8..].All(c => char.IsLetterOrDigit(c)))
                {
                    var baseName = shortcutName[..^9];
                    if (existingShortcuts.Contains(baseName))
                        continue;
                }

                // Skip if this shortcut already exists on either desktop
                // (put there by the software installer during Stage 1)
                if (existingShortcuts.Contains(shortcutName))
                {
                    progress.Report($"  Skipping '{shortcutName}' — already on desktop.");
                    continue;
                }

                // Check if shortcut target exists
                var shortcutData = _package.Shortcuts.FirstOrDefault(s =>
                    s.IsDesktopShortcut && s.Name == shortcutName);

                if (shortcutData != null && !string.IsNullOrEmpty(shortcutData.TargetPath) &&
                    !File.Exists(shortcutData.TargetPath) && !Directory.Exists(shortcutData.TargetPath))
                {
                    skippedNoTarget++;
                    continue;
                }

                try
                {
                    File.Copy(file, Path.Combine(PathResolver.Desktop, fileName), true);
                    restored++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to restore desktop shortcut '{fileName}': {ex.Message}");
                }
            }
        }

        // 2. Restore Start Menu shortcuts (preserve folder structure)
        progress.Report("Restoring Start Menu shortcuts...");
        if (Directory.Exists(startMenuSource))
        {
            restored += CopyDirectoryRecursive(startMenuSource, PathResolver.UserStartMenuPrograms, result, ct);
        }

        // 3. Restore startup folder shortcuts
        progress.Report("Restoring startup shortcuts...");
        if (Directory.Exists(startupSource))
        {
            foreach (var file in Directory.GetFiles(startupSource, "*.lnk"))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                try
                {
                    File.Copy(file, Path.Combine(PathResolver.UserStartupFolder, fileName), true);
                    restored++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to restore startup shortcut '{fileName}': {ex.Message}");
                }
            }
        }

        // 4. Taskbar: copy .lnk files + import Taskband registry data
        progress.Report("Restoring taskbar pins...");
        bool taskbarChanged = false;
        if (Directory.Exists(taskbarSource))
        {
            var taskbarDest = PathResolver.TaskbarPinsPath;
            Directory.CreateDirectory(taskbarDest);

            foreach (var file in Directory.GetFiles(taskbarSource, "*.lnk"))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);

                // On Win11, skip IE shortcuts
                if (isWin11 && fileName.Contains("Internet Explorer", StringComparison.OrdinalIgnoreCase))
                    continue;

                var shortcutData = _package.Shortcuts.FirstOrDefault(s =>
                    s.IsTaskbarPinned && s.Name == Path.GetFileNameWithoutExtension(fileName));

                if (shortcutData != null && !string.IsNullOrEmpty(shortcutData.TargetPath) &&
                    !File.Exists(shortcutData.TargetPath) && !Directory.Exists(shortcutData.TargetPath))
                {
                    skippedNoTarget++;
                    continue;
                }

                try
                {
                    File.Copy(file, Path.Combine(taskbarDest, fileName), true);
                    restored++;
                    taskbarChanged = true;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to restore taskbar pin '{fileName}': {ex.Message}");
                }
            }

            // Import Taskband registry data
            var taskbandPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

            var favoritesFile = Path.Combine(taskbarSource, "Taskband_Favorites.bin");
            if (File.Exists(favoritesFile))
            {
                var data = File.ReadAllBytes(favoritesFile);
                if (RegistryHelper.SetBinary(Registry.CurrentUser, taskbandPath, "Favorites", data))
                    taskbarChanged = true;
            }

            var favoritesResolveFile = Path.Combine(taskbarSource, "Taskband_FavoritesResolve.bin");
            if (File.Exists(favoritesResolveFile))
            {
                var data = File.ReadAllBytes(favoritesResolveFile);
                RegistryHelper.SetBinary(Registry.CurrentUser, taskbandPath, "FavoritesResolve", data);
            }
        }

        // Restart explorer to apply taskbar changes
        if (taskbarChanged)
        {
            try
            {
                progress.Report("Restarting Explorer to apply taskbar pins...");
                await ProcessRunner.RunAsync("taskkill", "/f /im explorer.exe", ct, 5000);
                await Task.Delay(1000, ct);
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                await Task.Delay(2000, ct);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Explorer restart failed: {ex.Message}. Taskbar pins may require a restart.");
            }
        }

        // 5. Registry startup entries — auto-write HKCU, auto-write HKLM (may need admin)
        foreach (var entry in _package.StartupEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Source == "Registry-HKCU")
            {
                if (RegistryHelper.SetString(Registry.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\Run", entry.Name, entry.Command))
                {
                    // Set StartupApproved state (enabled/disabled)
                    SetStartupApproved(Registry.CurrentUser, entry.Name, entry.IsEnabled ?? true);
                    restored++;
                }
                else
                {
                    result.Warnings.Add($"Failed to add HKCU startup entry '{entry.Name}'.");
                }
            }
            else
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    key?.SetValue(entry.Name, entry.Command, RegistryValueKind.String);
                    SetStartupApproved(Registry.LocalMachine, entry.Name, entry.IsEnabled ?? true);
                    restored++;
                }
                catch
                {
                    result.Warnings.Add($"Could not add HKLM startup entry '{entry.Name}' (requires admin).");
                }
            }
        }

        if (skippedNoTarget > 0)
            result.Warnings.Add($"Skipped {skippedNoTarget} shortcut(s) whose target applications were not found.");

        result.ItemsProcessed = restored;
        result.Status = restored > 0 || totalItems == 0
            ? CategoryStatus.Success
            : CategoryStatus.PartialSuccess;

        progress.Report($"Restored {restored} shortcut(s)/startup entries.");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    /// <summary>
    /// Sets the enabled/disabled state for a startup entry in StartupApproved\Run.
    /// </summary>
    private static void SetStartupApproved(RegistryKey baseKey, string entryName, bool enabled)
    {
        try
        {
            var subKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            using var key = baseKey.CreateSubKey(subKeyPath);
            if (key == null) return;

            // StartupApproved binary format: 12 bytes, first byte = 02 (enabled) or 03 (disabled)
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            key.SetValue(entryName, data, RegistryValueKind.Binary);
        }
        catch { }
    }

    private static int CopyDirectoryRecursive(string source, string dest, CategoryResult result, CancellationToken ct)
    {
        int count = 0;
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source, "*.lnk"))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            try
            {
                File.Copy(file, Path.Combine(dest, fileName), true);
                count++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to restore Start Menu shortcut '{fileName}': {ex.Message}");
            }
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            count += CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)), result, ct);
        }

        return count;
    }
}
