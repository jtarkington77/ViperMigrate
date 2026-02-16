using ViperMigrate.Core.Common;

namespace ViperMigrate.Core.Tests;

public class OsHelperTests
{
    [Theory]
    [InlineData("22631.2861", 22631)]
    [InlineData("22631", 22631)]
    [InlineData("19045.3803", 19045)]
    [InlineData("10240", 10240)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("abc", 0)]
    [InlineData("22631.2861.123", 22631)]
    public void ParseBuildNumber_VariousFormats(string? osBuild, int expected)
    {
        Assert.Equal(expected, OsHelper.ParseBuildNumber(osBuild!));
    }

    [Theory]
    [InlineData(22000, true)]
    [InlineData(22631, true)]
    [InlineData(26100, true)]
    [InlineData(21999, false)]
    [InlineData(19045, false)]
    [InlineData(0, false)]
    public void IsWindows11_BoundaryTests(int buildNumber, bool expected)
    {
        Assert.Equal(expected, OsHelper.IsWindows11(buildNumber));
    }

    [Fact]
    public void GetCurrentBuildNumber_ReturnsPositive()
    {
        var build = OsHelper.GetCurrentBuildNumber();
        Assert.True(build > 0);
    }
}
