using Microsoft.Win32;
using ViperMigrate.Core.Common;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Capture.Collectors;

public class DriveCollector : ICaptureCollector
{
    private readonly MigrationPackage _package;

    public DriveCollector(MigrationPackage package) => _package = package;

    public string Category => "Mapped Drives";

    public async Task<CategoryResult> CaptureAsync(string packagePath, IProgress<string> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new CategoryResult { Category = Category };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Registry: persistent mapped drives
        foreach (var letter in RegistryHelper.GetSubKeyNames(Registry.CurrentUser, "Network"))
        {
            ct.ThrowIfCancellationRequested();
            var keyPath = $@"Network\{letter}";
            var remotePath = RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "RemotePath");
            if (string.IsNullOrWhiteSpace(remotePath))
                continue;

            seen.Add(letter.ToUpperInvariant());
            _package.MappedDrives.Add(new CapturedDrive
            {
                DriveLetter = letter.ToUpperInvariant() + ":",
                UncPath = remotePath,
                UserName = RegistryHelper.ReadString(Registry.CurrentUser, keyPath, "UserName") ?? "",
                Persistent = true
            });
        }

        // net use: catch non-persistent mappings
        try
        {
            var (exitCode, stdout, _) = await ProcessRunner.RunAsync("net", "use", ct);
            if (exitCode == 0)
            {
                foreach (var line in stdout.Split('\n'))
                {
                    var trimmed = line.Trim();
                    // Format: "Status  Local  Remote  Network"
                    // e.g.   "OK      Z:     \\server\share  Microsoft Windows Network"
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3)
                        continue;

                    // Find a drive letter pattern (X:)
                    string? driveLetter = null;
                    string? uncPath = null;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Length == 2 && char.IsLetter(parts[i][0]) && parts[i][1] == ':')
                            driveLetter = parts[i];
                        else if (parts[i].StartsWith(@"\\"))
                            uncPath = parts[i];
                    }

                    if (driveLetter == null || uncPath == null)
                        continue;

                    var letter = driveLetter[0].ToString().ToUpperInvariant();
                    if (!seen.Add(letter))
                        continue;

                    _package.MappedDrives.Add(new CapturedDrive
                    {
                        DriveLetter = driveLetter.ToUpperInvariant(),
                        UncPath = uncPath,
                        Persistent = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Failed to enumerate net use drives: {ex.Message}");
        }

        result.ItemsTotal = _package.MappedDrives.Count;
        result.ItemsProcessed = _package.MappedDrives.Count;
        result.Status = _package.MappedDrives.Count > 0 ? CategoryStatus.Success : CategoryStatus.Success;
        if (_package.MappedDrives.Count == 0)
            progress.Report("No mapped drives found.");
        else
            progress.Report($"Found {_package.MappedDrives.Count} mapped drive(s).");

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
