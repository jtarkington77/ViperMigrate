using System.Collections.ObjectModel;
using System.Windows.Input;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.App.ViewModels;

public class RestoreViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private string _packagePath = string.Empty;
    private string _password = string.Empty;
    private bool _isPackageLoaded;
    private string _packageInfo = string.Empty;
    private bool _isBusy; // Used for CanExecute checks
    private MigrationPackage? _loadedPackage;
    private ObservableCollection<CategorySelection> _categories = new();

    public RestoreViewModel(MainViewModel main)
    {
        _main = main;

        BrowseCommand = new RelayCommand(Browse);
        LoadPackageCommand = new RelayCommand(LoadPackage, CanLoadPackage);
        StartRestoreCommand = new RelayCommand(StartRestore, CanStartRestore);
        BackCommand = new RelayCommand(() => _main.NavigateToHomeCommand.Execute(null));
    }

    public string PackagePath
    {
        get => _packagePath;
        set => SetProperty(ref _packagePath, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool IsPackageLoaded
    {
        get => _isPackageLoaded;
        set => SetProperty(ref _isPackageLoaded, value);
    }

    public string PackageInfo
    {
        get => _packageInfo;
        set => SetProperty(ref _packageInfo, value);
    }

    public ObservableCollection<CategorySelection> Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value);
    }

    public ICommand BrowseCommand { get; }
    public ICommand LoadPackageCommand { get; }
    public ICommand StartRestoreCommand { get; }
    public ICommand BackCommand { get; }

    private bool CanLoadPackage() => !string.IsNullOrWhiteSpace(PackagePath) && !_isBusy;

    private void LoadPackage()
    {
        try
        {
            var reader = new PackageReader();
            if (!reader.IsValidPackage(PackagePath))
            {
                PackageInfo = "Invalid package: no manifest found.";
                IsPackageLoaded = false;
                return;
            }

            if (reader.IsEncryptedPackage(PackagePath))
            {
                if (string.IsNullOrEmpty(Password))
                {
                    PackageInfo = "This package is encrypted. Enter the password and click Load again.";
                    IsPackageLoaded = false;
                    return;
                }

                try
                {
                    _loadedPackage = reader.ReadEncryptedManifest(PackagePath, Password);
                }
                catch
                {
                    PackageInfo = "Failed to decrypt. Check your password.";
                    IsPackageLoaded = false;
                    return;
                }
            }
            else
            {
                _loadedPackage = reader.ReadManifest(PackagePath);
            }

            // Build info display
            var info = _loadedPackage.SourceMachine;
            var lines = new List<string>
            {
                $"Computer: {info.ComputerName}",
                $"User: {info.Domain}\\{info.UserName}",
                $"OS: {info.OsVersion}",
                $"Captured: {info.CaptureDate:yyyy-MM-dd HH:mm} UTC",
                "",
                "Categories captured:"
            };

            // Build category list with counts
            var cats = new List<(string Name, int Count)>();
            if (_loadedPackage.Software.Count > 0) cats.Add(("Software", _loadedPackage.Software.Count));
            if (_loadedPackage.Printers.Count > 0) cats.Add(("Printers", _loadedPackage.Printers.Count));
            if (_loadedPackage.Scanners.Count > 0) cats.Add(("Scanners", _loadedPackage.Scanners.Count));
            if (_loadedPackage.MappedDrives.Count > 0) cats.Add(("Mapped Drives", _loadedPackage.MappedDrives.Count));
            if (_loadedPackage.BrowserProfiles.Count > 0) cats.Add(("Browsers", _loadedPackage.BrowserProfiles.Count));
            if (_loadedPackage.WifiProfiles.Count > 0) cats.Add(("WiFi", _loadedPackage.WifiProfiles.Count));
            if (_loadedPackage.Shortcuts.Count > 0 || _loadedPackage.StartupEntries.Count > 0)
                cats.Add(("Shortcuts", _loadedPackage.Shortcuts.Count + _loadedPackage.StartupEntries.Count));
            if (_loadedPackage.OutlookConfig != null) cats.Add(("Outlook", _loadedPackage.OutlookConfig.Accounts.Count + _loadedPackage.OutlookConfig.SignatureFiles.Count));
            if (_loadedPackage.OfficeRecentFiles.Count > 0) cats.Add(("Office Recent", _loadedPackage.OfficeRecentFiles.Count));
            if (_loadedPackage.AppSettings != null && _loadedPackage.AppSettings.CapturedApps.Count > 0)
                cats.Add(("App Settings", _loadedPackage.AppSettings.CapturedApps.Count));

            foreach (var (name, count) in cats)
                lines.Add($"  - {name} ({count} items)");

            if (cats.Count == 0)
                lines.Add("  (no data captured)");

            PackageInfo = string.Join("\n", lines);

            // Build category selection for restore
            Categories.Clear();
            foreach (var (name, count) in cats)
            {
                Categories.Add(new CategorySelection
                {
                    Name = name,
                    Description = $"{count} items",
                    IsSelected = true
                });
            }

            IsPackageLoaded = true;
        }
        catch (Exception ex)
        {
            PackageInfo = $"Error loading package: {ex.Message}";
            IsPackageLoaded = false;
        }
    }

    private bool CanStartRestore() => IsPackageLoaded && !_isBusy;

    private void StartRestore()
    {
        if (_isBusy || _loadedPackage == null) return;

        // Navigate to item-level selection screen
        _main.SelectionViewModel.LoadPackage(_loadedPackage, PackagePath);
        _main.NavigateToSelection();
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Migration Package Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            PackagePath = dialog.FolderName;
        }
    }
}
