using System.Security.Cryptography;
using System.Text;

namespace ViperMigrate.Core.Common;

public static class PackageEncryption
{
    private const int KeySize = 256;
    private const int BlockSize = 128;
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int IvSize = 16;

    public static void EncryptFile(string inputPath, string outputPath, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        using var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.GetBytes(KeySize / 8);
        aes.IV = iv;

        using var outputStream = File.Create(outputPath);
        outputStream.Write(salt, 0, salt.Length);
        outputStream.Write(iv, 0, iv.Length);

        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(outputStream, encryptor, CryptoStreamMode.Write);
        using var inputStream = File.OpenRead(inputPath);
        inputStream.CopyTo(cryptoStream);
    }

    public static void DecryptFile(string inputPath, string outputPath, string password)
    {
        using var inputStream = File.OpenRead(inputPath);

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        inputStream.ReadExactly(salt, 0, salt.Length);
        inputStream.ReadExactly(iv, 0, iv.Length);

        using var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.GetBytes(KeySize / 8);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var cryptoStream = new CryptoStream(inputStream, decryptor, CryptoStreamMode.Read);
        using var outputStream = File.Create(outputPath);
        cryptoStream.CopyTo(outputStream);
    }

    public static string EncryptString(string plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        using var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.GetBytes(KeySize / 8);
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Concatenate salt + iv + cipher
        var result = new byte[salt.Length + iv.Length + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(iv, 0, result, salt.Length, iv.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, salt.Length + iv.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public static string DecryptString(string ciphertext, string password)
    {
        var allBytes = Convert.FromBase64String(ciphertext);

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        var cipherBytes = new byte[allBytes.Length - SaltSize - IvSize];

        Buffer.BlockCopy(allBytes, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(allBytes, SaltSize, iv, 0, IvSize);
        Buffer.BlockCopy(allBytes, SaltSize + IvSize, cipherBytes, 0, cipherBytes.Length);

        using var key = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key.GetBytes(KeySize / 8);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
