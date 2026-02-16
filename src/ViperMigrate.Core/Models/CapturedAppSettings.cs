namespace ViperMigrate.Core.Models;

/// <summary>
/// Represents a single captured application's configuration data.
/// </summary>
public class CapturedAppConfig
{
    /// <summary>Display name of the application (e.g. "Thunderbird", "PuTTY", "Discord")</summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>Source location type: "AppData", "LocalAppData", "UserProfile", "Registry"</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Original source path (for reference/display)</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Subfolder name under AppSettings/ in the package</summary>
    public string PackageFolder { get; set; } = string.Empty;

    /// <summary>Total size in bytes of all captured files</summary>
    public long SizeBytes { get; set; }

    /// <summary>Number of files captured</summary>
    public int FileCount { get; set; }

    /// <summary>Whether this app was matched to the software inventory</summary>
    public bool MatchedToInventory { get; set; }
}

/// <summary>
/// Container for all dynamically captured application settings.
/// </summary>
public class CapturedAppSettings
{
    /// <summary>List of all captured app configurations</summary>
    public List<CapturedAppConfig> CapturedApps { get; set; } = new();
}
