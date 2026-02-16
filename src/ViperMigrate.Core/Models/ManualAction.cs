namespace ViperMigrate.Core.Models;

public class ManualAction
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ManualActionPriority Priority { get; set; } = ManualActionPriority.Medium;
    public string? Url { get; set; }
    public string? Command { get; set; }
    public bool IsChecked { get; set; }
}

public enum ManualActionPriority
{
    Low,
    Medium,
    High,
    Critical
}
