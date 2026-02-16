namespace ViperMigrate.Core.Models;

public class CapturedBrowserProfile
{
    public string BrowserName { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string ProfilePath { get; set; } = string.Empty;
    public bool BookmarksCaptured { get; set; }
    public bool ExtensionsCaptured { get; set; }
    public bool PasswordsCaptured { get; set; }
    public string BookmarksFile { get; set; } = string.Empty;
    public List<string> ExtensionIds { get; set; } = new();
    public List<CapturedExtensionInfo>? Extensions { get; set; }
    public List<CapturedBrowserPassword> Passwords { get; set; } = new();
}
