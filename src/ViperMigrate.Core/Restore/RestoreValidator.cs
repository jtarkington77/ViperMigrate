using System.Security.Principal;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore;

public class RestoreValidation
{
    public bool IsAdmin { get; set; }
    public bool WingetAvailable { get; set; }
    public int SoftwareAutoInstallCount { get; set; }
    public int SoftwareManualCount { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<StagePreviewItem> StagePreview { get; set; } = new();
}

public class StagePreviewItem
{
    public RestoreStage Stage { get; set; }
    public string StageName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public static class RestoreValidator
{
    public static async Task<RestoreValidation> ValidateAsync(MigrationPackage package, CancellationToken ct = default)
    {
        var validation = new RestoreValidation();

        // Check admin status
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            validation.IsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        if (!validation.IsAdmin)
        {
            validation.Warnings.Add("Not running as Administrator. Some operations (printers, system startup entries) may fail.");
        }

        // Check winget
        validation.WingetAvailable = await WingetHelper.IsAvailableAsync(ct);

        validation.SoftwareAutoInstallCount = package.Software.Count(s => s.CanAutoInstall);
        validation.SoftwareManualCount = package.Software.Count(s => !s.CanAutoInstall);

        if (validation.SoftwareAutoInstallCount > 0 && !validation.WingetAvailable)
        {
            validation.Warnings.Add($"Winget is not available. {validation.SoftwareAutoInstallCount} software package(s) that could be auto-installed will require manual installation.");
        }

        // Stage preview
        if (package.WifiProfiles.Count > 0 || package.MappedDrives.Count > 0)
        {
            validation.StagePreview.Add(new StagePreviewItem
            {
                Stage = RestoreStage.Connectivity,
                StageName = "Connectivity",
                ItemCount = package.WifiProfiles.Count + package.MappedDrives.Count,
                Description = $"{package.WifiProfiles.Count} WiFi, {package.MappedDrives.Count} drives"
            });
        }

        if (package.Software.Count > 0)
        {
            validation.StagePreview.Add(new StagePreviewItem
            {
                Stage = RestoreStage.SoftwareInstall,
                StageName = "Software Install",
                ItemCount = package.Software.Count,
                Description = $"{validation.SoftwareAutoInstallCount} auto, {validation.SoftwareManualCount} manual"
            });
        }

        if (package.Printers.Count > 0 || package.Scanners.Count > 0)
        {
            validation.StagePreview.Add(new StagePreviewItem
            {
                Stage = RestoreStage.Peripherals,
                StageName = "Peripherals",
                ItemCount = package.Printers.Count + package.Scanners.Count,
                Description = $"{package.Printers.Count} printers, {package.Scanners.Count} scanners"
            });
        }

        var appSettingsCount = package.AppSettings?.CapturedApps.Count ?? 0;
        var configCount = package.BrowserProfiles.Count + package.Shortcuts.Count +
                          package.StartupEntries.Count + package.OfficeRecentFiles.Count +
                          (package.OutlookConfig != null ? 1 : 0) +
                          appSettingsCount;
        if (configCount > 0)
        {
            var configItems = new List<string> { "Browsers", "shortcuts", "Outlook", "Office recent files" };
            if (appSettingsCount > 0) configItems.Add("App Settings");
            validation.StagePreview.Add(new StagePreviewItem
            {
                Stage = RestoreStage.Configuration,
                StageName = "Configuration",
                ItemCount = configCount,
                Description = string.Join(", ", configItems)
            });
        }

        validation.StagePreview.Add(new StagePreviewItem
        {
            Stage = RestoreStage.Verification,
            StageName = "Verification",
            ItemCount = validation.SoftwareAutoInstallCount,
            Description = "Spot-check installed software"
        });

        return validation;
    }
}
