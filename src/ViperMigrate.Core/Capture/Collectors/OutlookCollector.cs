using Microsoft.Win32;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class OutlookCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public OutlookCollector(MigrationPackage package) => _package = package;

    public string Category => "Outlook";

    public Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };

        var config = new CapturedOutlookConfig();
        var outlookDir = Path.Combine(packagePath, "Outlook");
        Directory.CreateDirectory(outlookDir);

        // Try Office 16.0, then 15.0
        string? profilesPath = null;
        string version = "";

        if (RegistryHelper.SubKeyExists(Registry.CurrentUser, PathResolver.OutlookProfilesRegistryPath))
        {
            profilesPath = PathResolver.OutlookProfilesRegistryPath;
            version = "16.0";
        }
        else if (RegistryHelper.SubKeyExists(Registry.CurrentUser, PathResolver.OutlookProfiles15RegistryPath))
        {
            profilesPath = PathResolver.OutlookProfiles15RegistryPath;
            version = "15.0";
        }

        if (profilesPath != null)
        {
            config.OutlookVersion = version;
            config.DefaultProfile = RegistryHelper.ReadString(
                Registry.CurrentUser,
                $@"Software\Microsoft\Office\{version}\Outlook",
                "DefaultProfile") ?? "";

            // Enumerate profiles and accounts
            foreach (var profileName in RegistryHelper.GetSubKeyNames(Registry.CurrentUser, profilesPath))
            {
                ct.ThrowIfCancellationRequested();
                var accountsPath = $@"{profilesPath}\{profileName}";
                foreach (var subKey in RegistryHelper.GetSubKeyNames(Registry.CurrentUser, accountsPath))
                {
                    var keyPath = $@"{accountsPath}\{subKey}";
                    var email = RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "Email");
                    var displayName = RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "Display Name");
                    var server = RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "POP3 Server")
                                 ?? RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "IMAP Server")
                                 ?? RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "HTTP Server URL")
                                 ?? "";

                    if (!string.IsNullOrEmpty(email))
                    {
                        config.Accounts.Add(new OutlookAccount
                        {
                            DisplayName = displayName ?? "",
                            EmailAddress = email,
                            ServerName = server
                        });
                    }

                    // Check for PST paths
                    var pstPath = RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "PST Path");
                    if (!string.IsNullOrEmpty(pstPath) && !config.PstPaths.Contains(pstPath))
                        config.PstPaths.Add(pstPath);
                }
            }
        }

        // Signatures
        var sigPath = PathResolver.OutlookSignaturesPath;
        if (Directory.Exists(sigPath))
        {
            config.SignaturePath = sigPath;
            var sigDestDir = Path.Combine(outlookDir, "Signatures");
            Directory.CreateDirectory(sigDestDir);

            try
            {
                foreach (var file in Directory.GetFiles(sigPath))
                {
                    ct.ThrowIfCancellationRequested();
                    var destFile = Path.Combine(sigDestDir, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                    config.SignatureFiles.Add(Path.GetFileName(file));
                }

                // Copy subdirectories (HTML signature resource folders)
                foreach (var dir in Directory.GetDirectories(sigPath))
                {
                    ct.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(dir);
                    var destSubDir = Path.Combine(sigDestDir, dirName);
                    CopyDirectory(dir, destSubDir, ct);
                }

                config.SignaturesCaptured = config.SignatureFiles.Count > 0;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error copying signatures: {ex.Message}");
            }
        }

        _package.OutlookConfig = config;

        var itemCount = config.Accounts.Count + config.SignatureFiles.Count;
        result.ItemsTotal = itemCount;
        result.ItemsProcessed = itemCount;
        result.Status = itemCount > 0 ? CategoryStatus.Success
            : profilesPath != null ? CategoryStatus.PartialSuccess
            : CategoryStatus.Success;

        progress.Report($"Captured {config.Accounts.Count} account(s), {config.SignatureFiles.Count} signature(s).");
        sw.Stop();
        result.Duration = sw.Elapsed;
        return Task.FromResult(result);
    }

    private static void CopyDirectory(string source, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), ct);
        }
    }
}
