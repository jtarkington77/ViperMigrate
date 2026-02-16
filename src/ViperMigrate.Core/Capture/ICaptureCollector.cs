using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture;

public interface ICaptureCollector
{
    string Category { get; }
    Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct);
}
