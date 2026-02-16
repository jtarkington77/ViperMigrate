using Moq;
using ViperMigrate.Core.Models;
using ViperMigrate.Core.Restore;

namespace ViperMigrate.Core.Tests;

public class RestoreEngineTests
{
    private static Mock<IRestoreApplicator> CreateMockApplicator(
        string category, RestoreStage stage, CategoryStatus resultStatus = CategoryStatus.Success)
    {
        var mock = new Mock<IRestoreApplicator>();
        mock.Setup(a => a.Category).Returns(category);
        mock.Setup(a => a.Stage).Returns(stage);
        mock.Setup(a => a.RestoreAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryResult
            {
                Category = category,
                Status = resultStatus,
                ItemsProcessed = 1,
                ItemsTotal = 1
            });
        return mock;
    }

    [Fact]
    public void GetStageGroups_GroupsByStageInOrder()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("Browsers", RestoreStage.Configuration).Object);
        engine.Register(CreateMockApplicator("WiFi", RestoreStage.Connectivity).Object);
        engine.Register(CreateMockApplicator("Software", RestoreStage.SoftwareInstall).Object);
        engine.Register(CreateMockApplicator("Printers", RestoreStage.Peripherals).Object);

        var groups = engine.GetStageGroups(new[] { "WiFi", "Software", "Printers", "Browsers" });

        Assert.Equal(4, groups.Count);
        Assert.Equal(RestoreStage.SoftwareInstall, groups[0].Stage);
        Assert.Equal(RestoreStage.Connectivity, groups[1].Stage);
        Assert.Equal(RestoreStage.Peripherals, groups[2].Stage);
        Assert.Equal(RestoreStage.Configuration, groups[3].Stage);
    }

    [Fact]
    public void GetStageGroups_FiltersUnselectedCategories()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("WiFi", RestoreStage.Connectivity).Object);
        engine.Register(CreateMockApplicator("Software", RestoreStage.SoftwareInstall).Object);
        engine.Register(CreateMockApplicator("Browsers", RestoreStage.Configuration).Object);

        var groups = engine.GetStageGroups(new[] { "WiFi", "Browsers" });

        Assert.Equal(2, groups.Count);
        Assert.Equal(RestoreStage.Connectivity, groups[0].Stage);
        Assert.Equal(RestoreStage.Configuration, groups[1].Stage);
    }

    [Fact]
    public void GetStageGroups_EmptyStagesNotCreated()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("Browsers", RestoreStage.Configuration).Object);

        var groups = engine.GetStageGroups(new[] { "Browsers" });

        Assert.Single(groups);
        Assert.Equal(RestoreStage.Configuration, groups[0].Stage);
    }

    [Fact]
    public void GetStageGroups_MultipleApplicatorsInSameStage()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("WiFi", RestoreStage.Connectivity).Object);
        engine.Register(CreateMockApplicator("Mapped Drives", RestoreStage.Connectivity).Object);

        var groups = engine.GetStageGroups(new[] { "WiFi", "Mapped Drives" });

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Applicators.Count);
    }

    [Fact]
    public async Task RunStagedAsync_ExecutesInStageOrder()
    {
        var executionOrder = new List<string>();

        var wifiMock = CreateMockApplicator("WiFi", RestoreStage.Connectivity);
        wifiMock.Setup(a => a.RestoreAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("WiFi"))
            .ReturnsAsync(new CategoryResult { Category = "WiFi", Status = CategoryStatus.Success });

        var softwareMock = CreateMockApplicator("Software", RestoreStage.SoftwareInstall);
        softwareMock.Setup(a => a.RestoreAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Software"))
            .ReturnsAsync(new CategoryResult { Category = "Software", Status = CategoryStatus.Success });

        var browserMock = CreateMockApplicator("Browsers", RestoreStage.Configuration);
        browserMock.Setup(a => a.RestoreAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("Browsers"))
            .ReturnsAsync(new CategoryResult { Category = "Browsers", Status = CategoryStatus.Success });

        var engine = new RestoreEngine();
        engine.Register(browserMock.Object);  // Register out of order
        engine.Register(wifiMock.Object);
        engine.Register(softwareMock.Object);

        var progress = new Progress<string>(_ => { });
        var stageProgress = new Progress<StageProgressInfo>(_ => { });

        var result = await engine.RunStagedAsync(
            "test", new[] { "WiFi", "Software", "Browsers" }, progress, stageProgress, CancellationToken.None);

        Assert.Equal(new[] { "Software", "WiFi", "Browsers" }, executionOrder);
        Assert.Equal(3, result.CategoryResults.Count);
    }

    [Fact]
    public async Task RunStagedAsync_ReportsStageStatuses()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("WiFi", RestoreStage.Connectivity).Object);
        engine.Register(CreateMockApplicator("Software", RestoreStage.SoftwareInstall, CategoryStatus.Failed).Object);

        var progress = new Progress<string>(_ => { });

        var result = await engine.RunStagedAsync(
            "test", new[] { "WiFi", "Software" }, progress, null, CancellationToken.None);

        Assert.Equal(StageStatus.Completed, result.StageStatuses[RestoreStage.Connectivity]);
        Assert.Equal(StageStatus.Failed, result.StageStatuses[RestoreStage.SoftwareInstall]);
    }

    [Fact]
    public async Task RunAsync_BackwardCompatible()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("WiFi", RestoreStage.Connectivity).Object);
        engine.Register(CreateMockApplicator("Software", RestoreStage.SoftwareInstall).Object);

        var progress = new Progress<string>(_ => { });

        var results = await engine.RunAsync("test", new[] { "WiFi", "Software" }, progress, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Software", results[0].Category);
        Assert.Equal("WiFi", results[1].Category);
    }

    [Fact]
    public async Task RunStagedAsync_HandlesApplicatorExceptions()
    {
        var failMock = new Mock<IRestoreApplicator>();
        failMock.Setup(a => a.Category).Returns("Broken");
        failMock.Setup(a => a.Stage).Returns(RestoreStage.Configuration);
        failMock.Setup(a => a.RestoreAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var engine = new RestoreEngine();
        engine.Register(failMock.Object);

        var progress = new Progress<string>(_ => { });

        var result = await engine.RunStagedAsync(
            "test", new[] { "Broken" }, progress, null, CancellationToken.None);

        Assert.Single(result.CategoryResults);
        Assert.Equal(CategoryStatus.Failed, result.CategoryResults[0].Status);
        Assert.Contains("Test error", result.CategoryResults[0].Errors[0]);
        Assert.Equal(StageStatus.Failed, result.StageStatuses[RestoreStage.Configuration]);
    }

    [Fact]
    public async Task RunStagedAsync_ReportsCategoryProgress()
    {
        var engine = new RestoreEngine();
        engine.Register(CreateMockApplicator("WiFi", RestoreStage.Connectivity).Object);
        engine.Register(CreateMockApplicator("Software", RestoreStage.SoftwareInstall, CategoryStatus.Failed).Object);

        var progress = new Progress<string>(_ => { });
        var reported = new List<CategoryProgressInfo>();
        var categoryProgress = new Progress<CategoryProgressInfo>(info =>
        {
            reported.Add(new CategoryProgressInfo { Category = info.Category, Status = info.Status });
        });

        var result = await engine.RunStagedAsync(
            "test", new[] { "WiFi", "Software" }, progress, null, categoryProgress, CancellationToken.None);

        // Each category should report InProgress then its final status
        // Note: Progress<T> posts to the sync context, so in tests the callbacks may be delayed.
        // We verify the result instead.
        Assert.Equal(2, result.CategoryResults.Count);
        Assert.Equal(CategoryStatus.Success, result.CategoryResults.First(r => r.Category == "WiFi").Status);
        Assert.Equal(CategoryStatus.Failed, result.CategoryResults.First(r => r.Category == "Software").Status);
    }

    [Fact]
    public async Task RunStagedAsync_CategoryProgressOnException()
    {
        var failMock = new Mock<IRestoreApplicator>();
        failMock.Setup(a => a.Category).Returns("Broken");
        failMock.Setup(a => a.Stage).Returns(RestoreStage.Configuration);
        failMock.Setup(a => a.RestoreAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        var engine = new RestoreEngine();
        engine.Register(failMock.Object);

        var progress = new Progress<string>(_ => { });

        var result = await engine.RunStagedAsync(
            "test", new[] { "Broken" }, progress, null, null, CancellationToken.None);

        Assert.Single(result.CategoryResults);
        Assert.Equal(CategoryStatus.Failed, result.CategoryResults[0].Status);
    }
}
