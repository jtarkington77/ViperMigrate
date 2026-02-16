namespace ViperMigrate.Core.Models;

public class CapturedScanner
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public bool IsNetworkScanner { get; set; }
    public string NetworkAddress { get; set; } = string.Empty;
    public List<ScanProfile> Profiles { get; set; } = new();
}

public class ScanProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string ColorMode { get; set; } = string.Empty;
    public int Resolution { get; set; }
    public string SavePath { get; set; } = string.Empty;
}
