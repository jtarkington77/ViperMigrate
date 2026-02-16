namespace ViperMigrate.Core.Models;

public class CapturedThunderbirdProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public string ProfilePath { get; set; } = string.Empty;
    public bool PrefsJsCaptured { get; set; }
    public bool LoginsCaptured { get; set; }
    public bool AddressBookCaptured { get; set; }
    public bool MailCaptured { get; set; }
}
