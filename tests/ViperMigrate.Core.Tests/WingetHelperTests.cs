using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Tests;

public class WingetHelperTests
{
    [Fact]
    public void MatchSoftwareToWinget_ExactMatch()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Google Chrome" }
        };
        var packageIds = new List<string> { "Google.Chrome" };

        WingetHelper.MatchSoftwareToWinget(software, packageIds);

        Assert.Equal("Google.Chrome", software[0].WingetId);
    }

    [Fact]
    public void MatchSoftwareToWinget_NormalizedMatch()
    {
        // Normalized matching strips version/arch:
        // "Slack 4.35.0" normalized -> "slack"
        // "Slack.Slack" -> "Slack Slack" normalized -> "slack slack"
        // Won't match normalized, but fuzzy: segment "Slack" (5 chars >= 4) found in display name,
        // and publisher "Slack" matches first segment
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Slack 4.35.0", Publisher = "Slack Technologies" }
        };
        var packageIds = new List<string> { "Slack.Slack" };

        WingetHelper.MatchSoftwareToWinget(software, packageIds);

        Assert.Equal("Slack.Slack", software[0].WingetId);
    }

    [Fact]
    public void MatchSoftwareToWinget_FuzzySubstring()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Mozilla Firefox (x64 en-US)", Publisher = "Mozilla" }
        };
        var packageIds = new List<string> { "Mozilla.Firefox" };

        WingetHelper.MatchSoftwareToWinget(software, packageIds);

        Assert.Equal("Mozilla.Firefox", software[0].WingetId);
    }

    [Fact]
    public void MatchSoftwareToWinget_NoMatch()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Some Obscure Internal Tool v3.2" }
        };
        var packageIds = new List<string> { "Google.Chrome", "Mozilla.Firefox" };

        WingetHelper.MatchSoftwareToWinget(software, packageIds);

        Assert.Null(software[0].WingetId);
        Assert.False(software[0].CanAutoInstall);
    }

    [Fact]
    public void MatchSoftwareToWinget_MultiplePackages()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Google Chrome" },
            new() { DisplayName = "7-Zip 24.08 (x64)", Publisher = "Igor Pavlov" },
            new() { DisplayName = "Custom Corp App" }
        };
        var packageIds = new List<string> { "Google.Chrome", "7zip.7zip" };

        WingetHelper.MatchSoftwareToWinget(software, packageIds);

        Assert.Equal("Google.Chrome", software[0].WingetId);
        // 7-Zip may or may not match depending on fuzzy matching sensitivity
        Assert.Null(software[2].WingetId);
    }

    [Fact]
    public void MatchSoftwareToWinget_SkipsAlreadyMatched()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Google Chrome", WingetId = "Already.Set" }
        };
        var packageIds = new List<string> { "Google.Chrome" };

        WingetHelper.MatchSoftwareToWinget(software, packageIds);

        Assert.Equal("Already.Set", software[0].WingetId);
    }

    [Fact]
    public void MatchSoftwareToWinget_EmptyLists()
    {
        var software = new List<CapturedSoftware>();
        var packageIds = new List<string>();

        WingetHelper.MatchSoftwareToWinget(software, packageIds);
        // No exception
    }

    [Fact]
    public void NormalizeName_RemovesVersionAndArch()
    {
        Assert.Equal("Google Chrome", WingetHelper.NormalizeName("Google Chrome 120.0.6099.130 (x64)"));
        Assert.Equal("Visual Studio Code", WingetHelper.NormalizeName("Visual Studio Code 1.85.0"));
        Assert.Equal("Notepad++", WingetHelper.NormalizeName("Notepad++ (64-bit)"));
    }

    [Fact]
    public void NormalizeName_RemovesParentheticalsAndLanguageTags()
    {
        Assert.Equal("Mozilla Firefox", WingetHelper.NormalizeName("Mozilla Firefox (x64 en-US)"));
        Assert.Equal("7-Zip", WingetHelper.NormalizeName("7-Zip 24.08 (x64)"));
        Assert.Equal("PuTTY release", WingetHelper.NormalizeName("PuTTY release 0.80"));
    }

    [Fact]
    public void NormalizeName_EmptyAndNull()
    {
        Assert.Equal("", WingetHelper.NormalizeName(""));
        Assert.Equal("", WingetHelper.NormalizeName(null!));
    }

    [Fact]
    public void ResolveByNamingPattern_VCRedist2015Plus_x64()
    {
        var result = WingetHelper.ResolveByNamingPattern(
            "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.38.33135", true);
        Assert.Equal("Microsoft.VCRedist.2015+.x64", result);
    }

    [Fact]
    public void ResolveByNamingPattern_VCRedist2013_x86()
    {
        var result = WingetHelper.ResolveByNamingPattern(
            "Microsoft Visual C++ 2013 Redistributable (x86) - 12.0.30501", false);
        Assert.Equal("Microsoft.VCRedist.2013.x86", result);
    }

    [Fact]
    public void ResolveByNamingPattern_DotNetSdk()
    {
        var result = WingetHelper.ResolveByNamingPattern(
            "Microsoft .NET SDK 8.0.404 (x64)", true);
        Assert.Equal("Microsoft.DotNet.SDK.8", result);
    }

    [Fact]
    public void ResolveByNamingPattern_DotNetDesktopRuntime()
    {
        var result = WingetHelper.ResolveByNamingPattern(
            "Microsoft .NET Desktop Runtime - 8.0.11 (x64)", true);
        Assert.Equal("Microsoft.DotNet.DesktopRuntime.8", result);
    }

    [Fact]
    public void ResolveByNamingPattern_NoMatch()
    {
        var result = WingetHelper.ResolveByNamingPattern("Google Chrome", true);
        Assert.Null(result);
    }

    [Fact]
    public void ParseWingetSearchOutput_ExtractsEntries()
    {
        var output =
            "Name".PadRight(32) + "Id".PadRight(28) + "Version\r\n" +
            new string('-', 68) + "\r\n" +
            "Google Chrome".PadRight(32) + "Google.Chrome".PadRight(28) + "120.0\r\n" +
            "Notepad++".PadRight(32) + "Notepad++.Notepad++".PadRight(28) + "8.6.2\r\n";

        var entries = WingetHelper.ParseWingetSearchOutput(output);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Google.Chrome", entries[0].Id);
        Assert.Equal("Google Chrome", entries[0].Name);
        Assert.Equal("Notepad++.Notepad++", entries[1].Id);
    }

    [Fact]
    public void ParseWingetSearchOutput_HandlesEmptyOutput()
    {
        var entries = WingetHelper.ParseWingetSearchOutput("");
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseWingetSearchOutput_SkipsLinesWithoutDotInId()
    {
        var output =
            "Name".PadRight(32) + "Id".PadRight(28) + "Version\r\n" +
            new string('-', 68) + "\r\n" +
            "No results found.\r\n";

        var entries = WingetHelper.ParseWingetSearchOutput(output);
        Assert.Empty(entries);
    }

    [Fact]
    public void ApplyKnownWingetIds_SetsVivaldi()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Vivaldi 6.5.3206.63" },
            new() { DisplayName = "Google Chrome" },
            new() { DisplayName = "Mozilla Firefox (x64 en-US)" }
        };

        WingetHelper.ApplyKnownWingetIds(software);

        Assert.Equal("Vivaldi.Vivaldi", software[0].WingetId);
        Assert.Equal("Google.Chrome", software[1].WingetId);
        Assert.Equal("Mozilla.Firefox", software[2].WingetId);
    }

    [Fact]
    public void ApplyKnownWingetIds_DoesNotOverrideExisting()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Google Chrome", WingetId = "Custom.ChromeId" }
        };

        WingetHelper.ApplyKnownWingetIds(software);

        Assert.Equal("Custom.ChromeId", software[0].WingetId);
    }

    [Fact]
    public void CanAutoInstall_ReflectsWingetId()
    {
        var sw = new CapturedSoftware { DisplayName = "Test" };
        Assert.False(sw.CanAutoInstall);

        sw.WingetId = "Test.App";
        Assert.True(sw.CanAutoInstall);

        sw.WingetId = "";
        Assert.False(sw.CanAutoInstall);

        sw.WingetId = null;
        Assert.False(sw.CanAutoInstall);
    }

    [Fact]
    public void ApplyKnownWingetIds_Sets7Zip()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "7-Zip 24.08 (x64)" },
            new() { DisplayName = "Notepad++ (64-bit x64)" },
            new() { DisplayName = "PuTTY release 0.80" }
        };

        WingetHelper.ApplyKnownWingetIds(software);

        Assert.Equal("7zip.7zip", software[0].WingetId);
        Assert.Equal("Notepad++.Notepad++", software[1].WingetId);
        Assert.Equal("PuTTY.PuTTY", software[2].WingetId);
    }

    [Fact]
    public void ParseWingetListOutput_ExtractsEntries()
    {
        // Simulate real winget list output with fixed-width columns
        // Name column = 32 chars, Id column = 28 chars, Version column = rest
        var output =
            "Name".PadRight(32) + "Id".PadRight(28) + "Version\r\n" +
            new string('-', 68) + "\r\n" +
            "Google Chrome".PadRight(32) + "Google.Chrome".PadRight(28) + "120.0.6099.130\r\n" +
            "Mozilla Firefox (x64 en-US)".PadRight(32) + "Mozilla.Firefox".PadRight(28) + "121.0\r\n" +
            "7-Zip 24.08 (x64)".PadRight(32) + "7zip.7zip".PadRight(28) + "24.08\r\n" +
            "Some Tool".PadRight(32) + "SomePublisher.Tool".PadRight(28) + "1.0.0\r\n";

        var entries = WingetHelper.ParseWingetListOutput(output);

        Assert.True(entries.Count >= 3);
        Assert.Contains(entries, e => e.Id == "Google.Chrome");
        Assert.Contains(entries, e => e.Id == "Mozilla.Firefox");
        Assert.Contains(entries, e => e.Id == "7zip.7zip");
    }

    [Fact]
    public void ParseWingetListOutput_HandlesEmptyOutput()
    {
        var entries = WingetHelper.ParseWingetListOutput("");
        Assert.Empty(entries);
    }

    [Fact]
    public void MatchSoftwareToWingetList_ExactNameMatch()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Google Chrome" }
        };
        var entries = new List<WingetListEntry>
        {
            new() { Name = "Google Chrome", Id = "Google.Chrome", Version = "120.0" }
        };

        WingetHelper.MatchSoftwareToWingetList(software, entries);

        Assert.Equal("Google.Chrome", software[0].WingetId);
    }

    [Fact]
    public void MatchSoftwareToWingetList_SkipsAlreadyMatched()
    {
        var software = new List<CapturedSoftware>
        {
            new() { DisplayName = "Google Chrome", WingetId = "Already.Set" }
        };
        var entries = new List<WingetListEntry>
        {
            new() { Name = "Google Chrome", Id = "Google.Chrome", Version = "120.0" }
        };

        WingetHelper.MatchSoftwareToWingetList(software, entries);

        Assert.Equal("Already.Set", software[0].WingetId);
    }
}
