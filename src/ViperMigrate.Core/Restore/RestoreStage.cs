namespace ViperMigrate.Core.Restore;

public enum RestoreStage
{
    SoftwareInstall = 1,
    Connectivity = 2,
    Peripherals = 3,
    Configuration = 4,
    Verification = 5
}

public enum StageStatus
{
    Waiting,
    InProgress,
    Completed,
    PartiallyCompleted,
    Failed,
    Skipped
}

public class StageGroup
{
    public RestoreStage Stage { get; set; }
    public string StageName => Stage switch
    {
        RestoreStage.Connectivity => "Connectivity",
        RestoreStage.SoftwareInstall => "Software Install",
        RestoreStage.Peripherals => "Peripherals",
        RestoreStage.Configuration => "Configuration",
        RestoreStage.Verification => "Verification",
        _ => Stage.ToString()
    };
    public List<IRestoreApplicator> Applicators { get; set; } = new();
}

public class StageProgressInfo
{
    public RestoreStage Stage { get; set; }
    public StageStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class StageRestoreResult
{
    public List<Models.CategoryResult> CategoryResults { get; set; } = new();
    public Dictionary<RestoreStage, StageStatus> StageStatuses { get; set; } = new();
}
