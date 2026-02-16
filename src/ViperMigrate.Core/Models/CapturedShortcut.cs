namespace ViperMigrate.Core.Models;

public class CapturedShortcut
{
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string IconLocation { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsTaskbarPinned { get; set; }
    public bool IsDesktopShortcut { get; set; }
    public bool IsStartMenu { get; set; }
    public bool IsStartup { get; set; }
    public bool IsQuickLaunch { get; set; }
    /// <summary>Relative path within Start Menu (e.g. "Anaconda\Jupyter Notebook")</summary>
    public string StartMenuFolder { get; set; } = string.Empty;
}

public class CapturedStartupEntry
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "Registry-HKCU", "Registry-HKLM", "StartupFolder"
    public bool IsPerUser { get; set; }
    /// <summary>Whether the entry is enabled in StartupApproved. Null = not found (assume enabled).</summary>
    public bool? IsEnabled { get; set; }
}
