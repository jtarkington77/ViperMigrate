using System.Windows.Input;

namespace ViperMigrate.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentView;
    private readonly CaptureViewModel _captureViewModel;
    private readonly RestoreViewModel _restoreViewModel;
    private readonly ProgressViewModel _progressViewModel;
    private readonly SummaryViewModel _summaryViewModel;
    private readonly SelectionViewModel _selectionViewModel;

    public MainViewModel()
    {
        _captureViewModel = new CaptureViewModel(this);
        _restoreViewModel = new RestoreViewModel(this);
        _progressViewModel = new ProgressViewModel(this);
        _summaryViewModel = new SummaryViewModel(this);
        _selectionViewModel = new SelectionViewModel(this);
        _currentView = null!;

        NavigateToCaptureCommand = new RelayCommand(() => NavigateTo(_captureViewModel));
        NavigateToRestoreCommand = new RelayCommand(() => NavigateTo(_restoreViewModel));
        NavigateToHomeCommand = new RelayCommand(() => CurrentView = null!);

        // Start with no view selected (home screen)
        _currentView = null!;
    }

    public ViewModelBase? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView!, value);
    }

    public ICommand NavigateToCaptureCommand { get; }
    public ICommand NavigateToRestoreCommand { get; }
    public ICommand NavigateToHomeCommand { get; }

    public void NavigateTo(ViewModelBase viewModel)
    {
        CurrentView = viewModel;
    }

    public void NavigateToProgress() => NavigateTo(_progressViewModel);
    public void NavigateToSummary() => NavigateTo(_summaryViewModel);
    public void NavigateToSelection() => NavigateTo(_selectionViewModel);
    public void NavigateToRestore() => NavigateTo(_restoreViewModel);

    public ProgressViewModel ProgressViewModel => _progressViewModel;
    public SummaryViewModel SummaryViewModel => _summaryViewModel;
    public SelectionViewModel SelectionViewModel => _selectionViewModel;
}
