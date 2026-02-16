using System.Data.SQLite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore.Applicators;

public class BrowserApplicator : IRestoreApplicator
{
    private readonly MigrationPackage _package;

    // Known built-in/default extension IDs to skip
    private static readonly HashSet<string> BuiltInExtensionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "nmmhkkegccagdldgiimedpiccmgmieda", // Chrome Web Store Payments
        "aapocclcgogkmnckokdopfmhonfmgoek", // Slides
        "aohghmighlieiainnegkcijnfilokake", // Docs
        "apdfllckaahabafndbhieahigkjlhalf", // Google Drive
        "blpcfgokakmgnkcojhhkbfbldkacnbeo", // YouTube
        "felcaaldnbdncclmgdcncolpebgiejap", // Sheets
        "ghbmnnjooekpmoecnnnilnnbdlolhkhi", // Google Docs Offline
        "pjkljhegncpnkpknbcohdijeoejaedia", // Gmail
    };

    // Known built-in extension names to skip (Edge mainly)
    private static readonly HashSet<string> BuiltInExtensionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edge relevant text changes", "Temp", "Microsoft Editor",
        "Web Store", "Chrome Web Store", "Edge Collections"
    };

    // Browser name → winget ID for install fallback
    private static readonly Dictionary<string, string> BrowserToWingetId = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Chrome", "Google.Chrome" },
        { "Edge", "Microsoft.Edge" },
        { "Firefox", "Mozilla.Firefox" },
        { "Vivaldi", "Vivaldi.Vivaldi" },
        { "Brave", "Brave.Brave" },
        { "Opera", "Opera.Opera" }
    };

    public BrowserApplicator(MigrationPackage package) => _package = package;

    public string Category => "Browsers";
    public RestoreStage Stage => RestoreStage.Configuration;

    public async Task<CategoryResult> RestoreAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        result.ItemsTotal = _package.BrowserProfiles.Count;
        int restored = 0;
        var csvFilesToDelete = new List<string>();

        // Dynamically detect installed browsers for matching
        var installedChromium = BrowserDetector.DetectChromiumBrowsers()
            .ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);
        var installedFirefox = BrowserDetector.DetectFirefoxBrowsers()
            .ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);

        foreach (var profile in _package.BrowserProfiles)
        {
            ct.ThrowIfCancellationRequested();

            if (installedChromium.TryGetValue(profile.BrowserName, out var chromiumBrowser))
            {
                // Browser detected — check if the specific profile directory exists
                var profileDir = Path.Combine(chromiumBrowser.UserDataPath, profile.ProfileName);
                if (!Directory.Exists(profileDir))
                {
                    progress.Report($"{profile.BrowserName} profile '{profile.ProfileName}' not found. Initializing...");
                    await BrowserDetector.InitializeBrowserProfileAsync(profile.BrowserName, ct);

                    // Wait for browser to fully exit and release file handles
                    await Task.Delay(3000, ct);

                    // Safety: create profile dir as absolute last resort
                    if (!Directory.Exists(profileDir))
                    {
                        try { Directory.CreateDirectory(profileDir); } catch { }
                    }
                }
                restored += await RestoreChromiumProfile(profile, chromiumBrowser, packagePath, progress, result, csvFilesToDelete, ct);
            }
            else if (installedFirefox.TryGetValue(profile.BrowserName, out var firefoxBrowser))
            {
                restored += await RestoreFirefoxProfile(profile, firefoxBrowser, packagePath, progress, result, ct);
            }
            else
            {
                // Browser not installed — try to install via winget as last resort
                if (BrowserToWingetId.TryGetValue(profile.BrowserName, out var wingetId))
                {
                    progress.Report($"{profile.BrowserName} not found. Attempting winget install...");
                    var installResult = await WingetHelper.InstallAsync(wingetId, progress, ct);
                    if (installResult.Success)
                    {
                        // Launch browser to initialize profile directories
                        progress.Report($"Initializing {profile.BrowserName} profile...");
                        await BrowserDetector.InitializeBrowserProfileAsync(profile.BrowserName, ct);
                        await Task.Delay(3000, ct);

                        // Re-detect browsers
                        var newChromium = BrowserDetector.DetectChromiumBrowsers()
                            .FirstOrDefault(b => b.Name.Equals(profile.BrowserName, StringComparison.OrdinalIgnoreCase));
                        var newFirefox = BrowserDetector.DetectFirefoxBrowsers()
                            .FirstOrDefault(b => b.Name.Equals(profile.BrowserName, StringComparison.OrdinalIgnoreCase));

                        if (newChromium != null)
                        {
                            restored += await RestoreChromiumProfile(profile, newChromium, packagePath, progress, result, csvFilesToDelete, ct);
                        }
                        else if (newFirefox != null)
                        {
                            restored += await RestoreFirefoxProfile(profile, newFirefox, packagePath, progress, result, ct);
                        }
                        else
                        {
                            result.Warnings.Add($"{profile.BrowserName} installed but profile directory not found. Try opening the browser manually, then re-run restore.");
                        }
                    }
                    else
                    {
                        result.Warnings.Add($"{profile.BrowserName} is not installed and could not be auto-installed: {installResult.Error}");
                    }
                }
                else
                {
                    result.Warnings.Add($"{profile.BrowserName} is not installed — skipping profile '{profile.ProfileName}'.");
                }
            }
        }

        // Clean up CSV files
        foreach (var csv in csvFilesToDelete)
        {
            try { File.Delete(csv); } catch { }
        }

        result.ItemsProcessed = restored;
        result.Status = restored > 0 || result.ItemsTotal == 0
            ? CategoryStatus.Success
            : CategoryStatus.PartialSuccess;

        progress.Report($"Restored {restored}/{result.ItemsTotal} browser profile(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static async Task<int> RestoreChromiumProfile(
        CapturedBrowserProfile profile, DetectedBrowser browser,
        string packagePath, IProgress<string> progress, CategoryResult result,
        List<string> csvFilesToDelete, CancellationToken ct)
    {
        int restored = 0;
        var destProfileDir = Path.Combine(browser.UserDataPath, profile.ProfileName);

        // Bookmarks
        if (profile.BookmarksCaptured && !string.IsNullOrEmpty(profile.BookmarksFile))
        {
            var sourceBookmarks = Path.Combine(packagePath, "Browsers", profile.BrowserName, profile.ProfileName, "Bookmarks");
            if (File.Exists(sourceBookmarks))
            {
                Directory.CreateDirectory(destProfileDir);
                try
                {
                    progress.Report($"Restoring {profile.BrowserName} bookmarks...");
                    File.Copy(sourceBookmarks, Path.Combine(destProfileDir, "Bookmarks"), true);
                    restored++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{profile.BrowserName} bookmarks: {ex.Message}");
                }
            }
        }

        // Passwords
        if (profile.PasswordsCaptured && profile.Passwords.Count > 0)
        {
            progress.Report($"Importing {profile.Passwords.Count} passwords into {profile.BrowserName}...");
            var exeName = BrowserDetector.GetProcessName(profile.BrowserName);
            await KillBrowserProcess(exeName, ct);

            var loginDataPath = Path.Combine(destProfileDir, "Login Data");
            bool injected = false;

            if (Directory.Exists(destProfileDir))
            {
                try
                {
                    injected = InjectPasswordsAesGcm(
                        browser.UserDataPath, destProfileDir, loginDataPath,
                        profile.Passwords, result);
                    if (injected)
                    {
                        progress.Report($"Imported {profile.Passwords.Count} passwords into {profile.BrowserName}.");
                        restored++;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{profile.BrowserName} AES-GCM password import failed: {ex.Message}");
                }
            }

            if (!injected)
            {
                try
                {
                    var csvDir = Path.Combine(packagePath, "Browsers", profile.BrowserName, profile.ProfileName);
                    Directory.CreateDirectory(csvDir);
                    var csvPath = Path.Combine(csvDir, "passwords_import.csv");
                    ExportPasswordsCsv(profile.Passwords, csvPath);
                    csvFilesToDelete.Add(csvPath);

                    result.ManualActions.Add(new ManualAction
                    {
                        Category = "Browsers",
                        Title = $"Import {profile.BrowserName} passwords ({profile.Passwords.Count})",
                        Description = $"Automatic password injection failed. Import manually:\n" +
                            $"1. Open {profile.BrowserName} \u2192 Settings \u2192 Passwords \u2192 Import\n" +
                            $"2. Select file: {csvPath}\n" +
                            $"3. Delete the CSV after import (contains plaintext passwords)",
                        Priority = ManualActionPriority.High
                    });
                    restored++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{profile.BrowserName} password CSV fallback: {ex.Message}");
                }
            }
        }

        // Extensions
        AddExtensionManualActions(profile, result);

        // Clean up lock files to prevent "profile is already in use" on next launch
        BrowserDetector.CleanupChromiumLockFiles(profile.BrowserName);

        return restored;
    }

    private static async Task<int> RestoreFirefoxProfile(
        CapturedBrowserProfile profile, DetectedBrowser browser,
        string packagePath, IProgress<string> progress, CategoryResult result,
        CancellationToken ct)
    {
        int restored = 0;
        var targetProfile = Directory.GetDirectories(browser.UserDataPath)
            .FirstOrDefault(d => Path.GetFileName(d) == profile.ProfileName)
            ?? Directory.GetDirectories(browser.UserDataPath)
                .FirstOrDefault(d => Path.GetFileName(d)!.EndsWith(".default-release"));

        if (targetProfile == null)
        {
            result.Warnings.Add($"{profile.BrowserName} profile '{profile.ProfileName}' not found.");
            return 0;
        }

        var exeName = BrowserDetector.GetProcessName(profile.BrowserName);

        if (profile.BookmarksCaptured)
        {
            var sourcePlaces = Path.Combine(packagePath, "Browsers", profile.BrowserName, profile.ProfileName, "places.sqlite");
            if (File.Exists(sourcePlaces))
            {
                progress.Report($"Restoring {profile.BrowserName} bookmarks...");
                await KillBrowserProcess(exeName, ct);
                try
                {
                    File.Copy(sourcePlaces, Path.Combine(targetProfile, "places.sqlite"), true);
                    restored++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{profile.BrowserName} bookmarks copy failed: {ex.Message}");
                }
            }
        }

        if (profile.PasswordsCaptured)
        {
            var sourceDir = Path.Combine(packagePath, "Browsers", profile.BrowserName, profile.ProfileName);
            var sourceLogins = Path.Combine(sourceDir, "logins.json");
            var sourceKey = Path.Combine(sourceDir, "key4.db");

            if (File.Exists(sourceLogins) && File.Exists(sourceKey))
            {
                progress.Report($"Restoring {profile.BrowserName} passwords...");
                await KillBrowserProcess(exeName, ct);
                try
                {
                    File.Copy(sourceLogins, Path.Combine(targetProfile, "logins.json"), true);
                    File.Copy(sourceKey, Path.Combine(targetProfile, "key4.db"), true);
                    restored++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{profile.BrowserName} password restore failed: {ex.Message}");
                }
            }
        }

        var sourceCert = Path.Combine(packagePath, "Browsers", profile.BrowserName, profile.ProfileName, "cert9.db");
        if (File.Exists(sourceCert))
        {
            try { File.Copy(sourceCert, Path.Combine(targetProfile, "cert9.db"), true); } catch { }
        }

        return restored;
    }

    private static void AddExtensionManualActions(CapturedBrowserProfile profile, CategoryResult result)
    {
        var userExtensionIds = profile.ExtensionIds
            .Where(id => !BuiltInExtensionIds.Contains(id))
            .ToList();

        if (userExtensionIds.Count == 0) return;

        var extLines = new List<string>();

        if (profile.Extensions != null && profile.Extensions.Count > 0)
        {
            foreach (var ext in profile.Extensions
                .Where(e => !BuiltInExtensionIds.Contains(e.Id) &&
                            !BuiltInExtensionNames.Contains(e.Name ?? "")))
            {
                var displayName = !string.IsNullOrEmpty(ext.Name) ? ext.Name : ext.Id;
                extLines.Add($"  - {displayName}");
            }
        }
        else
        {
            foreach (var id in userExtensionIds)
            {
                extLines.Add($"  - {id}");
            }
        }

        if (extLines.Count > 0)
        {
            result.ManualActions.Add(new ManualAction
            {
                Category = "Browsers",
                Title = $"Install {extLines.Count} {profile.BrowserName} extensions",
                Description = $"Re-install these extensions from the {profile.BrowserName} extension store:\n{string.Join("\n", extLines)}",
                Priority = ManualActionPriority.Medium
            });
        }
    }

    private static bool InjectPasswordsAesGcm(string userDataPath, string profileDir,
        string loginDataPath, List<CapturedBrowserPassword> passwords, CategoryResult result)
    {
        // Get or create the AES master key
        var masterKey = GetOrCreateMasterKey(userDataPath);
        if (masterKey == null)
        {
            result.Warnings.Add($"Password injection: could not get/create AES master key. " +
                $"Local State exists: {File.Exists(Path.Combine(userDataPath, "Local State"))}");
            return false;
        }

        // If Login Data doesn't exist, we need to create it
        if (!File.Exists(loginDataPath))
        {
            try
            {
                Directory.CreateDirectory(profileDir);
                CreateEmptyLoginDatabase(loginDataPath);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Password injection: could not create Login Data: {ex.Message}");
                return false;
            }
        }

        var tempDb = loginDataPath + ".viper_tmp";
        try
        {
            File.Copy(loginDataPath, tempDb, true);

            using var connection = new SQLiteConnection($"Data Source={tempDb};Version=3;");
            connection.Open();

            // Ensure logins table exists
            using (var check = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='logins'", connection))
            {
                if (check.ExecuteScalar() == null)
                {
                    CreateLoginsTable(connection);
                }
            }

            int injected = 0;
            string? firstError = null;
            foreach (var pw in passwords)
            {
                try
                {
                    // Encrypt password with AES-GCM using master key, prepend v10 prefix
                    var passwordBytes = Encoding.UTF8.GetBytes(pw.Password);
                    var encrypted = EncryptAesGcm(passwordBytes, masterKey);

                    // Check if this login already exists
                    using var checkCmd = new SQLiteCommand(
                        "SELECT COUNT(*) FROM logins WHERE origin_url=@url AND username_value=@user", connection);
                    checkCmd.Parameters.AddWithValue("@url", pw.Url);
                    checkCmd.Parameters.AddWithValue("@user", pw.Username);

                    if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0)
                        continue;

                    var signon = pw.Url;
                    try
                    {
                        if (Uri.TryCreate(pw.Url, UriKind.Absolute, out var uri))
                            signon = $"{uri.Scheme}://{uri.Host}/";
                    }
                    catch { }

                    using var cmd = new SQLiteCommand(@"INSERT INTO logins
                        (origin_url, action_url, username_element, username_value, password_element,
                         password_value, signon_realm, date_created, blacklisted_by_user, scheme)
                        VALUES (@origin, @action, '', @user, '', @pass, @realm, @date, 0, 0)", connection);

                    cmd.Parameters.AddWithValue("@origin", pw.Url);
                    cmd.Parameters.AddWithValue("@action", pw.Url);
                    cmd.Parameters.AddWithValue("@user", pw.Username);
                    cmd.Parameters.Add("@pass", System.Data.DbType.Binary).Value = encrypted;
                    cmd.Parameters.AddWithValue("@realm", signon);
                    cmd.Parameters.AddWithValue("@date", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);

                    cmd.ExecuteNonQuery();
                    injected++;
                }
                catch (Exception ex)
                {
                    // Log first failure only to avoid flooding
                    if (injected == 0 && firstError == null)
                        firstError = ex.Message;
                }
            }

            connection.Close();

            if (injected > 0)
            {
                File.Copy(tempDb, loginDataPath, true);
                return true;
            }

            result.Warnings.Add($"Password injection: 0/{passwords.Count} inserted. " +
                (firstError != null ? $"First error: {firstError}" : "All may be duplicates."));
            return false;
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Password injection failed: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }
    }

    /// <summary>
    /// Gets the AES master key from Local State, or creates one if it doesn't exist.
    /// </summary>
    private static byte[]? GetOrCreateMasterKey(string userDataPath)
    {
        var localStatePath = Path.Combine(userDataPath, "Local State");

        if (File.Exists(localStatePath))
        {
            // Read existing key — same algorithm as ChromiumPasswordDecryptor.GetMasterKey()
            try
            {
                var json = File.ReadAllText(localStatePath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) &&
                    osCrypt.TryGetProperty("encrypted_key", out var encKeyProp))
                {
                    var encKeyB64 = encKeyProp.GetString();
                    if (!string.IsNullOrEmpty(encKeyB64))
                    {
                        var encKey = Convert.FromBase64String(encKeyB64);
                        if (encKey.Length >= 5 && Encoding.ASCII.GetString(encKey, 0, 5) == "DPAPI")
                        {
                            var dpapiBlob = new byte[encKey.Length - 5];
                            Array.Copy(encKey, 5, dpapiBlob, 0, dpapiBlob.Length);
                            return ProtectedData.Unprotect(dpapiBlob, null, DataProtectionScope.CurrentUser);
                        }
                    }
                }
            }
            catch
            {
                // Fall through to generate new key
            }
        }

        // Browser never launched or Local State is corrupt — generate new key
        try
        {
            var masterKey = new byte[32];
            RandomNumberGenerator.Fill(masterKey);

            // DPAPI encrypt and prepend "DPAPI" prefix
            var dpapiEncrypted = ProtectedData.Protect(masterKey, null, DataProtectionScope.CurrentUser);
            var withPrefix = new byte[5 + dpapiEncrypted.Length];
            Encoding.ASCII.GetBytes("DPAPI", 0, 5, withPrefix, 0);
            Array.Copy(dpapiEncrypted, 0, withPrefix, 5, dpapiEncrypted.Length);

            var b64Key = Convert.ToBase64String(withPrefix);

            // Write or update Local State — use JsonNode for targeted patching
            // to preserve all existing browser config (prevents Vivaldi/Chrome corruption)
            Directory.CreateDirectory(userDataPath);
            if (File.Exists(localStatePath))
            {
                var jsonNode = JsonNode.Parse(File.ReadAllText(localStatePath));
                if (jsonNode is JsonObject root)
                {
                    root["os_crypt"] = new JsonObject { ["encrypted_key"] = b64Key };
                    File.WriteAllText(localStatePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            else
            {
                var root = new JsonObject
                {
                    ["os_crypt"] = new JsonObject { ["encrypted_key"] = b64Key }
                };
                File.WriteAllText(localStatePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            return masterKey;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Encrypts plaintext with AES-GCM, returns v10 + 12-byte nonce + ciphertext + 16-byte tag.
    /// </summary>
    private static byte[] EncryptAesGcm(byte[] plaintext, byte[] masterKey)
    {
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(masterKey, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: "v10" (3 bytes) + nonce (12 bytes) + ciphertext + tag (16 bytes)
        var result = new byte[3 + 12 + ciphertext.Length + 16];
        result[0] = (byte)'v';
        result[1] = (byte)'1';
        result[2] = (byte)'0';
        Array.Copy(nonce, 0, result, 3, 12);
        Array.Copy(ciphertext, 0, result, 15, ciphertext.Length);
        Array.Copy(tag, 0, result, 15 + ciphertext.Length, 16);

        return result;
    }

    private static void CreateEmptyLoginDatabase(string dbPath)
    {
        using var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
        connection.Open();
        CreateLoginsTable(connection);
        connection.Close();
    }

    private static void CreateLoginsTable(SQLiteConnection connection)
    {
        using var cmd = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS logins (
            origin_url TEXT NOT NULL,
            action_url TEXT,
            username_element TEXT,
            username_value TEXT,
            password_element TEXT,
            password_value BLOB,
            signon_realm TEXT NOT NULL,
            date_created INTEGER NOT NULL,
            blacklisted_by_user INTEGER NOT NULL DEFAULT 0,
            scheme INTEGER NOT NULL DEFAULT 0,
            password_type INTEGER DEFAULT 0,
            times_used INTEGER DEFAULT 0,
            form_data BLOB,
            display_name TEXT DEFAULT '',
            icon_url TEXT DEFAULT '',
            federation_url TEXT DEFAULT '',
            skip_zero_click INTEGER DEFAULT 0,
            generation_upload_status INTEGER DEFAULT 0,
            possible_username_pairs BLOB,
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date_last_used INTEGER DEFAULT 0,
            moving_blocked_for BLOB,
            date_password_modified INTEGER DEFAULT 0
        )", connection);
        cmd.ExecuteNonQuery();
    }

    private static async Task KillBrowserProcess(string processName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(processName)) return;

        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0) return;

        foreach (var p in processes)
        {
            try { p.Kill(); } catch { }
            p.Dispose();
        }

        // Give processes time to exit
        await Task.Delay(1500, ct);
    }

    private static void ExportPasswordsCsv(List<CapturedBrowserPassword> passwords, string csvPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name,url,username,password");

        foreach (var pw in passwords)
        {
            var name = pw.Url;
            try
            {
                if (Uri.TryCreate(pw.Url, UriKind.Absolute, out var uri))
                    name = uri.Host;
            }
            catch { }

            sb.AppendLine($"{CsvEscape(name)},{CsvEscape(pw.Url)},{CsvEscape(pw.Username)},{CsvEscape(pw.Password)}");
        }

        File.WriteAllText(csvPath, sb.ToString());
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
