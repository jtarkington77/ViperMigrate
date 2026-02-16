using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Tests;

public class HtmlReportGeneratorTests
{
    [Fact]
    public void Generate_ContainsDoctype()
    {
        var results = new List<CategoryResult>();
        var actions = new List<ManualAction>();

        var html = HtmlReportGenerator.Generate(results, actions);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("</html>", html);
        Assert.Contains("<title>ViperMigrate Report</title>", html);
    }

    [Fact]
    public void Generate_ContainsCategoryResults()
    {
        var results = new List<CategoryResult>
        {
            new()
            {
                Category = "Software",
                Status = CategoryStatus.Success,
                ItemsProcessed = 5,
                ItemsTotal = 5
            },
            new()
            {
                Category = "Printers",
                Status = CategoryStatus.PartialSuccess,
                ItemsProcessed = 2,
                ItemsTotal = 3,
                Warnings = { "Driver not found for HP 400" }
            }
        };

        var html = HtmlReportGenerator.Generate(results, new List<ManualAction>());

        Assert.Contains("Software", html);
        Assert.Contains("Printers", html);
        Assert.Contains("5/5 processed", html);
        Assert.Contains("2/3 processed", html);
        Assert.Contains("Driver not found for HP 400", html);
    }

    [Fact]
    public void Generate_ContainsManualActionCheckboxes()
    {
        var results = new List<CategoryResult>
        {
            new()
            {
                Category = "Software",
                Status = CategoryStatus.PartialSuccess,
                ManualActions =
                {
                    new ManualAction
                    {
                        Category = "Software",
                        Title = "Install Adobe Reader",
                        Description = "Download from adobe.com",
                        Command = "winget install --id Adobe.Acrobat.Reader.DC -e",
                        Priority = ManualActionPriority.High
                    }
                }
            }
        };

        var html = HtmlReportGenerator.Generate(results, new List<ManualAction>());

        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("Install Adobe Reader", html);
        Assert.Contains("winget install", html);
        Assert.Contains("localStorage", html);
        Assert.Contains("Copy", html);
    }

    [Fact]
    public void Generate_HandlesEmptyResults()
    {
        var html = HtmlReportGenerator.Generate(new List<CategoryResult>(), new List<ManualAction>());

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("ViperMigrate Report", html);
        // Should not throw and should still have status cards
        Assert.Contains("class=\"card success\"", html);
    }

    [Fact]
    public void Generate_IncludesSourceMachineInfo()
    {
        var info = new MachineInfo
        {
            ComputerName = "TESTPC",
            UserName = "admin",
            Domain = "CORP",
            OsVersion = "Windows 11"
        };

        var html = HtmlReportGenerator.Generate(new List<CategoryResult>(), new List<ManualAction>(), info);

        Assert.Contains("TESTPC", html);
        Assert.Contains("CORP\\admin", html);
        Assert.Contains("Windows 11", html);
    }

    [Fact]
    public void Generate_ContainsCopyAllWingetButton()
    {
        var actions = new List<ManualAction>
        {
            new()
            {
                Category = "Software",
                Title = "Install App",
                Command = "winget install --id Test.App -e",
                Priority = ManualActionPriority.High
            }
        };

        var results = new List<CategoryResult>
        {
            new()
            {
                Category = "Software",
                Status = CategoryStatus.PartialSuccess,
                ManualActions = actions
            }
        };

        var html = HtmlReportGenerator.Generate(results, actions);

        Assert.Contains("Copy All Winget Commands", html);
        Assert.Contains("copyAllWinget", html);
    }

    [Fact]
    public void Generate_ContainsCompleteButton()
    {
        var html = HtmlReportGenerator.Generate(new List<CategoryResult>(), new List<ManualAction>());

        Assert.Contains(">Complete</button>", html);
        Assert.Contains("generateTechReport()", html);
        Assert.Contains("id=\"techOverlay\"", html);
        Assert.Contains("tech-overlay", html);
        Assert.Contains("id=\"techContent\"", html);
    }
}
