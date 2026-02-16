using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class AppSettingsApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    // Files to never copy from dotfile backups
    private static readonly HashSet<string> DotfileSkipPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ".claude.json.backup", ".claude.json.corrupted"
    };

    // SSH files that are safe to copy (skip private keys)
    private static readonly HashSet<string> SshSafeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "known_hosts", "known_hosts.old", "authorized_keys"
    };

    // Self-exclusion: never kill our own process
    private static readonly int _selfPid = System.Diagnostics.Process.GetCurrentProcess().Id;

    public AppSettingsApplicator(MigrationPackage package) => _package = package;

    public string Category => "App Settings";
    public RestoreStage Stage => RestoreStage.Configuration;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        var settings = _package.AppSettings;
        if (settings == null || settings.CapturedApps.Count == 0)
        {
            result.Status = CategoryStatus.Skipped;
            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        var baseDir = Path.Combine(packagePath, "AppSettings");
        int restored = 0;
        int total = settings.CapturedApps.Count;

        // Kill recently installed apps to release file locks
        foreach (var installedSw in _package.Software.Where(s => s.CanAutoInstall))
        {
            KillMatchingProcesses(installedSw.DisplayName);
            if (!string.IsNullOrEmpty(installedSw.WingetId))
            {
                var shortName = installedSw.WingetId.Split('.').LastOrDefault() ?? "";
                if (!string.IsNullOrEmpty(shortName))
                    KillMatchingProcesses(shortName);
            }
        }
        // Wait for all processes to fully exit
        await Task.Delay(5000);

        foreach (var app in settings.CapturedApps)
        {
            ct.ThrowIfCancellationRequested();

            var srcDir = Path.Combine(baseDir, app.PackageFolder);
            if (!Directory.Exists(srcDir) && !File.Exists(srcDir))
            {
                result.Warnings.Add($"Package folder missing for '{app.AppName}'.");
                continue;
            }

            progress.Report($"Restoring {app.AppName} settings...");

            try
            {
                // Dotfiles get special handling — no "is app installed?" check
                if (IsDotfileEntry(app))
                {
                    restored += RestoreDotfile(srcDir, app, result, ct) ? 1 : 0;
                    continue;
                }

                if (app.SourceType == "Registry")
                {
                    restored += await RestoreRegistryApp(srcDir, app, result, ct) ? 1 : 0;
                }
                else
                {
                    restored += RestoreFileApp(srcDir, app, result, ct) ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to restore '{app.AppName}': {ex.Message}");
            }
        }

        result.ItemsTotal = total;
        result.ItemsProcessed = restored;
        result.Status = restored > 0 ? CategoryStatus.Success
            : (total > 0 ? CategoryStatus.PartialSuccess : CategoryStatus.Skipped);

        // Universal disclaimer — some apps generate unique profile paths/IDs on install
        result.Warnings.Add("Note: Not all application settings and accounts may transfer successfully due to how some applications store their configuration. Please verify your settings in each application after restore.");

        progress.Report($"Restored {restored}/{total} application setting(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static bool IsDotfileEntry(CapturedAppConfig app)
    {
        return app.PackageFolder.StartsWith("dotfile_", StringComparison.OrdinalIgnoreCase) ||
               app.PackageFolder.StartsWith("dotdir_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RestoreDotfile(string srcDir, CapturedAppConfig app, CategoryResult result, CancellationToken ct)
    {
        var userProfile = PathResolver.UserProfile;

        try
        {
            if (app.PackageFolder.StartsWith("dotfile_", StringComparison.OrdinalIgnoreCase))
            {
                // Single dotfile — copy files in srcDir to %USERPROFILE%
                foreach (var file in Directory.GetFiles(srcDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(file);

                    // Skip backup/corrupted files
                    if (DotfileSkipPatterns.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    File.Copy(file, Path.Combine(userProfile, fileName), true);
                }
                return true;
            }

            if (app.PackageFolder.StartsWith("dotdir_", StringComparison.OrdinalIgnoreCase))
            {
                // Dot-directory — copy to %USERPROFILE%/{app.AppName}
                var targetDir = Path.Combine(userProfile, app.AppName);

                // Special handling for .ssh — skip private keys
                if (app.AppName.Equals(".ssh", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(targetDir);
                    foreach (var file in Directory.GetFiles(srcDir))
                    {
                        ct.ThrowIfCancellationRequested();
                        var fileName = Path.GetFileName(file);

                        // Only copy safe files (config, known_hosts, *.pub)
                        if (SshSafeFiles.Contains(fileName) ||
                            fileName.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, Path.Combine(targetDir, fileName), true);
                        }
                    }
                    return true;
                }

                // General dot-directory
                CopyDirectoryRecursive(srcDir, targetDir, ct);
                return true;
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Failed to restore dotfile '{app.AppName}': {ex.Message}");
        }

        return false;
    }

    private static async Task<bool> RestoreRegistryApp(string srcDir, CapturedAppConfig app, CategoryResult result, CancellationToken ct)
    {
        var regFiles = Directory.GetFiles(srcDir, "*.reg");
        if (regFiles.Length == 0)
        {
            result.Warnings.Add($"No .reg file found for '{app.AppName}'.");
            return false;
        }

        var regFile = regFiles[0];
        var (exitCode, _, stderr) = await ProcessRunner.RunAsync(
            "reg", $"import \"{regFile}\"", ct, 10_000);

        if (exitCode == 0)
            return true;

        result.Warnings.Add($"Registry import for '{app.AppName}' failed: {stderr}");
        return false;
    }

    private static bool RestoreFileApp(string srcDir, CapturedAppConfig app, CategoryResult result, CancellationToken ct)
    {
        string? targetDir = ResolveTargetPath(app);
        if (targetDir == null)
        {
            result.ManualActions.Add(new ManualAction
            {
                Category = "App Settings",
                Title = $"Restore {app.AppName} settings",
                Description = $"Could not determine target path for '{app.AppName}'. " +
                              $"Original location: {app.SourcePath}. " +
                              $"Copy files manually from the package AppSettings/{app.PackageFolder}/ folder.",
                Priority = ManualActionPriority.Low
            });
            return false;
        }

        if (!Directory.Exists(targetDir))
        {
            // For AppData/LocalAppData entries, create the directory and copy anyway
            // Settings will be ready when the app launches
            if (app.SourceType is "AppData" or "LocalAppData")
            {
                try
                {
                    Directory.CreateDirectory(targetDir);
                }
                catch (Exception ex)
                {
                    result.ManualActions.Add(new ManualAction
                    {
                        Category = "App Settings",
                        Title = $"Restore {app.AppName} settings",
                        Description = $"Could not create target directory: {ex.Message}\n" +
                                      $"Install {app.AppName}, then copy settings from " +
                                      $"the package AppSettings/{app.PackageFolder}/ folder to {targetDir}.",
                        Priority = ManualActionPriority.Low
                    });
                    return false;
                }
            }
            else
            {
                result.ManualActions.Add(new ManualAction
                {
                    Category = "App Settings",
                    Title = $"Restore {app.AppName} settings",
                    Description = $"Install {app.AppName} first, then copy settings from " +
                                  $"the package AppSettings/{app.PackageFolder}/ folder to {targetDir}.",
                    Priority = ManualActionPriority.Low
                });
                return false;
            }
        }

        // Kill any running process that might lock files in the target directory
        // Many apps auto-launch after winget install and lock their config files
        KillMatchingProcesses(app.AppName);

        int failedCopies = 0;
        CopyDirectoryRecursive(srcDir, targetDir, ct, ref failedCopies);

        if (failedCopies > 0)
        {
            // Wait and retry failed files (often locked temporarily after process kill)
            Thread.Sleep(2000);
            int retryFailed = 0;
            CopyDirectoryRecursive(srcDir, targetDir, ct, ref retryFailed);
            failedCopies = retryFailed; // Update with final count after retry
        }

        if (failedCopies > 0)
        {
            result.Warnings.Add($"'{app.AppName}': {failedCopies} file(s) could not be copied (likely locked by running process). " +
                $"Close {app.AppName} and re-run restore, or copy manually from package.");
        }

        // After generic copy, handle profile-based apps (Thunderbird, Firefox-family)
        if (app.AppName.Equals("Thunderbird", StringComparison.OrdinalIgnoreCase))
        {
            FixThunderbirdProfiles(srcDir, targetDir, result);
        }

        return true;
    }

    /// <summary>
    /// Thunderbird stores profiles in random-hash folders (e.g. abc123.default-release).
    /// After copying, we need to ensure profiles.ini points to the restored profile,
    /// not a newly-created empty one.
    /// </summary>
    private static void FixThunderbirdProfiles(string srcDir, string targetDir, CategoryResult result)
    {
        try
        {
            var profilesIni = Path.Combine(targetDir, "profiles.ini");
            var srcProfilesDir = Path.Combine(srcDir, "Profiles");
            var targetProfilesDir = Path.Combine(targetDir, "Profiles");

            if (!Directory.Exists(srcProfilesDir) || !Directory.Exists(targetProfilesDir))
                return;

            // Find the source profile directory (the one with actual data)
            var srcProfiles = Directory.GetDirectories(srcProfilesDir)
                .Where(d => Directory.GetFiles(d, "prefs.js").Length > 0)
                .ToList();

            if (srcProfiles.Count == 0)
                return;

            var srcProfileFolder = Path.GetFileName(srcProfiles[0]); // e.g. "abc123.default-release"

            // Check if this profile exists in the target
            var restoredProfile = Path.Combine(targetProfilesDir, srcProfileFolder);
            if (!Directory.Exists(restoredProfile))
                return; // Nothing to fix — copy didn't create it

            // Find if there's a DIFFERENT (newer/empty) profile that Thunderbird created
            var targetProfiles = Directory.GetDirectories(targetProfilesDir)
                .Select(Path.GetFileName)
                .Where(n => n != srcProfileFolder && n!.Contains(".default", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Rewrite profiles.ini to point to the restored (data-containing) profile
            if (File.Exists(profilesIni))
            {
                var iniContent = File.ReadAllText(profilesIni);

                // Replace any reference to the new empty profile with the restored one
                foreach (var newProfile in targetProfiles)
                {
                    iniContent = iniContent.Replace(
                        $"Path=Profiles/{newProfile}",
                        $"Path=Profiles/{srcProfileFolder}",
                        StringComparison.OrdinalIgnoreCase);
                }

                // Ensure the restored profile is referenced
                if (!iniContent.Contains($"Path=Profiles/{srcProfileFolder}", StringComparison.OrdinalIgnoreCase))
                {
                    // Profile not referenced at all — add it
                    iniContent += $"\n[Profile0]\nName=default-release\nIsRelative=1\nPath=Profiles/{srcProfileFolder}\nDefault=1\n";
                }

                File.WriteAllText(profilesIni, iniContent);
            }

            // Delete the empty new profile to avoid confusion
            foreach (var newProfile in targetProfiles)
            {
                var emptyDir = Path.Combine(targetProfilesDir, newProfile!);
                try
                {
                    // Only delete if it has no prefs.js (i.e. it's a fresh empty profile)
                    if (Directory.GetFiles(emptyDir, "prefs.js").Length == 0)
                        Directory.Delete(emptyDir, true);
                }
                catch { }
            }

            result.Warnings.Add("Thunderbird profile path updated to restored profile.");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Thunderbird profile fix-up failed: {ex.Message}. Manual profile selection may be needed.");
        }
    }

    private static string? ResolveTargetPath(CapturedAppConfig app)
    {
        if (string.IsNullOrEmpty(app.SourcePath) || string.IsNullOrEmpty(app.AppName))
            return null;

        // Reconstruct path using current user's profile directories + app folder name
        // This handles cross-machine restores where source/target usernames differ
        return app.SourceType switch
        {
            "AppData" => Path.Combine(PathResolver.AppData, app.AppName),
            "LocalAppData" => Path.Combine(PathResolver.LocalAppData, app.AppName),
            "UserProfile" => Path.Combine(PathResolver.UserProfile, app.AppName),
            _ => null
        };
    }

    private static void CopyDirectoryRecursive(string source, string dest, CancellationToken ct)
    {
        int ignored = 0;
        CopyDirectoryRecursive(source, dest, ct, ref ignored);
    }

    private static void CopyDirectoryRecursive(string source, string dest, CancellationToken ct, ref int failedCopies)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            }
            catch
            {
                failedCopies++;
            }
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)), ct, ref failedCopies);
        }
    }

    /// <summary>
    /// Attempts to kill any process whose name matches the app folder name.
    /// Uses fuzzy matching — tries the folder name directly and common variations.
    /// No hardcoded app names — works for any app.
    /// </summary>
    private static void KillMatchingProcesses(string appFolderName)
    {
        if (string.IsNullOrEmpty(appFolderName) || appFolderName.Length < 3) return;

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            appFolderName,
            appFolderName.Replace(" ", ""),
        };

        bool killedAny = false;
        foreach (var name in candidates)
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(name);
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id == _selfPid) continue; // NEVER kill self
                        proc.Kill();
                        proc.WaitForExit(3000);
                        killedAny = true;
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }
            catch { }
        }

        // Also check running processes by module path — MUST exclude self
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (proc.Id == _selfPid) continue; // NEVER kill self
                    if (proc.MainModule?.FileName?.Contains(appFolderName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                        killedAny = true;
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }

        if (killedAny)
        {
            // Wait for file handles to fully release
            Thread.Sleep(2000);
        }
    }
}
