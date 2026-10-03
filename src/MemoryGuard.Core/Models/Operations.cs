namespace MemoryGuard.Core.Models;

public sealed record CleanupItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string TargetPath { get; init; }
    public CleanupRiskLevel Risk { get; init; } = CleanupRiskLevel.Safe;
    public long EstimatedSizeBytes { get; init; }
    public int FileCount { get; init; }
    public bool IsSelected { get; set; } = true;
    public bool RequiresElevation { get; init; }
}

public sealed record CleanupResult
{
    public required string ItemId { get; init; }
    public required string ItemName { get; init; }
    public long ReclaimedBytes { get; init; }
    public int FilesDeleted { get; init; }
    public int FilesSkipped { get; init; }
    public IReadOnlyList<string> SkippedReasons { get; init; } = Array.Empty<string>();
    public bool Succeeded { get; init; }
}

public sealed record StartupItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public required string Location { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsRunning { get; init; }
    public string Impact { get; init; } = "Low";
    public string RecommendedAction { get; init; } = "Keep enabled";
    public bool CanDisable { get; init; } = true;
}

public sealed record SafetyVerificationResult
{
    public bool IsSafeToClose { get; init; }
    public bool IsSafeToTerminate { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> RuleViolations { get; init; } = Array.Empty<string>();

    public static SafetyVerificationResult Denied(string reason, params string[] violations) =>
        new()
        {
            IsSafeToClose = false,
            IsSafeToTerminate = false,
            Reason = reason,
            RuleViolations = violations
        };

    public static SafetyVerificationResult Allowed(bool allowForceKill, string reason) =>
        new()
        {
            IsSafeToClose = true,
            IsSafeToTerminate = allowForceKill,
            Reason = reason
        };
}

public sealed record OptimizationRecommendation
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutablePath { get; init; }
    public long CurrentMemoryBytes { get; init; }
    public long EstimatedReclaimBytes { get; init; }
    public TimeSpan IdleDuration { get; init; }
    public RiskLevel Risk { get; init; }
    public required string Reason { get; init; }
    public bool IsPreselected { get; init; }
}
