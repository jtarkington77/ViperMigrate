namespace ViperMigrate.Core.Models;

public class CapturedOfficeRecent
{
    public string ApplicationName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTime LastAccessed { get; set; }
}
