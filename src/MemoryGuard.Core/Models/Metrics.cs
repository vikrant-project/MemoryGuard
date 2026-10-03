namespace MemoryGuard.Core.Models;

public sealed record ProcessMetrics
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public bool IsSignatureVerified { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateBytes { get; init; }
    public long VirtualMemoryBytes { get; init; }
    public double CpuPercent { get; init; }
    public DateTime StartTime { get; init; }
    public int SessionId { get; init; }
    public bool IsForeground { get; init; }
    public TimeSpan IdleTime { get; init; }
    public DateTime LastActiveTime { get; init; }
    public IReadOnlyList<ProcessSafetyCategory> Categories { get; init; } = Array.Empty<ProcessSafetyCategory>();
    public RiskLevel Risk { get; init; } = RiskLevel.None;
    public int RiskScore { get; init; }
    public string? RecommendedAction { get; init; }
    public long EstimatedReclaimableBytes { get; init; }
    public bool IsPossibleMemoryLeak { get; init; }
    public UserProtectionLevel UserProtection { get; init; } = UserProtectionLevel.Normal;
    public bool CanCloseGracefully { get; init; }
    public bool CanTerminateSafely { get; init; }
    public string SafetyReason { get; init; } = string.Empty;
}

public sealed record SystemMemoryMetrics
{
    public long TotalPhysicalBytes { get; init; }
    public long AvailablePhysicalBytes { get; init; }
    public long UsedPhysicalBytes { get; init; }
    public double MemoryLoadPercent { get; init; }
    public long CommitChargeBytes { get; init; }
    public long CommitLimitBytes { get; init; }
    public double CommitPercent { get; init; }
    public long PageFileTotalBytes { get; init; }
    public long PageFileAvailableBytes { get; init; }
    public long PageFileUsedBytes { get; init; }
    public MemoryPressureLevel PressureLevel { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed record MemoryHistoryPoint(
    DateTime Timestamp,
    double MemoryLoadPercent,
    long UsedPhysicalBytes,
    long AvailablePhysicalBytes,
    long CommitBytes
);

public sealed record LeakDetectionResult
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public bool IsPossibleLeak { get; init; }
    public double GrowthRateBytesPerMinute { get; init; }
    public long BaselineWorkingSetBytes { get; init; }
    public long CurrentWorkingSetBytes { get; init; }
    public TimeSpan MonitoredDuration { get; init; }
    public int ConsecutiveGrowthSamples { get; init; }
    public string Explanation { get; init; } = string.Empty;
}
