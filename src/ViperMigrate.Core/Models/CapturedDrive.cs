namespace ViperMigrate.Core.Models;

public class CapturedDrive
{
    public string DriveLetter { get; set; } = string.Empty;
    public string UncPath { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool Persistent { get; set; }
}
