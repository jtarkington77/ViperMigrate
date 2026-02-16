namespace ViperMigrate.Core.Models;

public class CapturedSoftware
{
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayVersion { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public string InstallDate { get; set; } = string.Empty;
    public bool IsX64 { get; set; }
    public string? WingetId { get; set; }
    public string? LicenseKey { get; set; }
    public bool CanAutoInstall => !string.IsNullOrEmpty(WingetId);
}
