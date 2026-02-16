using System.Text.Json;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Common;

public class PackageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MigrationPackage ReadManifest(string packagePath)
    {
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Manifest file not found.", manifestPath);

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<MigrationPackage>(json, JsonOptions)
               ?? throw new InvalidDataException("Failed to deserialize manifest.");
    }

    public MigrationPackage ReadEncryptedManifest(string packagePath, string password)
    {
        var encryptedPath = Path.Combine(packagePath, "manifest.enc");
        if (!File.Exists(encryptedPath))
            throw new FileNotFoundException("Encrypted manifest file not found.", encryptedPath);

        var tempPath = Path.Combine(Path.GetTempPath(), $"viper_manifest_{Guid.NewGuid()}.json");
        try
        {
            PackageEncryption.DecryptFile(encryptedPath, tempPath, password);
            var json = File.ReadAllText(tempPath);
            return JsonSerializer.Deserialize<MigrationPackage>(json, JsonOptions)
                   ?? throw new InvalidDataException("Failed to deserialize manifest.");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public bool IsEncryptedPackage(string packagePath)
    {
        return File.Exists(Path.Combine(packagePath, "manifest.enc"));
    }

    public bool IsValidPackage(string packagePath)
    {
        return Directory.Exists(packagePath)
               && (File.Exists(Path.Combine(packagePath, "manifest.json"))
                   || File.Exists(Path.Combine(packagePath, "manifest.enc")));
    }
}
