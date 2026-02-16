using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Common;

public static class ChromiumPasswordDecryptor
{
    /// <summary>
    /// Decrypts saved passwords from a Chromium-based browser profile.
    /// </summary>
    public static List<CapturedBrowserPassword> ExtractPasswords(string userDataPath, string profileName, out string? error)
    {
        error = null;
        var passwords = new List<CapturedBrowserPassword>();

        try
        {
            // 1. Get the AES master key from Local State
            var localStatePath = Path.Combine(userDataPath, "Local State");
            if (!File.Exists(localStatePath))
            {
                error = "Local State file not found.";
                return passwords;
            }

            var masterKey = GetMasterKey(localStatePath);
            if (masterKey == null)
            {
                error = "Could not extract master encryption key.";
                return passwords;
            }

            // 2. Copy Login Data to temp (it's locked by the browser)
            var loginDataPath = Path.Combine(userDataPath, profileName, "Login Data");
            if (!File.Exists(loginDataPath))
                return passwords; // No saved passwords

            var tempDb = Path.Combine(Path.GetTempPath(), $"viper_logins_{Guid.NewGuid()}.db");
            try
            {
                File.Copy(loginDataPath, tempDb, true);

                // Copy WAL and SHM files if they exist — required when browser is running
                // (SQLite WAL mode keeps uncommitted data in separate files)
                var walFile = loginDataPath + "-wal";
                var shmFile = loginDataPath + "-shm";
                if (File.Exists(walFile))
                    try { File.Copy(walFile, tempDb + "-wal", true); } catch { }
                if (File.Exists(shmFile))
                    try { File.Copy(shmFile, tempDb + "-shm", true); } catch { }

                // 3. Read the SQLite database
                using var conn = new SQLiteConnection($"Data Source={tempDb};Version=3;Read Only=True;");
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT origin_url, username_value, password_value FROM logins WHERE password_value IS NOT NULL AND length(password_value) > 0";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var url = reader.GetString(0);
                    var username = reader.GetString(1);
                    var encryptedPassword = (byte[])reader[2];

                    if (encryptedPassword.Length == 0)
                        continue;

                    var decrypted = DecryptPassword(encryptedPassword, masterKey);
                    if (decrypted != null)
                    {
                        passwords.Add(new CapturedBrowserPassword
                        {
                            Url = url,
                            Username = username,
                            Password = decrypted
                        });
                    }
                }
            }
            finally
            {
                try { File.Delete(tempDb); } catch { }
                try { File.Delete(tempDb + "-wal"); } catch { }
                try { File.Delete(tempDb + "-shm"); } catch { }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return passwords;
    }

    private static byte[]? GetMasterKey(string localStatePath)
    {
        try
        {
            var json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt))
                return null;
            if (!osCrypt.TryGetProperty("encrypted_key", out var encKeyProp))
                return null;

            var encKeyB64 = encKeyProp.GetString();
            if (string.IsNullOrEmpty(encKeyB64))
                return null;

            var encKey = Convert.FromBase64String(encKeyB64);

            // Strip the "DPAPI" prefix (5 bytes)
            if (encKey.Length < 5 || Encoding.ASCII.GetString(encKey, 0, 5) != "DPAPI")
                return null;

            var dpapiBlob = new byte[encKey.Length - 5];
            Array.Copy(encKey, 5, dpapiBlob, 0, dpapiBlob.Length);

            // Decrypt with DPAPI (current user scope)
            return ProtectedData.Unprotect(dpapiBlob, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null;
        }
    }

    private static string? DecryptPassword(byte[] encryptedPassword, byte[] masterKey)
    {
        try
        {
            // Chrome 80+ format: "v10" or "v11" prefix + 12-byte nonce + ciphertext + 16-byte auth tag
            if (encryptedPassword.Length > 3 &&
                encryptedPassword[0] == 'v' && encryptedPassword[1] == '1' &&
                (encryptedPassword[2] == '0' || encryptedPassword[2] == '1'))
            {
                // AES-GCM decryption
                var nonce = new byte[12];
                Array.Copy(encryptedPassword, 3, nonce, 0, 12);

                // Ciphertext is everything after prefix+nonce, minus the 16-byte tag at the end
                var ciphertextWithTag = new byte[encryptedPassword.Length - 15]; // 3 prefix + 12 nonce
                Array.Copy(encryptedPassword, 15, ciphertextWithTag, 0, ciphertextWithTag.Length);

                var tagSize = 16;
                var ciphertext = new byte[ciphertextWithTag.Length - tagSize];
                var tag = new byte[tagSize];
                Array.Copy(ciphertextWithTag, 0, ciphertext, 0, ciphertext.Length);
                Array.Copy(ciphertextWithTag, ciphertext.Length, tag, 0, tagSize);

                var plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(masterKey, tagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);

                return Encoding.UTF8.GetString(plaintext);
            }
            else
            {
                // Legacy DPAPI-only encryption (pre Chrome 80)
                var decrypted = ProtectedData.Unprotect(encryptedPassword, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
        }
        catch
        {
            return null;
        }
    }
}
