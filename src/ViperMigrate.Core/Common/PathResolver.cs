namespace ViperMigrate.Core.Common;

public static class PathResolver
{
    public static string AppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Desktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string ChromeUserDataPath =>
        Path.Combine(LocalAppData, "Google", "Chrome", "User Data");

    public static string EdgeUserDataPath =>
        Path.Combine(LocalAppData, "Microsoft", "Edge", "User Data");

    public static string VivaldiUserDataPath =>
        Path.Combine(LocalAppData, "Vivaldi", "User Data");

    public static string BraveUserDataPath =>
        Path.Combine(LocalAppData, "BraveSoftware", "Brave-Browser", "User Data");

    public static string OperaUserDataPath =>
        Path.Combine(AppData, "Opera Software", "Opera Stable");

    public static string FirefoxProfilesPath =>
        Path.Combine(AppData, "Mozilla", "Firefox", "Profiles");

    /// <summary>
    /// All known Chromium-based browsers with their display name and User Data path.
    /// </summary>
    public static readonly (string Name, string UserDataPath, string ExtensionStoreUrl)[] ChromiumBrowsers =
    {
        ("Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data"),
            "https://chrome.google.com/webstore/detail/"),
        ("Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data"),
            "https://microsoftedge.microsoft.com/addons/detail/"),
        ("Vivaldi", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vivaldi", "User Data"),
            "https://chrome.google.com/webstore/detail/"),
        ("Brave", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "User Data"),
            "https://chrome.google.com/webstore/detail/"),
        ("Opera", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opera Software", "Opera Stable"),
            "https://chrome.google.com/webstore/detail/"),
    };

    public static string OutlookProfilesRegistryPath =>
        @"Software\Microsoft\Office\16.0\Outlook\Profiles";

    public static string OutlookSignaturesPath =>
        Path.Combine(AppData, "Microsoft", "Signatures");

    public static string PublicDesktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public static string TaskbarPinsPath =>
        Path.Combine(AppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static string QuickLaunchPath =>
        Path.Combine(AppData, @"Microsoft\Internet Explorer\Quick Launch");

    public static string ImplicitAppShortcutsPath =>
        Path.Combine(AppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts");

    public static string UserStartMenuPrograms =>
        Path.Combine(AppData, @"Microsoft\Windows\Start Menu\Programs");

    public static string CommonStartMenuPrograms =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

    public static string UserStartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static string CommonStartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

    public static string OutlookProfiles15RegistryPath =>
        @"Software\Microsoft\Office\15.0\Outlook\Profiles";

    public static string OfficeMruRegistryBase(string version, string app) =>
        $@"Software\Microsoft\Office\{version}\{app}\File MRU";

    public static string ResolveEnvironmentPath(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }

    public static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }
}
