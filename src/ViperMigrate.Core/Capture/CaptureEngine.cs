using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture;

public class CaptureEngine
{
    private readonly List<ICaptureCollector> _collectors = new();

    public IReadOnlyList<ICaptureCollector> Collectors => _collectors.AsReadOnly();

    public void Register(ICaptureCollector collector)
    {
        _collectors.Add(collector);
    }

    public async Task<List<CategoryResult>> RunAsync(
        string packagePath,
        IEnumerable<string> selectedCategories,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var results = new List<CategoryResult>();
        var selected = new HashSet<string>(selectedCategories, StringComparer.OrdinalIgnoreCase);

        foreach (var collector in _collectors)
        {
            if (!selected.Contains(collector.Category))
                continue;

            ct.ThrowIfCancellationRequested();
            progress.Report($"Capturing {collector.Category}...");

            try
            {
                var result = await Task.Run(
                    () => collector.CaptureAsync(packagePath, progress, ct), ct);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new CategoryResult
                {
                    Category = collector.Category,
                    Status = CategoryStatus.Failed,
                    Errors = { ex.Message }
                });
            }
        }

        return results;
    }
}
