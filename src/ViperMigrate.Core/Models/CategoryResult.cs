namespace ViperMigrate.Core.Models;

public class CategoryResult
{
    public string Category { get; set; } = string.Empty;
    public CategoryStatus Status { get; set; } = CategoryStatus.Pending;
    public int ItemsProcessed { get; set; }
    public int ItemsTotal { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<ManualAction> ManualActions { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

public enum CategoryStatus
{
    Pending,
    InProgress,
    Success,
    PartialSuccess,
    Failed,
    Skipped
}
