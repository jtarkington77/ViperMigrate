namespace ViperMigrate.Core.Models;

public class CapturedOutlookConfig
{
    public string OutlookVersion { get; set; } = string.Empty;
    public string DefaultProfile { get; set; } = string.Empty;
    public List<OutlookAccount> Accounts { get; set; } = new();
    public List<string> PstPaths { get; set; } = new();
    public string SignaturePath { get; set; } = string.Empty;
    public bool SignaturesCaptured { get; set; }
    public List<string> SignatureFiles { get; set; } = new();
}

public class OutlookAccount
{
    public string DisplayName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
}
