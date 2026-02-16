using System.Collections.ObjectModel;
using System.Windows.Threading;
using ViperMigrate.Core.Models;
using ViperMigrate.Core.Restore;

namespace ViperMigrate.App.ViewModels;

public class ProgressItemViewModel : ViewModelBase
{
    private CategoryStatus _status = CategoryStatus.Pending;
    private string _statusText = "Pending";
    private string _detail = string.Empty;

    public string Category { get; set; } = string.Empty;

    public CategoryStatus Status
    {
        get => _status;
        set
        {
            SetProperty(ref _status, value);
            StatusText = value.ToString();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}

public class StageGroupViewModel : ViewModelBase
{
    private StageStatus _status = StageStatus.Waiting;
    private string _statusText = "Waiting";

    public RestoreStage Stage { get; set; }
    public string StageName { get; set; } = string.Empty;

    public StageStatus Status
    {
        get => _status;
        set
        {
            SetProperty(ref _status, value);
            StatusText = value switch
            {
                StageStatus.Waiting => "Waiting",
                StageStatus.InProgress => "In Progress",
                StageStatus.Completed => "Completed",
                StageStatus.PartiallyCompleted => "Partial",
                StageStatus.Failed => "Failed",
                StageStatus.Skipped => "Skipped",
                _ => value.ToString()
            };
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ObservableCollection<ProgressItemViewModel> Items { get; } = new();
}

public class ProgressViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private string _currentOperation = string.Empty;
    private double _overallProgress;

    public ProgressViewModel(MainViewModel main)
    {
        _main = main;
        Items = new ObservableCollection<ProgressItemViewModel>();
        Stages = new ObservableCollection<StageGroupViewModel>();
    }

    public ObservableCollection<ProgressItemViewModel> Items { get; }
    public ObservableCollection<StageGroupViewModel> Stages { get; }

    public string CurrentOperation
    {
        get => _currentOperation;
        set => SetProperty(ref _currentOperation, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    /// <summary>
    /// Initialize for capture flow (flat category list, no stages).
    /// </summary>
    public void Initialize(IEnumerable<string> categories)
    {
        Items.Clear();
        Stages.Clear();
        OverallProgress = 0;
        CurrentOperation = "Initializing...";

        foreach (var cat in categories)
        {
            Items.Add(new ProgressItemViewModel { Category = cat });
        }
    }

    /// <summary>
    /// Initialize for staged restore flow (grouped by stage).
    /// </summary>
    public void InitializeStaged(List<StageGroup> stageGroups)
    {
        Items.Clear();
        Stages.Clear();
        OverallProgress = 0;
        CurrentOperation = "Initializing staged restore...";

        foreach (var group in stageGroups)
        {
            var stageVm = new StageGroupViewModel
            {
                Stage = group.Stage,
                StageName = group.StageName
            };

            foreach (var applicator in group.Applicators)
            {
                var item = new ProgressItemViewModel { Category = applicator.Category };
                stageVm.Items.Add(item);
                Items.Add(item); // Also in flat list for UpdateCategory compatibility
            }

            Stages.Add(stageVm);
        }
    }

    public void UpdateCategory(string category, CategoryStatus status)
    {
        var item = Items.FirstOrDefault(i => i.Category == category);
        if (item != null)
            item.Status = status;

        // Update overall progress
        var total = Items.Count;
        if (total > 0)
        {
            var done = Items.Count(i => i.Status is CategoryStatus.Success
                or CategoryStatus.PartialSuccess or CategoryStatus.Failed or CategoryStatus.Skipped);
            OverallProgress = (double)done / total * 100;
        }
    }

    public void UpdateStage(RestoreStage stage, StageStatus status)
    {
        var stageVm = Stages.FirstOrDefault(s => s.Stage == stage);
        if (stageVm != null)
            stageVm.Status = status;
    }
}
