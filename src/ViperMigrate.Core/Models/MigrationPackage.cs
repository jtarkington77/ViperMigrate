namespace ViperMigrate.Core.Models;

public class MigrationPackage
{
    public string Version { get; set; } = "1.2";
    public MachineInfo SourceMachine { get; set; } = new();
    public List<CapturedSoftware> Software { get; set; } = new();
    public List<CapturedPrinter> Printers { get; set; } = new();
    public List<CapturedScanner> Scanners { get; set; } = new();
    public List<CapturedDrive> MappedDrives { get; set; } = new();
    public List<CapturedBrowserProfile> BrowserProfiles { get; set; } = new();
    public List<CapturedWifiProfile> WifiProfiles { get; set; } = new();
    public List<CapturedShortcut> Shortcuts { get; set; } = new();
    public List<CapturedStartupEntry> StartupEntries { get; set; } = new();
    public CapturedOutlookConfig? OutlookConfig { get; set; }
    public List<CapturedOfficeRecent> OfficeRecentFiles { get; set; } = new();
    public bool WingetExportCaptured { get; set; }
    public List<CapturedThunderbirdProfile>? ThunderbirdProfiles { get; set; }
    public CapturedAppSettings? AppSettings { get; set; }
    public List<CategoryResult> CaptureResults { get; set; } = new();
    public List<ManualAction> ManualActions { get; set; } = new();
}
