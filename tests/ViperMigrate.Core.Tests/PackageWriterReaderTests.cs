using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Tests;

public class PackageWriterReaderTests : IDisposable
{
    private readonly string _tempDir;

    public PackageWriterReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"viper_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void CreatePackageFolder_CreatesExpectedStructure()
    {
        var writer = new PackageWriter();
        var packagePath = writer.CreatePackageFolder(_tempDir, "TESTPC");

        Assert.True(Directory.Exists(packagePath));
        Assert.True(Directory.Exists(Path.Combine(packagePath, "Browsers")));
        Assert.True(Directory.Exists(Path.Combine(packagePath, "Outlook")));
        Assert.True(Directory.Exists(Path.Combine(packagePath, "WiFi")));
        Assert.True(Directory.Exists(Path.Combine(packagePath, "Shortcuts")));
        Assert.True(Directory.Exists(Path.Combine(packagePath, "Logs")));
        Assert.Contains("ViperMigrate_TESTPC_", Path.GetFileName(packagePath));
    }

    [Fact]
    public void WriteManifest_ReadManifest_RoundTrip()
    {
        var writer = new PackageWriter();
        var reader = new PackageReader();
        var packagePath = writer.CreatePackageFolder(_tempDir, "TESTPC");

        var package = CreateSamplePackage();
        writer.WriteManifest(packagePath, package);

        var loaded = reader.ReadManifest(packagePath);

        Assert.Equal(package.Version, loaded.Version);
        Assert.Equal(package.SourceMachine.ComputerName, loaded.SourceMachine.ComputerName);
        Assert.Equal(package.Software.Count, loaded.Software.Count);
        Assert.Equal(package.Software[0].DisplayName, loaded.Software[0].DisplayName);
        Assert.Equal(package.Printers.Count, loaded.Printers.Count);
        Assert.Equal(package.MappedDrives.Count, loaded.MappedDrives.Count);
    }

    [Fact]
    public void WriteEncryptedManifest_ReadEncryptedManifest_RoundTrip()
    {
        var writer = new PackageWriter();
        var reader = new PackageReader();
        var packagePath = writer.CreatePackageFolder(_tempDir, "TESTPC");
        var password = "EncryptionTest!";

        var package = CreateSamplePackage();
        writer.WriteEncryptedManifest(packagePath, package, password);

        Assert.True(reader.IsEncryptedPackage(packagePath));
        Assert.False(File.Exists(Path.Combine(packagePath, "manifest.json")));

        var loaded = reader.ReadEncryptedManifest(packagePath, password);

        Assert.Equal(package.SourceMachine.ComputerName, loaded.SourceMachine.ComputerName);
        Assert.Equal(package.Software.Count, loaded.Software.Count);
    }

    [Fact]
    public void IsValidPackage_ReturnsTrueForValidPackage()
    {
        var writer = new PackageWriter();
        var reader = new PackageReader();
        var packagePath = writer.CreatePackageFolder(_tempDir, "TESTPC");

        writer.WriteManifest(packagePath, new MigrationPackage());

        Assert.True(reader.IsValidPackage(packagePath));
    }

    [Fact]
    public void IsValidPackage_ReturnsFalseForEmptyDirectory()
    {
        var reader = new PackageReader();
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        Assert.False(reader.IsValidPackage(emptyDir));
    }

    private static MigrationPackage CreateSamplePackage()
    {
        return new MigrationPackage
        {
            Version = "1.0",
            SourceMachine = new MachineInfo
            {
                ComputerName = "TESTPC",
                UserName = "testuser",
                Domain = "TESTDOMAIN",
                OsVersion = "Windows 11",
                OsBuild = "22631",
                CaptureDate = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            },
            Software =
            {
                new CapturedSoftware
                {
                    DisplayName = "Visual Studio Code",
                    DisplayVersion = "1.85.0",
                    Publisher = "Microsoft"
                }
            },
            Printers =
            {
                new CapturedPrinter
                {
                    Name = "HP LaserJet",
                    DriverName = "HP Universal Print Driver",
                    IsNetworkPrinter = true,
                    UncPath = @"\\printserver\HPLaser"
                }
            },
            MappedDrives =
            {
                new CapturedDrive
                {
                    DriveLetter = "Z:",
                    UncPath = @"\\fileserver\shared",
                    Persistent = true
                }
            }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
