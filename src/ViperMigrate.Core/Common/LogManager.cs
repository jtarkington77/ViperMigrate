namespace ViperMigrate.Core.Common;

public class LogManager : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;

    public LogManager(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var logFile = Path.Combine(logDirectory, $"viper_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _writer = new StreamWriter(logFile, append: true) { AutoFlush = true };
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Error(string message, Exception ex) => Write("ERROR", $"{message}: {ex}");

    private void Write(string level, string message)
    {
        if (_disposed) return;
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
    }
}
