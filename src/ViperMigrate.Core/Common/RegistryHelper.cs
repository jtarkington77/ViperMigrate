using Microsoft.Win32;

namespace ViperMigrate.Core.Common;

public static class RegistryHelper
{
    public static string? ReadString(RegistryKey baseKey, string subKeyPath, string valueName)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public static int? ReadInt(RegistryKey baseKey, string subKeyPath, string valueName)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            var value = key?.GetValue(valueName);
            return value is int i ? i : null;
        }
        catch
        {
            return null;
        }
    }

    public static object? ReadValue(RegistryKey baseKey, string subKeyPath, string valueName)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key?.GetValue(valueName);
        }
        catch
        {
            return null;
        }
    }

    public static string[] GetSubKeyNames(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string[] GetValueNames(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool SetString(RegistryKey baseKey, string subKeyPath, string valueName, string value)
    {
        try
        {
            using var key = baseKey.CreateSubKey(subKeyPath);
            key?.SetValue(valueName, value, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static byte[]? ReadBinary(RegistryKey baseKey, string subKeyPath, string valueName)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key?.GetValue(valueName) as byte[];
        }
        catch
        {
            return null;
        }
    }

    public static bool SetBinary(RegistryKey baseKey, string subKeyPath, string valueName, byte[] value)
    {
        try
        {
            using var key = baseKey.CreateSubKey(subKeyPath);
            key?.SetValue(valueName, value, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool SubKeyExists(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }
}
