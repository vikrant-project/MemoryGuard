using MemoryGuard.Core.Models;

namespace MemoryGuard.Core.Interfaces;

public interface IMemoryMonitor : IDisposable
{
    event EventHandler<SystemMemoryMetrics>? MetricsUpdated;
    event EventHandler<IReadOnlyList<ProcessMetrics>>? ProcessesUpdated;

    SystemMemoryMetrics CurrentSystemMetrics { get; }
    IReadOnlyList<ProcessMetrics> CurrentProcesses { get; }
    IReadOnlyList<MemoryHistoryPoint> HistoryPoints { get; }

    bool IsMonitoring { get; }
    TimeSpan Interval { get; set; }

    void Start();
    void Stop();
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IProcessClassifier
{
    (IReadOnlyList<ProcessSafetyCategory> Categories, RiskLevel Risk, int RiskScore, string Reason) Classify(
        string processName,
        string executablePath,
        int sessionId,
        bool isForeground,
        TimeSpan idleTime,
        long workingSetBytes,
        bool isSignatureVerified,
        UserProtectionLevel userProtection
    );
}

public interface ISafetyEngine
{
    SafetyVerificationResult VerifyProcessClosure(ProcessMetrics process);
    SafetyVerificationResult VerifyProcessTermination(ProcessMetrics process);
    bool IsImmutableSystemProcess(string processName, string? executablePath);
    bool IsSecurityProcess(string processName, string? executablePath);
    bool ValidatePathSafety(string path, string approvedRoot);
}

public interface ILeakDetector
{
    void RecordSample(int processId, string processName, long workingSetBytes, long privateBytes);
    LeakDetectionResult EvaluateProcess(int processId);
    void RemoveProcess(int processId);
    IReadOnlyList<LeakDetectionResult> GetAllPotentialLeaks();
}

public interface ICleanupEngine
{
    Task<IReadOnlyList<CleanupItem>> ScanLocationsAsync(CancellationToken cancellationToken = default);
    Task<CleanupResult> CleanItemAsync(CleanupItem item, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanupResult>> CleanMultipleAsync(IEnumerable<CleanupItem> items, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public interface IStartupManager
{
    Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default);
    Task<bool> SetStartupItemStateAsync(StartupItem item, bool enable, bool stopRunningProcess = true, CancellationToken cancellationToken = default);
    bool IsProcessRunningForStartupItem(StartupItem item);
    Task<bool> StopRunningStartupProcessAsync(StartupItem item);
}

public interface ISettingsService
{
    MemoryGuardSettings Current { get; }
    event EventHandler<MemoryGuardSettings>? SettingsChanged;
    Task LoadAsync();
    Task SaveAsync();
    void ResetToDefaults();
    UserProtectionLevel GetProcessProtection(string processName, string? executablePath);
    void SetProcessProtection(string processName, string? executablePath, UserProtectionLevel level);
}

public interface ILoggerService
{
    event Action<string>? LogEntryAdded;
    event Action? LogsCleared;
    void LogInfo(string message, string? category = null);
    void LogWarning(string message, Exception? ex = null, string? category = null);
    void LogError(string message, Exception? ex = null, string? category = null);
    IReadOnlyList<string> GetRecentLogs(int maxCount = 200);
    void ClearLogs();
}

public interface IGlobalShortcutService : IDisposable
{
    event EventHandler? OpenDashboardRequested;
    event EventHandler? QuickOptimizeRequested;

    bool RegisterShortcuts(IntPtr windowHandle);
    void UnregisterShortcuts(IntPtr windowHandle);
    void HandleHotKeyMessage(int hotKeyId);
}
