using System.Text.Json;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Common;

public class PackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string CreatePackageFolder(string destinationPath, string machineName)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var folderName = $"ViperMigrate_{machineName}_{timestamp}";
        var packagePath = Path.Combine(destinationPath, folderName);

        Directory.CreateDirectory(packagePath);
        Directory.CreateDirectory(Path.Combine(packagePath, "Browsers"));
        Directory.CreateDirectory(Path.Combine(packagePath, "Outlook"));
        Directory.CreateDirectory(Path.Combine(packagePath, "WiFi"));
        Directory.CreateDirectory(Path.Combine(packagePath, "Shortcuts"));
        Directory.CreateDirectory(Path.Combine(packagePath, "Logs"));
        Directory.CreateDirectory(Path.Combine(packagePath, "AppSettings"));

        return packagePath;
    }

    public void WriteManifest(string packagePath, MigrationPackage package)
    {
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        var json = JsonSerializer.Serialize(package, JsonOptions);
        File.WriteAllText(manifestPath, json);
    }

    public void WriteEncryptedManifest(string packagePath, MigrationPackage package, string password)
    {
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        var encryptedPath = Path.Combine(packagePath, "manifest.enc");

        var json = JsonSerializer.Serialize(package, JsonOptions);
        File.WriteAllText(manifestPath, json);

        PackageEncryption.EncryptFile(manifestPath, encryptedPath, password);
        File.Delete(manifestPath);
    }
}
