using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.App.ViewModels;

public class SummaryItemViewModel
{
    public string Category { get; set; } = string.Empty;
    public CategoryStatus Status { get; set; }
    public string StatusDisplay { get; set; } = string.Empty;
    public int ItemsProcessed { get; set; }
    public int ItemsTotal { get; set; }
    public string Detail { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public class CheckableManualAction : INotifyPropertyChanged
{
    private bool _isChecked;

    public ManualAction Action { get; }

    public CheckableManualAction(ManualAction action)
    {
        Action = action;
        _isChecked = action.IsChecked;
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                Action.IsChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }

    public string Title => Action.Title;
    public string Description => Action.Description;
    public ManualActionPriority Priority => Action.Priority;
    public string? Command => Action.Command;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class SummaryViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private string _packagePath = string.Empty;
    private MachineInfo? _sourceInfo;

    public SummaryViewModel(MainViewModel main)
    {
        _main = main;
        Results = new ObservableCollection<CategoryResult>();
        ManualActions = new ObservableCollection<ManualAction>();
        DetailedResults = new ObservableCollection<SummaryItemViewModel>();
        CheckableActions = new ObservableCollection<CheckableManualAction>();

        BackToHomeCommand = new RelayCommand(() => _main.NavigateToHomeCommand.Execute(null));
        ExportReportCommand = new RelayCommand(ExportReport);
    }

    public ObservableCollection<CategoryResult> Results { get; }
    public ObservableCollection<ManualAction> ManualActions { get; }
    public ObservableCollection<SummaryItemViewModel> DetailedResults { get; }
    public ObservableCollection<CheckableManualAction> CheckableActions { get; }

    public ICommand BackToHomeCommand { get; }
    public ICommand ExportReportCommand { get; }

    public int SuccessCount => Results.Count(r => r.Status == CategoryStatus.Success);
    public int WarningCount => Results.Count(r => r.Status == CategoryStatus.PartialSuccess);
    public int ErrorCount => Results.Count(r => r.Status == CategoryStatus.Failed);

    public void LoadResults(List<CategoryResult> results, List<ManualAction>? packageManualActions = null, string? packagePath = null, MachineInfo? sourceInfo = null)
    {
        _packagePath = packagePath ?? string.Empty;
        _sourceInfo = sourceInfo;

        Results.Clear();
        ManualActions.Clear();
        DetailedResults.Clear();
        CheckableActions.Clear();

        foreach (var r in results)
        {
            Results.Add(r);

            // Build detailed summary
            var detail = BuildDetailString(r);
            DetailedResults.Add(new SummaryItemViewModel
            {
                Category = r.Category,
                Status = r.Status,
                StatusDisplay = r.Status switch
                {
                    CategoryStatus.Success => "Success",
                    CategoryStatus.PartialSuccess => "Partial",
                    CategoryStatus.Failed => "Failed",
                    CategoryStatus.Skipped => "Skipped",
                    _ => r.Status.ToString()
                },
                ItemsProcessed = r.ItemsProcessed,
                ItemsTotal = r.ItemsTotal,
                Detail = detail,
                Warnings = r.Warnings.ToList()
            });

            foreach (var action in r.ManualActions)
            {
                ManualActions.Add(action);
                CheckableActions.Add(new CheckableManualAction(action));
            }
        }

        if (packageManualActions != null)
        {
            foreach (var action in packageManualActions)
            {
                ManualActions.Add(action);
                CheckableActions.Add(new CheckableManualAction(action));
            }
        }

        OnPropertyChanged(nameof(SuccessCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
    }

    private static string BuildDetailString(CategoryResult r)
    {
        if (r.Status == CategoryStatus.Skipped)
            return "Skipped";

        var parts = new List<string>();

        if (r.ItemsTotal > 0)
        {
            parts.Add($"{r.ItemsProcessed} of {r.ItemsTotal} processed");
        }

        if (r.Warnings.Count > 0)
            parts.Add($"{r.Warnings.Count} warning(s)");

        if (r.ManualActions.Count > 0)
            parts.Add($"{r.ManualActions.Count} manual action(s)");

        if (r.Duration != TimeSpan.Zero)
            parts.Add($"{r.Duration.TotalSeconds:F1}s");

        return parts.Count > 0 ? string.Join(", ", parts) : "Complete";
    }

    private void ExportReport()
    {
        try
        {
            var allManualActions = CheckableActions.Select(ca => ca.Action).ToList();
            var html = HtmlReportGenerator.Generate(Results.ToList(), allManualActions, _sourceInfo);

            var reportDir = !string.IsNullOrEmpty(_packagePath) && Directory.Exists(_packagePath)
                ? _packagePath
                : Path.GetTempPath();
            var reportPath = Path.Combine(reportDir, $"ViperMigrate_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            File.WriteAllText(reportPath, html);

            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to export report: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
