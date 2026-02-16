using System.Text.Json;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Tests;

public class ModelSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void MigrationPackage_SerializeDeserialize()
    {
        var package = new MigrationPackage
        {
            SourceMachine = new MachineInfo
            {
                ComputerName = "PC1",
                UserName = "user1",
                Domain = "CORP"
            }
        };

        var json = JsonSerializer.Serialize(package, Options);
        var deserialized = JsonSerializer.Deserialize<MigrationPackage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("1.2", deserialized.Version);
        Assert.Equal("PC1", deserialized.SourceMachine.ComputerName);
    }

    [Fact]
    public void CapturedPrinter_SerializeDeserialize()
    {
        var printer = new CapturedPrinter
        {
            Name = "HP LaserJet Pro",
            DriverName = "HP Universal Driver",
            PortName = "IP_10.0.0.50",
            IsDefault = true,
            IsNetworkPrinter = true,
            UncPath = @"\\server\printer1"
        };

        var json = JsonSerializer.Serialize(printer, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedPrinter>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(printer.Name, deserialized.Name);
        Assert.Equal(printer.UncPath, deserialized.UncPath);
        Assert.True(deserialized.IsDefault);
        Assert.True(deserialized.IsNetworkPrinter);
    }

    [Fact]
    public void CapturedPrinter_WithHostAddress_SerializeDeserialize()
    {
        var printer = new CapturedPrinter
        {
            Name = "HP LaserJet Pro",
            DriverName = "HP Universal Driver",
            PortName = "IP_10.0.0.50",
            IsDefault = true,
            IsNetworkPrinter = false,
            PrinterHostAddress = "10.0.0.50"
        };

        var json = JsonSerializer.Serialize(printer, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedPrinter>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("10.0.0.50", deserialized.PrinterHostAddress);
        Assert.Equal(printer.PortName, deserialized.PortName);
    }

    [Fact]
    public void CapturedWifiProfile_SerializeDeserialize()
    {
        var wifi = new CapturedWifiProfile
        {
            Ssid = "CorpWiFi",
            AuthType = "WPA2-Enterprise",
            EncryptionType = "AES",
            HasPassword = true,
            EncryptedPassword = "base64encrypted=="
        };

        var json = JsonSerializer.Serialize(wifi, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedWifiProfile>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("CorpWiFi", deserialized.Ssid);
        Assert.True(deserialized.HasPassword);
    }

    [Fact]
    public void CapturedScanner_WithProfiles_SerializeDeserialize()
    {
        var scanner = new CapturedScanner
        {
            DeviceName = "Fujitsu ScanSnap",
            Manufacturer = "Fujitsu",
            Profiles =
            {
                new ScanProfile
                {
                    ProfileName = "Documents",
                    FileType = "PDF",
                    ColorMode = "Color",
                    Resolution = 300
                }
            }
        };

        var json = JsonSerializer.Serialize(scanner, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedScanner>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Profiles);
        Assert.Equal("Documents", deserialized.Profiles[0].ProfileName);
        Assert.Equal(300, deserialized.Profiles[0].Resolution);
    }

    [Fact]
    public void CategoryResult_SerializeDeserialize()
    {
        var result = new CategoryResult
        {
            Category = "Printers",
            Status = CategoryStatus.PartialSuccess,
            ItemsProcessed = 3,
            ItemsTotal = 4,
            Warnings = { "Printer 'OldPrinter' not found on network" },
            ManualActions =
            {
                new ManualAction
                {
                    Category = "Printers",
                    Title = "Install Driver",
                    Description = "Download and install HP driver manually",
                    Priority = ManualActionPriority.High
                }
            }
        };

        var json = JsonSerializer.Serialize(result, Options);
        var deserialized = JsonSerializer.Deserialize<CategoryResult>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(CategoryStatus.PartialSuccess, deserialized.Status);
        Assert.Single(deserialized.Warnings);
        Assert.Single(deserialized.ManualActions);
        Assert.Equal(ManualActionPriority.High, deserialized.ManualActions[0].Priority);
    }

    [Fact]
    public void BackwardCompat_V10Package_DeserializesWithoutNewFields()
    {
        // Simulate a v1.0 package that lacks ThunderbirdProfiles, AppSettings, OsBuildNumber, Extensions
        var v10Json = """
        {
            "version": "1.0",
            "sourceMachine": {
                "computerName": "OLD-PC",
                "userName": "user",
                "domain": "CORP",
                "osVersion": "Windows 10",
                "osBuild": "19045.3803",
                "captureDate": "2024-01-15T10:00:00Z"
            },
            "software": [],
            "printers": [],
            "scanners": [],
            "mappedDrives": [],
            "browserProfiles": [
                {
                    "browserName": "Chrome",
                    "profileName": "Default",
                    "profilePath": "C:\\path",
                    "bookmarksCaptured": true,
                    "extensionsCaptured": true,
                    "passwordsCaptured": false,
                    "bookmarksFile": "bookmarks.json",
                    "extensionIds": ["ext1", "ext2"],
                    "passwords": []
                }
            ],
            "wifiProfiles": [],
            "shortcuts": [],
            "startupEntries": [],
            "wingetExportCaptured": false,
            "captureResults": [],
            "manualActions": []
        }
        """;

        var package = JsonSerializer.Deserialize<MigrationPackage>(v10Json, Options);

        Assert.NotNull(package);
        Assert.Equal("1.0", package.Version);
        Assert.Null(package.ThunderbirdProfiles);
        Assert.Null(package.AppSettings);
        Assert.Null(package.SourceMachine.OsBuildNumber);
        Assert.Single(package.BrowserProfiles);
        Assert.Null(package.BrowserProfiles[0].Extensions);
        Assert.Equal(2, package.BrowserProfiles[0].ExtensionIds.Count);
    }

    [Fact]
    public void CategoryProgressInfo_SerializeDeserialize()
    {
        var info = new CategoryProgressInfo
        {
            Category = "Printers",
            Status = CategoryStatus.PartialSuccess
        };

        var json = JsonSerializer.Serialize(info, Options);
        var deserialized = JsonSerializer.Deserialize<CategoryProgressInfo>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("Printers", deserialized.Category);
        Assert.Equal(CategoryStatus.PartialSuccess, deserialized.Status);
    }

    [Fact]
    public void CapturedBrowserProfile_WithExtensions_SerializeDeserialize()
    {
        var profile = new CapturedBrowserProfile
        {
            BrowserName = "Edge",
            ProfileName = "Default",
            ExtensionIds = new List<string> { "abc123", "def456" },
            Extensions = new List<CapturedExtensionInfo>
            {
                new() { Id = "abc123", Name = "uBlock Origin", Version = "1.55.0" },
                new() { Id = "def456", Name = "Bitwarden" }
            }
        };

        var json = JsonSerializer.Serialize(profile, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedBrowserProfile>(json, Options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Extensions);
        Assert.Equal(2, deserialized.Extensions.Count);
        Assert.Equal("uBlock Origin", deserialized.Extensions[0].Name);
        Assert.Equal("1.55.0", deserialized.Extensions[0].Version);
    }

    [Fact]
    public void CapturedAppSettings_DynamicApps_SerializeDeserialize()
    {
        var settings = new CapturedAppSettings
        {
            CapturedApps =
            {
                new CapturedAppConfig
                {
                    AppName = "Thunderbird",
                    SourceType = "AppData",
                    SourcePath = @"C:\Users\user\AppData\Roaming\Thunderbird",
                    PackageFolder = "Thunderbird",
                    SizeBytes = 1024000,
                    FileCount = 15,
                    MatchedToInventory = true
                },
                new CapturedAppConfig
                {
                    AppName = "PuTTY",
                    SourceType = "Registry",
                    SourcePath = @"HKCU\Software\SimonTatham\PuTTY",
                    PackageFolder = "reg_PuTTY",
                    SizeBytes = 2048,
                    FileCount = 1,
                    MatchedToInventory = true
                }
            }
        };

        var json = JsonSerializer.Serialize(settings, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedAppSettings>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.CapturedApps.Count);
        Assert.Equal("Thunderbird", deserialized.CapturedApps[0].AppName);
        Assert.Equal("Registry", deserialized.CapturedApps[1].SourceType);
        Assert.True(deserialized.CapturedApps[0].MatchedToInventory);
    }

    [Fact]
    public void BackwardCompat_V11Package_OldAppSettings_DeserializesGracefully()
    {
        // Simulate a v1.1 package with the old hardcoded AppSettings format
        var v11Json = """
        {
            "version": "1.1",
            "sourceMachine": {
                "computerName": "PC2",
                "userName": "user",
                "domain": "CORP"
            },
            "software": [],
            "printers": [],
            "scanners": [],
            "mappedDrives": [],
            "browserProfiles": [],
            "wifiProfiles": [],
            "shortcuts": [],
            "startupEntries": [],
            "appSettings": {
                "puttyCaptured": true,
                "notepadPlusPlusCaptured": false,
                "obsStudioCaptured": false,
                "vsCodeCaptured": true,
                "vsCodeExtensions": ["ms-python.python"],
                "gitConfigCaptured": true
            },
            "captureResults": [],
            "manualActions": []
        }
        """;

        var package = JsonSerializer.Deserialize<MigrationPackage>(v11Json, Options);

        Assert.NotNull(package);
        // Old format deserializes into new model — unknown properties are ignored
        // AppSettings will be non-null but CapturedApps will be empty (old fields ignored)
        Assert.NotNull(package.AppSettings);
        Assert.Empty(package.AppSettings.CapturedApps);
    }

    [Fact]
    public void CapturedStartupEntry_WithIsEnabled_SerializeDeserialize()
    {
        var entry = new CapturedStartupEntry
        {
            Name = "Spotify",
            Command = @"""C:\Users\user\AppData\Roaming\Spotify\Spotify.exe"" /minimized",
            Source = "Registry-HKCU",
            IsPerUser = true,
            IsEnabled = false
        };

        var json = JsonSerializer.Serialize(entry, Options);
        var deserialized = JsonSerializer.Deserialize<CapturedStartupEntry>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("Spotify", deserialized.Name);
        Assert.Equal("Registry-HKCU", deserialized.Source);
        Assert.False(deserialized.IsEnabled);
    }

    [Fact]
    public void CapturedStartupEntry_IsEnabledNull_BackwardCompat()
    {
        // Simulate a package without the IsEnabled field (older format)
        var json = """
        {
            "name": "OldApp",
            "command": "C:\\oldapp.exe",
            "source": "Registry-HKCU",
            "isPerUser": true
        }
        """;

        var deserialized = JsonSerializer.Deserialize<CapturedStartupEntry>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal("OldApp", deserialized.Name);
        Assert.Null(deserialized.IsEnabled); // Not present in old format
    }

    [Fact]
    public void FullMigrationPackage_AllCategories_SerializeDeserialize()
    {
        var package = new MigrationPackage
        {
            SourceMachine = new MachineInfo
            {
                ComputerName = "WS001",
                UserName = "jsmith",
                Domain = "CORP",
                OsVersion = "Windows 11 Enterprise",
                OsBuild = "22631.2861"
            },
            Software = { new CapturedSoftware { DisplayName = "Chrome", DisplayVersion = "120.0" } },
            Printers = { new CapturedPrinter { Name = "HP Printer", IsDefault = true } },
            Scanners = { new CapturedScanner { DeviceName = "Scanner1" } },
            MappedDrives = { new CapturedDrive { DriveLetter = "Z:", UncPath = @"\\server\share" } },
            BrowserProfiles = { new CapturedBrowserProfile { BrowserName = "Chrome", ProfileName = "Default" } },
            WifiProfiles = { new CapturedWifiProfile { Ssid = "Office" } },
            Shortcuts = { new CapturedShortcut { Name = "Notepad", IsDesktopShortcut = true } },
            OutlookConfig = new CapturedOutlookConfig { DefaultProfile = "Outlook", OutlookVersion = "16.0" },
            OfficeRecentFiles = { new CapturedOfficeRecent { ApplicationName = "Word", FileName = "doc.docx" } }
        };

        var json = JsonSerializer.Serialize(package, Options);
        var deserialized = JsonSerializer.Deserialize<MigrationPackage>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Software);
        Assert.Single(deserialized.Printers);
        Assert.Single(deserialized.Scanners);
        Assert.Single(deserialized.MappedDrives);
        Assert.Single(deserialized.BrowserProfiles);
        Assert.Single(deserialized.WifiProfiles);
        Assert.Single(deserialized.Shortcuts);
        Assert.NotNull(deserialized.OutlookConfig);
        Assert.Single(deserialized.OfficeRecentFiles);
    }
}
