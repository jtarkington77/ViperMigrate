namespace ViperMigrate.Core.Models;

public class CapturedPrinter
{
    public string Name { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string PortName { get; set; } = string.Empty;
    public string ShareName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsNetworkPrinter { get; set; }
    public string UncPath { get; set; } = string.Empty;
    public string PrinterHostAddress { get; set; } = string.Empty;
}
