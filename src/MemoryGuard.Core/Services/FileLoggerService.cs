using MemoryGuard.Core.Interfaces;

namespace MemoryGuard.Core.Services;

public sealed class FileLoggerService : ILoggerService
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private readonly Queue<string> _inMemoryLogs = new();
    private const int MaxInMemoryLogs = 300;
    private const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    public event Action<string>? LogEntryAdded;
    public event Action? LogsCleared;

    public FileLoggerService(string? customDir = null)
    {
        _logDirectory = !string.IsNullOrWhiteSpace(customDir)
            ? customDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryGuard", "logs");

        _logFilePath = Path.Combine(_logDirectory, $"memoryguard_{DateTime.UtcNow:yyyyMMdd}.log");

        try
        {
            Directory.CreateDirectory(_logDirectory);
            CleanOldLogs();
        }
        catch
        {
            // Ignore
        }
    }

    public void LogInfo(string message, string? category = null) => WriteLog("INFO", message, null, category);
    public void LogWarning(string message, Exception? ex = null, string? category = null) => WriteLog("WARN", message, ex, category);
    public void LogError(string message, Exception? ex = null, string? category = null) => WriteLog("ERROR", message, ex, category);

    public IReadOnlyList<string> GetRecentLogs(int maxCount = 200)
    {
        lock (_lock)
        {
            return _inMemoryLogs.TakeLast(maxCount).ToList();
        }
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            _inMemoryLogs.Clear();
        }
        LogsCleared?.Invoke();
    }

    private void WriteLog(string level, string message, Exception? ex, string? category)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string catStr = string.IsNullOrWhiteSpace(category) ? "General" : category;
        string logLine = $"[{timestamp}] [{level}] [{catStr}] {message}";

        if (ex != null)
        {
            logLine += Environment.NewLine + $"    Exception: {ex.GetType().Name}: {ex.Message}";
        }

        lock (_lock)
        {
            _inMemoryLogs.Enqueue(logLine);
            while (_inMemoryLogs.Count > MaxInMemoryLogs)
            {
                _inMemoryLogs.Dequeue();
            }

            try
            {
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch
            {
                // Resilient logging: never throw on logging failure
            }
        }

        try
        {
            LogEntryAdded?.Invoke(logLine);
        }
        catch
        {
            // Ignore subscriber exceptions
        }
    }

    public string LogDirectory => _logDirectory;
    public string LogFilePath => _logFilePath;

    private void RotateIfNeeded()
    {
        try
        {
            if (File.Exists(_logFilePath))
            {
                var fi = new FileInfo(_logFilePath);
                if (fi.Length > MaxLogFileSizeBytes)
                {
                    string rotated = Path.Combine(_logDirectory, $"memoryguard_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                    File.Move(_logFilePath, rotated, overwrite: true);
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var files = Directory.GetFiles(_logDirectory, "memoryguard_*.log");
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                if (fi.CreationTimeUtc < cutoff)
                {
                    try { fi.Delete(); } catch { }
                }
            }
        }
        catch
        {
            // Ignore
        }
    }
}
