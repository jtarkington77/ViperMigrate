using System.Collections.ObjectModel;
using System.Windows.Input;
using ViperMigrate.Core.Capture;
using ViperMigrate.Core.Capture.Collectors;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.App.ViewModels;

public class CategorySelection : ViewModelBase
{
    private bool _isSelected = true;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public class CaptureViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private string _destinationPath = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _isBusy;

    public CaptureViewModel(MainViewModel main)
    {
        _main = main;

        Categories = new ObservableCollection<CategorySelection>
        {
            new() { Name = "Software", Description = "Installed software inventory" },
            new() { Name = "Printers", Description = "Printer configurations" },
            new() { Name = "Scanners", Description = "Scanner profiles" },
            new() { Name = "Mapped Drives", Description = "Network drive mappings" },
            new() { Name = "Browsers", Description = "Bookmarks, extensions, profiles" },
            new() { Name = "WiFi", Description = "Saved WiFi networks" },
            new() { Name = "Shortcuts", Description = "Desktop and taskbar shortcuts" },
            new() { Name = "Outlook", Description = "Email configuration and signatures" },
            new() { Name = "Office Recent", Description = "Recent and pinned Office files" },
            new() { Name = "App Settings", Description = "Application configurations and preferences" }
        };

        StartCaptureCommand = new RelayCommand(StartCapture, CanStartCapture);
        BackCommand = new RelayCommand(() => _main.NavigateToHomeCommand.Execute(null));
        BrowseCommand = new RelayCommand(Browse);
    }

    public ObservableCollection<CategorySelection> Categories { get; }

    public string DestinationPath
    {
        get => _destinationPath;
        set => SetProperty(ref _destinationPath, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public ICommand StartCaptureCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand BrowseCommand { get; }

    private bool CanStartCapture()
    {
        return !_isBusy
               && !string.IsNullOrWhiteSpace(DestinationPath)
               && Categories.Any(c => c.IsSelected);
    }

    private async void StartCapture()
    {
        if (_isBusy) return;

        // Validate password match
        if (!string.IsNullOrEmpty(Password) && Password != ConfirmPassword)
        {
            System.Windows.MessageBox.Show("Passwords do not match.", "Validation",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        _isBusy = true;
        var selectedCategories = Categories.Where(c => c.IsSelected).Select(c => c.Name).ToList();

        // Initialize progress view
        var progressVm = _main.ProgressViewModel;
        progressVm.Initialize(selectedCategories);
        _main.NavigateToProgress();

        // Initialize logging
        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViperMigrate", "Logs");
        var log = new LogManager(logDir);
        log.Info($"Capture started. Categories: {string.Join(", ", selectedCategories)}");

        try
        {
            // Create package
            var writer = new PackageWriter();
            var packagePath = writer.CreatePackageFolder(DestinationPath, Environment.MachineName);

            var package = new MigrationPackage
            {
                SourceMachine = new MachineInfo
                {
                    ComputerName = Environment.MachineName,
                    UserName = Environment.UserName,
                    Domain = Environment.UserDomainName,
                    OsVersion = Environment.OSVersion.VersionString,
                    OsBuild = Environment.OSVersion.Version.Build.ToString(),
                    OsBuildNumber = Environment.OSVersion.Version.Build,
                    CaptureDate = DateTime.UtcNow
                }
            };

            // Register collectors
            var engine = new CaptureEngine();
            engine.Register(new SoftwareCollector(package));
            engine.Register(new PrinterCollector(package));
            engine.Register(new ScannerCollector(package));
            engine.Register(new DriveCollector(package));
            engine.Register(new BrowserCollector(package));
            engine.Register(new WifiCollector(package));
            engine.Register(new ShortcutCollector(package));
            engine.Register(new OutlookCollector(package));
            engine.Register(new OfficeRecentCollector(package));
            engine.Register(new AppSettingsCollector(package));

            // Progress handler
            var progress = new Progress<string>(msg =>
            {
                progressVm.CurrentOperation = msg;
                log.Info(msg);

                // Update category status based on message
                foreach (var cat in selectedCategories)
                {
                    if (msg.StartsWith($"Capturing {cat}"))
                        progressVm.UpdateCategory(cat, CategoryStatus.InProgress);
                }
            });

            // Run capture
            var results = await engine.RunAsync(packagePath, selectedCategories, progress, CancellationToken.None);

            // Update final statuses
            foreach (var r in results)
            {
                progressVm.UpdateCategory(r.Category, r.Status);
                log.Info($"Category '{r.Category}' completed: {r.Status}");
            }

            // Store results in package
            package.CaptureResults = results;
            foreach (var r in results)
            {
                foreach (var action in r.ManualActions)
                    package.ManualActions.Add(action);
            }

            // Write manifest
            if (!string.IsNullOrEmpty(Password))
                writer.WriteEncryptedManifest(packagePath, package, Password);
            else
                writer.WriteManifest(packagePath, package);

            progressVm.CurrentOperation = "Capture complete!";
            progressVm.OverallProgress = 100;
            log.Info("Capture completed successfully.");

            // Navigate to summary
            _main.SummaryViewModel.LoadResults(results);
            _main.NavigateToSummary();
        }
        catch (Exception ex)
        {
            log.Error("Capture failed", ex);
            progressVm.CurrentOperation = $"Error: {ex.Message}";
            System.Windows.MessageBox.Show($"Capture failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            log.Dispose();
            _isBusy = false;
        }
    }

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Destination Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            DestinationPath = dialog.FolderName;
        }
    }
}
