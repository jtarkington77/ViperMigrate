namespace ViperMigrate.Core.Models;

public class MachineInfo
{
    public string ComputerName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public int? OsBuildNumber { get; set; }
    public DateTime CaptureDate { get; set; } = DateTime.UtcNow;
}
