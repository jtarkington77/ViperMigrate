namespace ViperMigrate.Core.Common;

public static class OsHelper
{
    public static int GetCurrentBuildNumber()
    {
        return Environment.OSVersion.Version.Build;
    }

    public static bool IsWindows11(int buildNumber)
    {
        return buildNumber >= 22000;
    }

    public static int ParseBuildNumber(string osBuild)
    {
        if (string.IsNullOrWhiteSpace(osBuild))
            return 0;

        // Handle "22631.2861" format — take the part before the dot
        var dotIndex = osBuild.IndexOf('.');
        var major = dotIndex >= 0 ? osBuild[..dotIndex] : osBuild;

        return int.TryParse(major, out var result) ? result : 0;
    }
}
