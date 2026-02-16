using System.Diagnostics;

namespace ViperMigrate.Core.Common;

public class DetectedBrowser
{
    public string Name { get; set; } = string.Empty;
    public string UserDataPath { get; set; } = string.Empty;
    public string BrowserType { get; set; } = string.Empty; // "Chromium" or "Firefox"
    public List<string> ProfileNames { get; set; } = new();
}

public static class BrowserDetector
{
    // Known Chromium browser paths relative to %LOCALAPPDATA% (or %APPDATA% for Opera)
    private static readonly (string Name, string RelativePath, bool UseAppData)[] KnownChromiumPaths =
    {
        ("Chrome", @"Google\Chrome\User Data", false),
        ("Edge", @"Microsoft\Edge\User Data", false),
        ("Vivaldi", @"Vivaldi\User Data", false),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data", false),
        ("Opera", @"Opera Software\Opera Stable", true),
        ("Opera GX", @"Opera Software\Opera GX Stable", true),
        ("Chromium", @"Chromium\User Data", false),
        ("Yandex", @"Yandex\YandexBrowser\User Data", false),
        ("Coc Coc", @"CocCoc\Browser\User Data", false),
        ("Comodo Dragon", @"Comodo\Dragon\User Data", false),
        ("Epic", @"Epic Privacy Browser\User Data", false),
        ("Iridium", @"Iridium\User Data", false),
        ("Iron", @"SRWare Iron\User Data", false),
        ("Cent", @"CentBrowser\User Data", false),
        ("Torch", @"Torch\User Data", false),
        ("Slimjet", @"Slimjet\User Data", false),
        ("Whale", @"Naver\Naver Whale\User Data", false),
        ("360Browser", @"360Chrome\Chrome\User Data", false),
    };

    // Known Firefox-family paths relative to %APPDATA%
    private static readonly (string Name, string RelativePath)[] KnownFirefoxPaths =
    {
        ("Firefox", @"Mozilla\Firefox\Profiles"),
        ("Waterfox", @"Waterfox\Profiles"),
        ("LibreWolf", @"LibreWolf\Profiles"),
        ("Pale Moon", @"Moonchild Productions\Pale Moon\Profiles"),
        ("Basilisk", @"Moonchild Productions\Basilisk\Profiles"),
        ("Floorp", @"Floorp\Profiles"),
        ("Zen", @"Zen\Profiles"),
        ("SeaMonkey", @"Mozilla\SeaMonkey\Profiles"),
    };

    private static readonly Dictionary<string, string> ProcessNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chrome"] = "chrome",
        ["Edge"] = "msedge",
        ["Vivaldi"] = "vivaldi",
        ["Brave"] = "brave",
        ["Opera"] = "opera",
        ["Opera GX"] = "opera",
        ["Chromium"] = "chromium",
        ["Yandex"] = "browser", // Yandex Browser
        ["Coc Coc"] = "CocCoc",
        ["Comodo Dragon"] = "dragon",
        ["Epic"] = "epic",
        ["Iridium"] = "iridium",
        ["Iron"] = "iron",
        ["Cent"] = "CentBrowser",
        ["Torch"] = "torch",
        ["Slimjet"] = "slimjet",
        ["Whale"] = "whale",
        ["360Browser"] = "360Chrome",
        ["Firefox"] = "firefox",
        ["Waterfox"] = "waterfox",
        ["LibreWolf"] = "librewolf",
        ["Pale Moon"] = "palemoon",
        ["Basilisk"] = "basilisk",
        ["Floorp"] = "floorp",
        ["Zen"] = "zen",
        ["SeaMonkey"] = "seamonkey",
    };

    public static List<DetectedBrowser> DetectChromiumBrowsers()
    {
        var browsers = new List<DetectedBrowser>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Check known paths
        foreach (var (name, relativePath, useAppData) in KnownChromiumPaths)
        {
            var basePath = useAppData ? appData : localAppData;
            var userDataPath = Path.Combine(basePath, relativePath);

            if (!Directory.Exists(userDataPath))
                continue;

            // Verify the browser is actually installed (exe exists on disk)
            // Prevents capturing ghost AppData from uninstalled browsers
            if (GetExecutablePath(name) == null)
                continue;

            var profiles = FindChromiumProfiles(userDataPath);
            if (profiles.Count > 0)
            {
                browsers.Add(new DetectedBrowser
                {
                    Name = name,
                    UserDataPath = userDataPath,
                    BrowserType = "Chromium",
                    ProfileNames = profiles
                });
            }
        }

        // Deep scan: check 2 levels under %LOCALAPPDATA% for any folder with "User Data/Default/Login Data"
        try
        {
            foreach (var vendorDir in Directory.GetDirectories(localAppData))
            {
                foreach (var appDir in Directory.GetDirectories(vendorDir))
                {
                    var userDataDir = Path.Combine(appDir, "User Data");
                    if (!Directory.Exists(userDataDir))
                        continue;

                    // Skip if already detected by known paths
                    if (browsers.Any(b => b.UserDataPath.Equals(userDataDir, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var loginData = Path.Combine(userDataDir, "Default", "Login Data");
                    if (!File.Exists(loginData))
                        continue;

                    // Verify the browser has an executable (not ghost AppData)
                    var name = Path.GetFileName(appDir);
                    if (GetExecutablePath(name) == null)
                    {
                        // Also check if any .exe exists in the parent application directory
                        bool hasExe = false;
                        try
                        {
                            hasExe = Directory.GetFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly).Length > 0 ||
                                     Directory.GetParent(userDataDir) is { } parent &&
                                     Directory.GetFiles(parent.FullName, "*.exe", SearchOption.TopDirectoryOnly).Length > 0;
                        }
                        catch { }
                        if (!hasExe) continue;
                    }

                    var profiles = FindChromiumProfiles(userDataDir);
                    if (profiles.Count > 0)
                    {
                        browsers.Add(new DetectedBrowser
                        {
                            Name = name,
                            UserDataPath = userDataDir,
                            BrowserType = "Chromium",
                            ProfileNames = profiles
                        });
                    }
                }
            }
        }
        catch
        {
            // Deep scan failure is non-fatal
        }

        return browsers;
    }

    public static List<DetectedBrowser> DetectFirefoxBrowsers()
    {
        var browsers = new List<DetectedBrowser>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var (name, relativePath) in KnownFirefoxPaths)
        {
            // Thunderbird is handled by AppSettingsCollector generically
            var profilesPath = Path.Combine(appData, relativePath);
            if (!Directory.Exists(profilesPath))
                continue;

            // Verify the browser is actually installed (exe exists on disk)
            if (GetExecutablePath(name) == null)
                continue;

            try
            {
                var profiles = Directory.GetDirectories(profilesPath)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                if (profiles.Count > 0)
                {
                    browsers.Add(new DetectedBrowser
                    {
                        Name = name,
                        UserDataPath = profilesPath,
                        BrowserType = "Firefox",
                        ProfileNames = profiles!
                    });
                }
            }
            catch
            {
                // Individual browser detection failure is non-fatal
            }
        }

        // Scan for profiles.ini files that may indicate other Firefox-based browsers
        try
        {
            var mozillaBase = Path.Combine(appData, "Mozilla");
            if (Directory.Exists(mozillaBase))
            {
                foreach (var dir in Directory.GetDirectories(mozillaBase))
                {
                    var profilesIni = Path.Combine(dir, "profiles.ini");
                    if (!File.Exists(profilesIni))
                        continue;

                    var profilesDir = Path.Combine(dir, "Profiles");
                    if (!Directory.Exists(profilesDir))
                        continue;

                    if (browsers.Any(b => b.UserDataPath.Equals(profilesDir, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var profiles = Directory.GetDirectories(profilesDir)
                        .Select(Path.GetFileName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();

                    if (profiles.Count > 0)
                    {
                        browsers.Add(new DetectedBrowser
                        {
                            Name = Path.GetFileName(dir),
                            UserDataPath = profilesDir,
                            BrowserType = "Firefox",
                            ProfileNames = profiles!
                        });
                    }
                }
            }
        }
        catch
        {
            // Scan failure is non-fatal
        }

        return browsers;
    }

    public static List<string> FindChromiumProfiles(string userDataPath)
    {
        var profiles = new List<string>();
        if (!Directory.Exists(userDataPath))
            return profiles;

        try
        {
            foreach (var dir in Directory.GetDirectories(userDataPath))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                {
                    profiles.Add(name);
                }
            }
        }
        catch
        {
            // Non-fatal
        }

        return profiles;
    }

    public static string GetProcessName(string browserName)
    {
        return ProcessNameMap.TryGetValue(browserName, out var processName) ? processName : browserName.ToLowerInvariant();
    }

    // Browser name → list of possible exe paths (ordered by likelihood)
    private static readonly Dictionary<string, string[]> BrowserExePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chrome"] = new[]
        {
            @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
            @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
        },
        ["Edge"] = new[]
        {
            @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
            @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
        },
        ["Firefox"] = new[]
        {
            @"%ProgramFiles%\Mozilla Firefox\firefox.exe",
            @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"
        },
        ["Vivaldi"] = new[]
        {
            @"%LOCALAPPDATA%\Vivaldi\Application\vivaldi.exe"
        },
        ["Brave"] = new[]
        {
            @"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe",
            @"%ProgramFiles(x86)%\BraveSoftware\Brave-Browser\Application\brave.exe",
            @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\Application\brave.exe"
        },
        ["Opera"] = new[]
        {
            @"%LOCALAPPDATA%\Programs\Opera\opera.exe",
            @"%APPDATA%\Opera Software\Opera Stable\opera.exe"
        },
        ["Opera GX"] = new[]
        {
            @"%LOCALAPPDATA%\Programs\Opera GX\opera.exe",
            @"%APPDATA%\Opera Software\Opera GX Stable\opera.exe"
        }
    };

    public static string? GetExecutablePath(string browserName)
    {
        if (BrowserExePaths.TryGetValue(browserName, out var candidates))
        {
            foreach (var template in candidates)
            {
                var expanded = Environment.ExpandEnvironmentVariables(template);
                if (File.Exists(expanded))
                    return expanded;
            }
        }

        // Fallback: try to find via process name in common locations
        var processName = GetProcessName(browserName);
        var searchDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                var matches = Directory.GetFiles(dir, $"{processName}.exe", SearchOption.AllDirectories);
                if (matches.Length > 0)
                    return matches[0];
            }
            catch
            {
                // Permission errors on some directories are expected
            }
        }

        return null;
    }

    // Chromium lock files that prevent launch after process kill
    private static readonly string[] ChromiumLockFiles =
    {
        "lockfile", "SingletonLock", "SingletonSocket", "SingletonCookie"
    };

    /// <summary>
    /// Launches a browser briefly to initialize its profile directory, then kills it.
    /// Cleans up lock files to prevent broken state. Returns true if profile directory exists.
    /// </summary>
    public static async Task<bool> InitializeBrowserProfileAsync(string browserName, CancellationToken ct)
    {
        // Check if profile already exists — skip initialization if so
        var existingChromium = DetectChromiumBrowsers()
            .FirstOrDefault(b => b.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase));
        var existingGecko = DetectFirefoxBrowsers()
            .FirstOrDefault(b => b.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase));
        if (existingChromium != null || existingGecko != null)
            return true;

        var exePath = GetExecutablePath(browserName);
        if (exePath == null)
            return false;

        bool isGecko = browserName.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
                       browserName.Contains("Waterfox", StringComparison.OrdinalIgnoreCase) ||
                       browserName.Contains("LibreWolf", StringComparison.OrdinalIgnoreCase) ||
                       browserName.Contains("Thunderbird", StringComparison.OrdinalIgnoreCase);

        Process? process = null;
        try
        {
            if (isGecko)
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "-headless -no-remote",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--no-first-run --disable-sync --disable-extensions --disable-default-apps --no-default-browser-check",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });
            }

            if (process == null)
                return false;

            // Poll for profile directory creation (up to 15 seconds)
            bool found = false;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000, ct);

                var chromium = DetectChromiumBrowsers()
                    .FirstOrDefault(b => b.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase));
                var gecko = DetectFirefoxBrowsers()
                    .FirstOrDefault(b => b.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase));

                if (chromium != null || gecko != null)
                {
                    found = true;
                    break;
                }
            }

            return found;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    var processName = GetProcessName(browserName);
                    foreach (var p in Process.GetProcessesByName(processName))
                    {
                        try { p.Kill(); } catch { }
                        p.Dispose();
                    }
                }
                catch { }

                // Wait for file handles to release
                await Task.Delay(2000, CancellationToken.None);
                process.Dispose();

                // Clean up Chromium lock files to prevent broken state on next launch
                if (!isGecko)
                {
                    CleanupChromiumLockFiles(browserName);
                }
            }
        }
    }

    /// <summary>
    /// Removes Chromium lock files from the User Data directory.
    /// These files prevent the browser from opening if the process was killed.
    /// </summary>
    public static void CleanupChromiumLockFiles(string browserName)
    {
        try
        {
            var browser = DetectChromiumBrowsers()
                .FirstOrDefault(b => b.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase));
            if (browser == null) return;

            foreach (var lockFile in ChromiumLockFiles)
            {
                var lockPath = Path.Combine(browser.UserDataPath, lockFile);
                try { if (File.Exists(lockPath)) File.Delete(lockPath); } catch { }
            }

            // Also clean lock files from profile subdirectories (Default, Profile 1, etc.)
            foreach (var profileName in browser.ProfileNames)
            {
                var profileDir = Path.Combine(browser.UserDataPath, profileName);
                foreach (var lockFile in ChromiumLockFiles)
                {
                    var lockPath = Path.Combine(profileDir, lockFile);
                    try { if (File.Exists(lockPath)) File.Delete(lockPath); } catch { }
                }
            }
        }
        catch { }
    }
}
