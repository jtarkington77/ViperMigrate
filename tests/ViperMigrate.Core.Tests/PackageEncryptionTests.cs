using ViperMigrate.Core.Common;

namespace ViperMigrate.Core.Tests;

public class PackageEncryptionTests : IDisposable
{
    private readonly string _tempDir;

    public PackageEncryptionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"viper_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void EncryptString_DecryptString_RoundTrip()
    {
        var original = "Hello, ViperMigrate! This is a secret.";
        var password = "TestPassword123!";

        var encrypted = PackageEncryption.EncryptString(original, password);
        var decrypted = PackageEncryption.DecryptString(encrypted, password);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptString_DifferentPasswordFails()
    {
        var original = "Sensitive data";
        var encrypted = PackageEncryption.EncryptString(original, "correct-password");

        Assert.ThrowsAny<Exception>(() =>
            PackageEncryption.DecryptString(encrypted, "wrong-password"));
    }

    [Fact]
    public void EncryptString_ProducesDifferentCiphertextEachTime()
    {
        var original = "Same plaintext";
        var password = "password";

        var encrypted1 = PackageEncryption.EncryptString(original, password);
        var encrypted2 = PackageEncryption.EncryptString(original, password);

        Assert.NotEqual(encrypted1, encrypted2); // Different salt/IV each time
    }

    [Fact]
    public void EncryptFile_DecryptFile_RoundTrip()
    {
        var inputPath = Path.Combine(_tempDir, "input.txt");
        var encryptedPath = Path.Combine(_tempDir, "encrypted.bin");
        var decryptedPath = Path.Combine(_tempDir, "decrypted.txt");
        var password = "FilePassword!";
        var content = "This is file content for encryption testing.\nLine 2.\nLine 3.";

        File.WriteAllText(inputPath, content);

        PackageEncryption.EncryptFile(inputPath, encryptedPath, password);
        Assert.True(File.Exists(encryptedPath));

        PackageEncryption.DecryptFile(encryptedPath, decryptedPath, password);
        var decryptedContent = File.ReadAllText(decryptedPath);

        Assert.Equal(content, decryptedContent);
    }

    [Fact]
    public void EncryptFile_EncryptedDiffersFromOriginal()
    {
        var inputPath = Path.Combine(_tempDir, "plaintext.txt");
        var encryptedPath = Path.Combine(_tempDir, "ciphertext.bin");

        File.WriteAllText(inputPath, "Some content to encrypt");
        PackageEncryption.EncryptFile(inputPath, encryptedPath, "password");

        var original = File.ReadAllBytes(inputPath);
        var encrypted = File.ReadAllBytes(encryptedPath);

        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void EncryptString_EmptyString_RoundTrip()
    {
        var encrypted = PackageEncryption.EncryptString(string.Empty, "password");
        var decrypted = PackageEncryption.DecryptString(encrypted, "password");

        Assert.Equal(string.Empty, decrypted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
