using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Restore;

public class RestoreEngine
{
    private readonly List<IRestoreApplicator> _applicators = new();

    public IReadOnlyList<IRestoreApplicator> Applicators => _applicators.AsReadOnly();

    public void Register(IRestoreApplicator applicator)
    {
        _applicators.Add(applicator);
    }

    public List<StageGroup> GetStageGroups(IEnumerable<string> selectedCategories)
    {
        var selected = new HashSet<string>(selectedCategories, StringComparer.OrdinalIgnoreCase);

        return _applicators
            .Where(a => selected.Contains(a.Category))
            .GroupBy(a => a.Stage)
            .OrderBy(g => (int)g.Key)
            .Select(g => new StageGroup
            {
                Stage = g.Key,
                Applicators = g.ToList()
            })
            .ToList();
    }

    public async Task<StageRestoreResult> RunStagedAsync(
        string packagePath,
        IEnumerable<string> selectedCategories,
        IProgress<string> progress,
        IProgress<StageProgressInfo>? stageProgress,
        IProgress<CategoryProgressInfo>? categoryProgress,
        CancellationToken ct)
    {
        var result = new StageRestoreResult();
        var stageGroups = GetStageGroups(selectedCategories);

        foreach (var group in stageGroups)
        {
            ct.ThrowIfCancellationRequested();

            // Report stage start
            stageProgress?.Report(new StageProgressInfo
            {
                Stage = group.Stage,
                Status = StageStatus.InProgress,
                Message = $"Starting {group.StageName}..."
            });

            progress.Report($"Stage {(int)group.Stage}: {group.StageName}");

            var stageHasFailures = false;
            var stageHasSuccesses = false;

            foreach (var applicator in group.Applicators)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report($"Restoring {applicator.Category}...");

                // Report category started
                categoryProgress?.Report(new CategoryProgressInfo
                {
                    Category = applicator.Category,
                    Status = CategoryStatus.InProgress
                });

                try
                {
                    var categoryResult = await Task.Run(
                        () => applicator.RestoreAsync(packagePath, progress, ct), ct);
                    result.CategoryResults.Add(categoryResult);

                    // Report category finished with actual status
                    categoryProgress?.Report(new CategoryProgressInfo
                    {
                        Category = applicator.Category,
                        Status = categoryResult.Status
                    });

                    if (categoryResult.Status is CategoryStatus.Success or CategoryStatus.PartialSuccess)
                        stageHasSuccesses = true;
                    if (categoryResult.Status is CategoryStatus.Failed)
                        stageHasFailures = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stageHasFailures = true;
                    result.CategoryResults.Add(new CategoryResult
                    {
                        Category = applicator.Category,
                        Status = CategoryStatus.Failed,
                        Errors = { ex.Message }
                    });

                    categoryProgress?.Report(new CategoryProgressInfo
                    {
                        Category = applicator.Category,
                        Status = CategoryStatus.Failed
                    });
                }
            }

            // Determine stage status
            var stageStatus = (stageHasSuccesses, stageHasFailures) switch
            {
                (true, false) => StageStatus.Completed,
                (true, true) => StageStatus.PartiallyCompleted,
                (false, true) => StageStatus.Failed,
                _ => StageStatus.Completed
            };

            result.StageStatuses[group.Stage] = stageStatus;

            stageProgress?.Report(new StageProgressInfo
            {
                Stage = group.Stage,
                Status = stageStatus,
                Message = $"{group.StageName} {stageStatus.ToString().ToLowerInvariant()}"
            });
        }

        return result;
    }

    /// <summary>
    /// Backward-compatible overload without categoryProgress.
    /// </summary>
    public Task<StageRestoreResult> RunStagedAsync(
        string packagePath,
        IEnumerable<string> selectedCategories,
        IProgress<string> progress,
        IProgress<StageProgressInfo>? stageProgress,
        CancellationToken ct)
    {
        return RunStagedAsync(packagePath, selectedCategories, progress, stageProgress, null, ct);
    }

    /// <summary>
    /// Backward-compatible wrapper around RunStagedAsync.
    /// </summary>
    public async Task<List<CategoryResult>> RunAsync(
        string packagePath,
        IEnumerable<string> selectedCategories,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var stagedResult = await RunStagedAsync(packagePath, selectedCategories, progress, null, null, ct);
        return stagedResult.CategoryResults;
    }
}
