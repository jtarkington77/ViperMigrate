namespace ViperMigrate.Core.Models;

public class CapturedWifiProfile
{
    public string Ssid { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string EncryptionType { get; set; } = string.Empty;
    public string XmlProfile { get; set; } = string.Empty;
    public bool HasPassword { get; set; }
    public string EncryptedPassword { get; set; } = string.Empty;
}
