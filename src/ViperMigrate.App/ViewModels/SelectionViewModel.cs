using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using System.Windows.Input;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;
using ViperMigrate.Core.Restore;
using ViperMigrate.Core.Restore.Applicators;

namespace ViperMigrate.App.ViewModels;

public class SelectableItem<T> : ViewModelBase
{
    private bool _isSelected = true;
    private bool _isVisible = true;

    public T Item { get; set; } = default!;
    public string DisplayName { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

public class SelectionViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private MigrationPackage _package = null!;
    private string _packagePath = string.Empty;
    private string _searchFilter = string.Empty;
    private string _selectedSummary = string.Empty;
    private bool _isBusy;
    private bool _includeOutlook = true;
    private string _outlookDetail = string.Empty;

    // Software-to-settings dependency mapping
    private readonly Dictionary<SelectableItem<CapturedSoftware>, List<SelectableItem<CapturedAppConfig>>> _softwareToSettings = new();

    // Virtual printer patterns to auto-deselect
    private static readonly string[] VirtualPrinterPatterns =
    {
        "Microsoft Print to PDF", "Microsoft XPS Document Writer",
        "OneNote", "Send to OneNote", "Fax", "Microsoft Fax",
        "Foxit", "CutePDF", "Adobe PDF", "PDFCreator"
    };

    // Hardware driver publishers
    private static readonly string[] HardwarePublishers =
    {
        "Intel", "AMD", "NVIDIA", "Realtek", "Synaptics", "Conexant",
        "Qualcomm", "Broadcom", "Marvell", "MediaTek"
    };

    private static readonly string[] HardwareKeywords =
    {
        "Driver", "Graphics", "Audio", "Chipset", "Ethernet",
        "WiFi Driver", "Bluetooth Driver", "Display Driver"
    };

    // Printer utility publishers
    private static readonly string[] PrinterUtilityPublishers =
    {
        "HP", "Canon", "Epson", "Brother", "Xerox", "Ricoh", "Lexmark", "Samsung"
    };

    private static readonly string[] PrinterUtilityKeywords =
    {
        "Utility", "Scan", "Monitor", "Status", "Toolbox",
        "Fax Driver", "Print Driver", "Device Setup"
    };

    // Anti-cheat patterns
    private static readonly string[] AntiCheatPatterns =
    {
        "EasyAntiCheat", "BattlEye", "Vanguard", "Riot Vanguard",
        "FACEIT Anti-Cheat", "nProtect GameGuard"
    };

    private static bool ShouldAutoDeselect(CapturedSoftware sw)
    {
        var name = sw.DisplayName;
        var publisher = sw.Publisher ?? "";

        // Hardware drivers
        if (HardwarePublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)) &&
            HardwareKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Printer utilities (not the printer itself, but scan/status/monitor apps)
        if (PrinterUtilityPublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)) &&
            PrinterUtilityKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Anti-cheat
        if (AntiCheatPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Windows updates/hotfixes
        if (publisher.Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("Hotfix", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public SelectionViewModel(MainViewModel main)
    {
        _main = main;
        StartRestoreCommand = new RelayCommand(ExecuteRestore, () => !_isBusy);
        BackCommand = new RelayCommand(() => _main.NavigateToRestore());

        // Software (split into winget and manual)
        SelectAllWingetSoftwareCommand = new RelayCommand(() => SetAll(WingetSoftwareItems, true));
        DeselectAllWingetSoftwareCommand = new RelayCommand(() => SetAll(WingetSoftwareItems, false));
        SelectAllManualSoftwareCommand = new RelayCommand(() => SetAll(ManualSoftwareItems, true));
        DeselectAllManualSoftwareCommand = new RelayCommand(() => SetAll(ManualSoftwareItems, false));

        SelectAllPrintersCommand = new RelayCommand(() => SetAll(PrinterItems, true));
        DeselectAllPrintersCommand = new RelayCommand(() => SetAll(PrinterItems, false));
        SelectAllBrowsersCommand = new RelayCommand(() => SetAll(BrowserItems, true));
        DeselectAllBrowsersCommand = new RelayCommand(() => SetAll(BrowserItems, false));
        SelectAllWiFiCommand = new RelayCommand(() => SetAll(WiFiItems, true));
        DeselectAllWiFiCommand = new RelayCommand(() => SetAll(WiFiItems, false));
        SelectAllShortcutsCommand = new RelayCommand(() => SetAll(ShortcutItems, true));
        DeselectAllShortcutsCommand = new RelayCommand(() => SetAll(ShortcutItems, false));
        SelectAllAppSettingsCommand = new RelayCommand(() => SetAll(AppSettingItems, true));
        DeselectAllAppSettingsCommand = new RelayCommand(() => SetAll(AppSettingItems, false));
        SelectAllDrivesCommand = new RelayCommand(() => SetAll(DriveItems, true));
        DeselectAllDrivesCommand = new RelayCommand(() => SetAll(DriveItems, false));
        SelectAllScannersCommand = new RelayCommand(() => SetAll(ScannerItems, true));
        DeselectAllScannersCommand = new RelayCommand(() => SetAll(ScannerItems, false));
        SelectAllOfficeRecentCommand = new RelayCommand(() => SetAll(OfficeRecentItems, true));
        DeselectAllOfficeRecentCommand = new RelayCommand(() => SetAll(OfficeRecentItems, false));
    }

    // Collections — Software split into winget and manual
    public ObservableCollection<SelectableItem<CapturedSoftware>> WingetSoftwareItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedSoftware>> ManualSoftwareItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedPrinter>> PrinterItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedBrowserProfile>> BrowserItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedWifiProfile>> WiFiItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedShortcut>> ShortcutItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedAppConfig>> AppSettingItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedDrive>> DriveItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedScanner>> ScannerItems { get; } = new();
    public ObservableCollection<SelectableItem<CapturedOfficeRecent>> OfficeRecentItems { get; } = new();

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (SetProperty(ref _searchFilter, value))
                ApplyFilter();
        }
    }

    public string SelectedSummary
    {
        get => _selectedSummary;
        set => SetProperty(ref _selectedSummary, value);
    }

    public bool IncludeOutlook
    {
        get => _includeOutlook;
        set
        {
            if (SetProperty(ref _includeOutlook, value))
                UpdateSummary();
        }
    }

    public bool HasOutlookConfig => _package?.OutlookConfig != null;

    public string OutlookDetail
    {
        get => _outlookDetail;
        set => SetProperty(ref _outlookDetail, value);
    }

    public ICommand StartRestoreCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SelectAllWingetSoftwareCommand { get; }
    public ICommand DeselectAllWingetSoftwareCommand { get; }
    public ICommand SelectAllManualSoftwareCommand { get; }
    public ICommand DeselectAllManualSoftwareCommand { get; }
    public ICommand SelectAllPrintersCommand { get; }
    public ICommand DeselectAllPrintersCommand { get; }
    public ICommand SelectAllBrowsersCommand { get; }
    public ICommand DeselectAllBrowsersCommand { get; }
    public ICommand SelectAllWiFiCommand { get; }
    public ICommand DeselectAllWiFiCommand { get; }
    public ICommand SelectAllShortcutsCommand { get; }
    public ICommand DeselectAllShortcutsCommand { get; }
    public ICommand SelectAllAppSettingsCommand { get; }
    public ICommand DeselectAllAppSettingsCommand { get; }
    public ICommand SelectAllDrivesCommand { get; }
    public ICommand DeselectAllDrivesCommand { get; }
    public ICommand SelectAllScannersCommand { get; }
    public ICommand DeselectAllScannersCommand { get; }
    public ICommand SelectAllOfficeRecentCommand { get; }
    public ICommand DeselectAllOfficeRecentCommand { get; }

    public void LoadPackage(MigrationPackage package, string packagePath)
    {
        _package = package;
        _packagePath = packagePath;
        _softwareToSettings.Clear();

        WingetSoftwareItems.Clear();
        ManualSoftwareItems.Clear();
        PrinterItems.Clear();
        BrowserItems.Clear();
        WiFiItems.Clear();
        ShortcutItems.Clear();
        AppSettingItems.Clear();
        DriveItems.Clear();
        ScannerItems.Clear();
        OfficeRecentItems.Clear();

        // Software — split into winget (auto) and manual
        foreach (var sw in package.Software.OrderBy(s => s.DisplayName))
        {
            var autoSkip = ShouldAutoDeselect(sw);
            var collection = sw.CanAutoInstall ? WingetSoftwareItems : ManualSoftwareItems;
            var status = sw.CanAutoInstall ? $"[winget: {sw.WingetId}]" : "[manual install]";
            var detail = autoSkip ? $"{sw.DisplayVersion} {status} (auto-skipped)" : $"{sw.DisplayVersion} {status}";

            var item = new SelectableItem<CapturedSoftware>
            {
                Item = sw,
                DisplayName = sw.DisplayName,
                Detail = detail,
                IsSelected = !autoSkip
            };
            collection.Add(item);

            // Subscribe for dependency cascade
            item.PropertyChanged += OnSoftwareSelectionChanged;
        }

        // Printers
        foreach (var p in package.Printers)
        {
            var isVirtual = VirtualPrinterPatterns.Any(v =>
                p.Name.Contains(v, StringComparison.OrdinalIgnoreCase));
            PrinterItems.Add(new SelectableItem<CapturedPrinter>
            {
                Item = p,
                DisplayName = p.Name,
                Detail = $"Driver: {p.DriverName}",
                IsSelected = !isVirtual
            });
        }

        // Browsers
        foreach (var b in package.BrowserProfiles)
        {
            var parts = new List<string>();
            if (b.BookmarksCaptured) parts.Add("bookmarks");
            if (b.PasswordsCaptured && b.Passwords.Count > 0) parts.Add($"{b.Passwords.Count} passwords");
            if (b.ExtensionIds.Count > 0) parts.Add($"{b.ExtensionIds.Count} extensions");
            var isEmpty = !b.BookmarksCaptured && b.Passwords.Count == 0 && b.ExtensionIds.Count == 0;

            BrowserItems.Add(new SelectableItem<CapturedBrowserProfile>
            {
                Item = b,
                DisplayName = $"{b.BrowserName} ({b.ProfileName})",
                Detail = parts.Count > 0 ? string.Join(", ", parts) : "empty profile",
                IsSelected = !isEmpty
            });
        }

        // WiFi
        foreach (var w in package.WifiProfiles)
        {
            var isOpen = string.IsNullOrEmpty(w.AuthType) ||
                         w.AuthType.Equals("Open", StringComparison.OrdinalIgnoreCase);
            WiFiItems.Add(new SelectableItem<CapturedWifiProfile>
            {
                Item = w,
                DisplayName = w.Ssid,
                Detail = $"Auth: {w.AuthType}",
                IsSelected = !isOpen
            });
        }

        // Shortcuts
        foreach (var s in package.Shortcuts)
        {
            var location = s.IsDesktopShortcut ? "Desktop"
                : s.IsTaskbarPinned ? "Taskbar"
                : s.IsStartMenu ? "Start Menu"
                : s.IsStartup ? "Startup"
                : s.IsQuickLaunch ? "Quick Launch"
                : "Other";

            ShortcutItems.Add(new SelectableItem<CapturedShortcut>
            {
                Item = s,
                DisplayName = s.Name,
                Detail = $"{location} → {s.TargetPath}",
                GroupName = location,
                IsSelected = true
            });
        }

        // Mapped Drives
        foreach (var d in package.MappedDrives)
        {
            var detail = !string.IsNullOrEmpty(d.Label) ? $"{d.UncPath} ({d.Label})" : d.UncPath;
            DriveItems.Add(new SelectableItem<CapturedDrive>
            {
                Item = d,
                DisplayName = d.DriveLetter,
                Detail = detail,
                IsSelected = true
            });
        }

        // Scanners
        foreach (var sc in package.Scanners)
        {
            var netInfo = sc.IsNetworkScanner ? $"Network ({sc.NetworkAddress})" : "Local";
            ScannerItems.Add(new SelectableItem<CapturedScanner>
            {
                Item = sc,
                DisplayName = sc.DeviceName,
                Detail = $"{sc.Manufacturer} | {netInfo}",
                IsSelected = true
            });
        }

        // Outlook Config (single toggle)
        _includeOutlook = package.OutlookConfig != null;
        if (package.OutlookConfig != null)
        {
            var oc = package.OutlookConfig;
            var ocParts = new List<string>();
            if (!string.IsNullOrEmpty(oc.OutlookVersion)) ocParts.Add($"Outlook {oc.OutlookVersion}");
            if (oc.Accounts.Count > 0) ocParts.Add($"{oc.Accounts.Count} accounts");
            if (oc.PstPaths.Count > 0) ocParts.Add($"{oc.PstPaths.Count} PST files");
            if (oc.SignaturesCaptured) ocParts.Add("signatures");
            OutlookDetail = string.Join(" | ", ocParts);
        }
        OnPropertyChanged(nameof(HasOutlookConfig));
        OnPropertyChanged(nameof(IncludeOutlook));

        // Office Recent Files
        foreach (var or in package.OfficeRecentFiles)
        {
            OfficeRecentItems.Add(new SelectableItem<CapturedOfficeRecent>
            {
                Item = or,
                DisplayName = or.FileName,
                Detail = $"{or.ApplicationName} | {or.FilePath}",
                IsSelected = true
            });
        }

        // App Settings + dependency mapping
        if (package.AppSettings != null)
        {
            foreach (var a in package.AppSettings.CapturedApps)
            {
                var detail = $"{a.SourceType} | {a.FileCount} files | {a.SizeBytes / 1024}KB";
                var selected = true;

                // Find linked software item for cascade
                var linkedSw = FindLinkedSoftware(a.AppName);
                if (linkedSw != null)
                {
                    detail += $" (linked to {linkedSw.DisplayName})";
                }
                else if (!a.MatchedToInventory)
                {
                    detail += " (unmatched)";
                }

                var settingItem = new SelectableItem<CapturedAppConfig>
                {
                    Item = a,
                    DisplayName = a.AppName,
                    Detail = detail,
                    IsSelected = selected
                };
                AppSettingItems.Add(settingItem);

                // Register dependency
                if (linkedSw != null)
                {
                    if (!_softwareToSettings.ContainsKey(linkedSw))
                        _softwareToSettings[linkedSw] = new List<SelectableItem<CapturedAppConfig>>();
                    _softwareToSettings[linkedSw].Add(settingItem);
                }
            }
        }

        UpdateSummary();
    }

    /// <summary>
    /// Finds a software item whose DisplayName matches the app config name.
    /// Uses exact match first, then contains match (min 4 chars to avoid false positives).
    /// </summary>
    private SelectableItem<CapturedSoftware>? FindLinkedSoftware(string appName)
    {
        if (string.IsNullOrEmpty(appName) || appName.Length < 3) return null;

        foreach (var item in WingetSoftwareItems.Concat(ManualSoftwareItems))
        {
            var displayName = item.Item.DisplayName;

            // Exact match
            if (displayName.Equals(appName, StringComparison.OrdinalIgnoreCase))
                return item;

            // DisplayName contains AppName (e.g., "Mozilla Thunderbird" contains "Thunderbird")
            if (appName.Length >= 4 && displayName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                return item;

            // AppName contains DisplayName (less common)
            if (displayName.Length >= 4 && appName.Contains(displayName, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    /// <summary>
    /// When a software item is deselected, auto-deselect its linked app settings.
    /// When re-selected, auto-re-select them.
    /// </summary>
    private void OnSoftwareSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectableItem<CapturedSoftware>.IsSelected)) return;
        if (sender is not SelectableItem<CapturedSoftware> swItem) return;

        if (_softwareToSettings.TryGetValue(swItem, out var linked))
        {
            foreach (var setting in linked)
                setting.IsSelected = swItem.IsSelected;
        }

        UpdateSummary();
    }

    public MigrationPackage BuildFilteredPackage()
    {
        var filtered = new MigrationPackage
        {
            Version = _package.Version,
            SourceMachine = _package.SourceMachine,
            WingetExportCaptured = _package.WingetExportCaptured,
            OutlookConfig = _includeOutlook ? _package.OutlookConfig : null,
            ThunderbirdProfiles = _package.ThunderbirdProfiles,
            StartupEntries = _package.StartupEntries
        };

        // Software from both collections
        foreach (var item in WingetSoftwareItems.Where(i => i.IsSelected))
            filtered.Software.Add(item.Item);
        foreach (var item in ManualSoftwareItems.Where(i => i.IsSelected))
            filtered.Software.Add(item.Item);

        foreach (var item in PrinterItems.Where(i => i.IsSelected))
            filtered.Printers.Add(item.Item);
        foreach (var item in BrowserItems.Where(i => i.IsSelected))
            filtered.BrowserProfiles.Add(item.Item);
        foreach (var item in WiFiItems.Where(i => i.IsSelected))
            filtered.WifiProfiles.Add(item.Item);
        foreach (var item in ShortcutItems.Where(i => i.IsSelected))
            filtered.Shortcuts.Add(item.Item);

        // Mapped Drives (filtered)
        foreach (var item in DriveItems.Where(i => i.IsSelected))
            filtered.MappedDrives.Add(item.Item);

        // Scanners (filtered)
        foreach (var item in ScannerItems.Where(i => i.IsSelected))
            filtered.Scanners.Add(item.Item);

        // Office Recent (filtered)
        foreach (var item in OfficeRecentItems.Where(i => i.IsSelected))
            filtered.OfficeRecentFiles.Add(item.Item);

        // AppSettings
        if (_package.AppSettings != null)
        {
            filtered.AppSettings = new CapturedAppSettings();
            foreach (var item in AppSettingItems.Where(i => i.IsSelected))
                filtered.AppSettings.CapturedApps.Add(item.Item);
        }

        return filtered;
    }

    private void ApplyFilter()
    {
        var filter = _searchFilter?.Trim() ?? "";
        ApplyFilterToCollection(WingetSoftwareItems, filter);
        ApplyFilterToCollection(ManualSoftwareItems, filter);
        ApplyFilterToCollection(PrinterItems, filter);
        ApplyFilterToCollection(BrowserItems, filter);
        ApplyFilterToCollection(WiFiItems, filter);
        ApplyFilterToCollection(ShortcutItems, filter);
        ApplyFilterToCollection(AppSettingItems, filter);
        ApplyFilterToCollection(DriveItems, filter);
        ApplyFilterToCollection(ScannerItems, filter);
        ApplyFilterToCollection(OfficeRecentItems, filter);
    }

    private static void ApplyFilterToCollection<T>(ObservableCollection<SelectableItem<T>> items, string filter)
    {
        foreach (var item in items)
        {
            item.IsVisible = string.IsNullOrEmpty(filter) ||
                             item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                             item.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void SetAll<T>(ObservableCollection<SelectableItem<T>> items, bool selected)
    {
        foreach (var item in items)
            item.IsSelected = selected;
    }

    private void UpdateSummary()
    {
        var parts = new List<string>();
        var swWinget = WingetSoftwareItems.Count(i => i.IsSelected);
        var swManual = ManualSoftwareItems.Count(i => i.IsSelected);
        var swCount = swWinget + swManual;
        if (swCount > 0) parts.Add($"{swCount} software ({swWinget} auto, {swManual} manual)");
        var pCount = PrinterItems.Count(i => i.IsSelected);
        if (pCount > 0) parts.Add($"{pCount} printers");
        var bCount = BrowserItems.Count(i => i.IsSelected);
        if (bCount > 0) parts.Add($"{bCount} browsers");
        var wCount = WiFiItems.Count(i => i.IsSelected);
        if (wCount > 0) parts.Add($"{wCount} WiFi");
        var dCount = DriveItems.Count(i => i.IsSelected);
        if (dCount > 0) parts.Add($"{dCount} mapped drives");
        var scCount = ScannerItems.Count(i => i.IsSelected);
        if (scCount > 0) parts.Add($"{scCount} scanners");
        if (_includeOutlook && _package?.OutlookConfig != null) parts.Add("Outlook");
        var orCount = OfficeRecentItems.Count(i => i.IsSelected);
        if (orCount > 0) parts.Add($"{orCount} Office recent");
        var sCount = ShortcutItems.Count(i => i.IsSelected);
        if (sCount > 0) parts.Add($"{sCount} shortcuts");
        var aCount = AppSettingItems.Count(i => i.IsSelected);
        if (aCount > 0) parts.Add($"{aCount} app settings");

        SelectedSummary = parts.Count > 0
            ? string.Join(", ", parts)
            : "Nothing selected";
    }

    private async void ExecuteRestore()
    {
        if (_isBusy) return;

        // Admin check
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                System.Windows.MessageBox.Show(
                    "Restore requires administrator privileges.\n\nPlease restart ViperMigrate as Administrator.",
                    "Admin Required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        _isBusy = true;

        var filteredPackage = BuildFilteredPackage();

        // Determine which categories have items
        var selectedCategories = new List<string>();
        if (filteredPackage.Software.Count > 0) selectedCategories.Add("Software");
        if (filteredPackage.Printers.Count > 0) selectedCategories.Add("Printers");
        if (filteredPackage.Scanners.Count > 0) selectedCategories.Add("Scanners");
        if (filteredPackage.MappedDrives.Count > 0) selectedCategories.Add("Mapped Drives");
        if (filteredPackage.BrowserProfiles.Count > 0) selectedCategories.Add("Browsers");
        if (filteredPackage.WifiProfiles.Count > 0) selectedCategories.Add("WiFi");
        if (filteredPackage.Shortcuts.Count > 0 || filteredPackage.StartupEntries.Count > 0) selectedCategories.Add("Shortcuts");
        if (filteredPackage.OutlookConfig != null) selectedCategories.Add("Outlook");
        if (filteredPackage.OfficeRecentFiles.Count > 0) selectedCategories.Add("Office Recent");
        if (filteredPackage.AppSettings != null && filteredPackage.AppSettings.CapturedApps.Count > 0) selectedCategories.Add("App Settings");

        // Pre-restore validation
        try
        {
            var validation = await RestoreValidator.ValidateAsync(filteredPackage);
            if (validation.Warnings.Count > 0)
            {
                var warningText = string.Join("\n", validation.Warnings);
                var mbResult = System.Windows.MessageBox.Show(
                    $"Pre-restore warnings:\n\n{warningText}\n\nContinue anyway?",
                    "Restore Validation",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (mbResult != System.Windows.MessageBoxResult.Yes)
                {
                    _isBusy = false;
                    return;
                }
            }
        }
        catch { }

        // Initialize logging
        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ViperMigrate", "Logs");
        var log = new LogManager(logDir);
        log.Info($"Restore started. Categories: {string.Join(", ", selectedCategories)}");

        // Register applicators
        var engine = new RestoreEngine();
        engine.Register(new WifiApplicator(filteredPackage));
        engine.Register(new DriveApplicator(filteredPackage));
        engine.Register(new SoftwareApplicator(filteredPackage));
        engine.Register(new PrinterApplicator(filteredPackage));
        engine.Register(new ScannerApplicator(filteredPackage));
        engine.Register(new BrowserApplicator(filteredPackage));
        engine.Register(new ShortcutApplicator(filteredPackage));
        engine.Register(new OutlookApplicator(filteredPackage));
        engine.Register(new OfficeRecentApplicator(filteredPackage));
        engine.Register(new AppSettingsApplicator(filteredPackage));
        engine.Register(new VerificationApplicator(filteredPackage));

        // Initialize progress
        var progressVm = _main.ProgressViewModel;
        var stageGroups = engine.GetStageGroups(selectedCategories);
        progressVm.InitializeStaged(stageGroups);
        _main.NavigateToProgress();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                progressVm.CurrentOperation = msg;
                log.Info(msg);
                foreach (var cat in selectedCategories)
                {
                    if (msg.StartsWith($"Restoring {cat}"))
                        progressVm.UpdateCategory(cat, CategoryStatus.InProgress);
                }
            });

            var stageProgress = new Progress<StageProgressInfo>(info =>
            {
                progressVm.UpdateStage(info.Stage, info.Status);
            });

            var categoryProgress = new Progress<CategoryProgressInfo>(info =>
            {
                progressVm.UpdateCategory(info.Category, info.Status);
                log.Info($"Category '{info.Category}' completed: {info.Status}");
            });

            var stagedResult = await engine.RunStagedAsync(
                _packagePath, selectedCategories, progress, stageProgress, categoryProgress, CancellationToken.None);

            progressVm.CurrentOperation = "Restore complete!";
            progressVm.OverallProgress = 100;
            log.Info("Restore completed successfully.");

            _main.SummaryViewModel.LoadResults(stagedResult.CategoryResults, filteredPackage.ManualActions, _packagePath, filteredPackage.SourceMachine);
            _main.NavigateToSummary();
        }
        catch (Exception ex)
        {
            log.Error("Restore failed", ex);
            progressVm.CurrentOperation = $"Error: {ex.Message}";
            System.Windows.MessageBox.Show($"Restore failed: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            log.Dispose();
            _isBusy = false;
        }
    }
}
