using ViperMigrate.Core.Common;

namespace ViperMigrate.Core.Tests;

public class BrowserDetectorTests
{
    [Fact]
    public void DetectChromiumBrowsers_HandlesGracefully()
    {
        // Should not throw even on machines without any browsers
        var browsers = BrowserDetector.DetectChromiumBrowsers();
        Assert.NotNull(browsers);
        // Each detected browser should have valid properties
        foreach (var browser in browsers)
        {
            Assert.False(string.IsNullOrEmpty(browser.Name));
            Assert.False(string.IsNullOrEmpty(browser.UserDataPath));
            Assert.Equal("Chromium", browser.BrowserType);
            Assert.True(browser.ProfileNames.Count > 0);
        }
    }

    [Fact]
    public void DetectFirefoxBrowsers_HandlesGracefully()
    {
        // Should not throw even on machines without Firefox
        var browsers = BrowserDetector.DetectFirefoxBrowsers();
        Assert.NotNull(browsers);
        foreach (var browser in browsers)
        {
            Assert.False(string.IsNullOrEmpty(browser.Name));
            Assert.False(string.IsNullOrEmpty(browser.UserDataPath));
            Assert.Equal("Firefox", browser.BrowserType);
            Assert.True(browser.ProfileNames.Count > 0);
        }
    }

    [Fact]
    public void FindChromiumProfiles_NonexistentPath_ReturnsEmpty()
    {
        var profiles = BrowserDetector.FindChromiumProfiles(@"C:\NonExistent\Path\That\Does\Not\Exist");
        Assert.NotNull(profiles);
        Assert.Empty(profiles);
    }

    [Fact]
    public void FindChromiumProfiles_TempDir_FindsDefaultAndProfileN()
    {
        // Create a temp structure mimicking a Chromium User Data dir
        var tempDir = Path.Combine(Path.GetTempPath(), $"viper_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "Default"));
            Directory.CreateDirectory(Path.Combine(tempDir, "Profile 1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "Profile 2"));
            Directory.CreateDirectory(Path.Combine(tempDir, "System Profile")); // Should NOT be included
            Directory.CreateDirectory(Path.Combine(tempDir, "Guest Profile")); // Should NOT be included

            var profiles = BrowserDetector.FindChromiumProfiles(tempDir);

            Assert.Equal(3, profiles.Count);
            Assert.Contains("Default", profiles);
            Assert.Contains("Profile 1", profiles);
            Assert.Contains("Profile 2", profiles);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("Chrome", "chrome")]
    [InlineData("Edge", "msedge")]
    [InlineData("Firefox", "firefox")]
    [InlineData("Brave", "brave")]
    [InlineData("Opera", "opera")]
    [InlineData("UnknownBrowser", "unknownbrowser")]
    public void GetProcessName_ReturnsExpected(string browserName, string expected)
    {
        Assert.Equal(expected, BrowserDetector.GetProcessName(browserName));
    }
}
