using System.Text.Json;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class BrowserCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public BrowserCollector(MigrationPackage package) => _package = package;

    public string Category => "Browsers";

    public Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        var browsersDir = Path.Combine(packagePath, "Browsers");
        Directory.CreateDirectory(browsersDir);

        // Dynamically detect all Chromium-based browsers
        var chromiumBrowsers = BrowserDetector.DetectChromiumBrowsers();
        foreach (var browser in chromiumBrowsers)
        {
            CaptureChromium(browser.Name, browser.UserDataPath, browsersDir, result, ct);
        }

        // Dynamically detect all Firefox-family browsers (excludes Thunderbird)
        var firefoxBrowsers = BrowserDetector.DetectFirefoxBrowsers();
        foreach (var browser in firefoxBrowsers)
        {
            CaptureFirefox(browser.Name, browser.UserDataPath, browsersDir, result, ct);
        }

        result.ItemsTotal = _package.BrowserProfiles.Count;
        result.ItemsProcessed = _package.BrowserProfiles.Count;
        result.Status = result.Errors.Count > 0
            ? (_package.BrowserProfiles.Count > 0 ? CategoryStatus.PartialSuccess : CategoryStatus.Failed)
            : (_package.BrowserProfiles.Count > 0 ? CategoryStatus.Success : CategoryStatus.Success);

        var totalPw = _package.BrowserProfiles.Sum(p => p.Passwords.Count);
        var pwMsg = totalPw > 0 ? $", {totalPw} saved password(s)" : "";
        progress.Report($"Captured {_package.BrowserProfiles.Count} browser profile(s){pwMsg}.");

        if (totalPw > 0)
        {
            result.ManualActions.Add(new ManualAction
            {
                Category = "Security",
                Title = "Package contains saved passwords",
                Description = $"{totalPw} password(s) were captured and stored in the migration package. " +
                    "DELETE the entire package folder after migration is complete to protect your credentials.",
                Priority = ManualActionPriority.Critical
            });
        }

        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }

    private void CaptureChromium(string browserName, string userDataPath, string browsersDir,
        CategoryResult result, CancellationToken ct)
    {
        if (!Directory.Exists(userDataPath))
            return;

        try
        {
            // Find profiles (Default, Profile 1, Profile 2, etc.)
            var profileDirs = Directory.GetDirectories(userDataPath)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                           || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                });

            foreach (var profileDir in profileDirs)
            {
                ct.ThrowIfCancellationRequested();
                var profileName = Path.GetFileName(profileDir);
                var profile = new CapturedBrowserProfile
                {
                    BrowserName = browserName,
                    ProfileName = profileName,
                    ProfilePath = profileDir
                };

                var destDir = Path.Combine(browsersDir, browserName, profileName);
                Directory.CreateDirectory(destDir);

                // Bookmarks
                var bookmarksFile = Path.Combine(profileDir, "Bookmarks");
                if (File.Exists(bookmarksFile))
                {
                    var destFile = Path.Combine(destDir, "Bookmarks");
                    File.Copy(bookmarksFile, destFile, true);
                    profile.BookmarksCaptured = true;
                    profile.BookmarksFile = destFile;
                }

                // Extensions — read manifest.json for human-readable names
                var extensionsDir = Path.Combine(profileDir, "Extensions");
                if (Directory.Exists(extensionsDir))
                {
                    var extensionInfos = new List<CapturedExtensionInfo>();
                    foreach (var extDir in Directory.GetDirectories(extensionsDir))
                    {
                        var extId = Path.GetFileName(extDir);
                        if (string.IsNullOrEmpty(extId))
                            continue;

                        profile.ExtensionIds.Add(extId);

                        var extInfo = new CapturedExtensionInfo { Id = extId };

                        // Find latest version subfolder
                        try
                        {
                            var versionDir = Directory.GetDirectories(extDir)
                                .OrderByDescending(d => Path.GetFileName(d))
                                .FirstOrDefault();

                            if (versionDir != null)
                            {
                                extInfo.Version = Path.GetFileName(versionDir);
                                var manifestPath = Path.Combine(versionDir, "manifest.json");
                                if (File.Exists(manifestPath))
                                {
                                    var name = ReadExtensionName(manifestPath, versionDir);
                                    if (!string.IsNullOrEmpty(name))
                                        extInfo.Name = name;
                                }
                            }
                        }
                        catch
                        {
                            // Non-fatal — keep the ID at minimum
                        }

                        extensionInfos.Add(extInfo);
                    }

                    profile.Extensions = extensionInfos;
                    profile.ExtensionsCaptured = profile.ExtensionIds.Count > 0;
                }

                // Saved passwords
                try
                {
                    var passwords = ChromiumPasswordDecryptor.ExtractPasswords(userDataPath, profileName, out var pwError);
                    if (passwords.Count > 0)
                    {
                        profile.Passwords = passwords;
                        profile.PasswordsCaptured = true;
                    }
                    if (pwError != null)
                        result.Warnings.Add($"{browserName} '{profileName}' passwords: {pwError}");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{browserName} '{profileName}' password capture failed: {ex.Message}");
                }

                _package.BrowserProfiles.Add(profile);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error capturing {browserName} profiles: {ex.Message}");
        }
    }

    private static string? ReadExtensionName(string manifestPath, string versionDir)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("name", out var nameProp))
                return null;

            var name = nameProp.GetString();
            if (string.IsNullOrEmpty(name))
                return null;

            // Handle Chrome's __MSG_ localized names
            if (name.StartsWith("__MSG_") && name.EndsWith("__"))
            {
                var msgKey = name[6..^2]; // strip __MSG_ and __
                name = ResolveLocalizedName(versionDir, msgKey) ?? name;
            }

            return name;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveLocalizedName(string versionDir, string msgKey)
    {
        // Try en, then en_US, then first available locale
        string[] localesToTry = { "en", "en_US", "en_GB" };

        foreach (var locale in localesToTry)
        {
            var messagesPath = Path.Combine(versionDir, "_locales", locale, "messages.json");
            var resolved = ReadMessageFromFile(messagesPath, msgKey);
            if (resolved != null) return resolved;
        }

        // Fallback: try any locale directory
        var localesDir = Path.Combine(versionDir, "_locales");
        if (!Directory.Exists(localesDir))
            return null;

        try
        {
            var firstLocale = Directory.GetDirectories(localesDir).FirstOrDefault();
            if (firstLocale != null)
            {
                var messagesPath = Path.Combine(firstLocale, "messages.json");
                return ReadMessageFromFile(messagesPath, msgKey);
            }
        }
        catch
        {
            // Non-fatal
        }

        return null;
    }

    private static string? ReadMessageFromFile(string messagesPath, string msgKey)
    {
        if (!File.Exists(messagesPath))
            return null;

        try
        {
            var json = File.ReadAllText(messagesPath);
            using var doc = JsonDocument.Parse(json);

            // Try exact key, then case-insensitive
            if (doc.RootElement.TryGetProperty(msgKey, out var msgObj))
            {
                if (msgObj.TryGetProperty("message", out var message))
                    return message.GetString();
            }

            // Case-insensitive fallback
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(msgKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.TryGetProperty("message", out var message))
                        return message.GetString();
                }
            }
        }
        catch
        {
            // Non-fatal
        }

        return null;
    }

    private void CaptureFirefox(string browserName, string profilesPath, string browsersDir,
        CategoryResult result, CancellationToken ct)
    {
        if (!Directory.Exists(profilesPath))
            return;

        try
        {
            foreach (var profileDir in Directory.GetDirectories(profilesPath))
            {
                ct.ThrowIfCancellationRequested();
                var profileName = Path.GetFileName(profileDir);
                var profile = new CapturedBrowserProfile
                {
                    BrowserName = browserName,
                    ProfileName = profileName,
                    ProfilePath = profileDir
                };

                var destDir = Path.Combine(browsersDir, browserName, profileName);
                Directory.CreateDirectory(destDir);

                // places.sqlite contains bookmarks and history
                var placesFile = Path.Combine(profileDir, "places.sqlite");
                if (File.Exists(placesFile))
                {
                    var destFile = Path.Combine(destDir, "places.sqlite");
                    try
                    {
                        File.Copy(placesFile, destFile, true);
                        profile.BookmarksCaptured = true;
                        profile.BookmarksFile = destFile;
                    }
                    catch (IOException ex)
                    {
                        result.Warnings.Add($"{browserName} '{profileName}': Could not copy places.sqlite (may be locked): {ex.Message}");
                    }
                }

                // Firefox passwords: copy logins.json + key4.db (needed together for import)
                var loginsFile = Path.Combine(profileDir, "logins.json");
                var keyFile = Path.Combine(profileDir, "key4.db");
                if (File.Exists(loginsFile) && File.Exists(keyFile))
                {
                    try
                    {
                        File.Copy(loginsFile, Path.Combine(destDir, "logins.json"), true);
                        File.Copy(keyFile, Path.Combine(destDir, "key4.db"), true);
                        profile.PasswordsCaptured = true;
                    }
                    catch (IOException ex)
                    {
                        result.Warnings.Add($"{browserName} '{profileName}': Could not copy password files (may be locked): {ex.Message}");
                    }
                }

                // cert9.db — certificate store
                var certFile = Path.Combine(profileDir, "cert9.db");
                if (File.Exists(certFile))
                {
                    try
                    {
                        File.Copy(certFile, Path.Combine(destDir, "cert9.db"), true);
                    }
                    catch (IOException)
                    {
                        // Non-fatal
                    }
                }

                _package.BrowserProfiles.Add(profile);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error capturing {browserName} profiles: {ex.Message}");
        }
    }
}
