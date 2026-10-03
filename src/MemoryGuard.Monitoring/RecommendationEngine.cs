using MemoryGuard.Core.Models;

namespace MemoryGuard.Monitoring;

public sealed class RecommendationEngine
{
    public IReadOnlyList<OptimizationRecommendation> GenerateRecommendations(
        SystemMemoryMetrics systemMetrics,
        IReadOnlyList<ProcessMetrics> processes,
        int minMemoryMb = 50,
        int idleTimeoutMinutes = 5)
    {
        var recommendations = new List<OptimizationRecommendation>();
        long minMemoryBytes = (long)minMemoryMb * 1024 * 1024;

        foreach (var proc in processes)
        {
            // Never recommend terminating or closing non-closeable processes
            if (!proc.CanCloseGracefully)
            {
                continue;
            }

            // Exclude foreground or critical processes
            if (proc.IsForeground || proc.Risk == RiskLevel.Critical)
            {
                continue;
            }

            // User protected
            if (proc.UserProtection == UserProtectionLevel.NeverClose)
            {
                continue;
            }

            // Must exceed minimum memory threshold
            if (proc.WorkingSetBytes < minMemoryBytes)
            {
                continue;
            }

            // Estimated reclaimable memory:
            // Conservative 80% of working set (accounting for OS shared pages / dll mappings)
            long estimatedReclaim = (long)(proc.WorkingSetBytes * 0.80);

            bool isIdle = proc.IdleTime.TotalMinutes >= idleTimeoutMinutes;
            bool isHighMemory = proc.WorkingSetBytes > 200L * 1024 * 1024;
            bool isSystemUnderPressure = systemMetrics.PressureLevel >= MemoryPressureLevel.Warning;

            string reason;
            bool isPreselected = false;

            if (proc.IsPossibleMemoryLeak)
            {
                reason = $"Possible memory leak detected; consuming {proc.WorkingSetBytes / (1024 * 1024):N0} MB.";
                isPreselected = proc.Risk == RiskLevel.Low;
            }
            else if (isIdle && isHighMemory)
            {
                reason = $"Idle for {Math.Round(proc.IdleTime.TotalMinutes)} min while retaining {proc.WorkingSetBytes / (1024 * 1024):N0} MB RAM.";
                isPreselected = (proc.Risk == RiskLevel.Low) && (proc.UserProtection == UserProtectionLevel.Normal);
            }
            else if (isIdle)
            {
                reason = $"Background process idle for {Math.Round(proc.IdleTime.TotalMinutes)} min.";
                isPreselected = (proc.Risk == RiskLevel.Low);
            }
            else if (isHighMemory)
            {
                reason = $"High memory background consumer ({proc.WorkingSetBytes / (1024 * 1024):N0} MB).";
                isPreselected = false; // User should confirm
            }
            else if (proc.Categories.Contains(ProcessSafetyCategory.USER_APPLICATION) && !proc.IsForeground)
            {
                reason = $"Inactive background user application ({proc.WorkingSetBytes / (1024 * 1024):N0} MB).";
                isPreselected = false;
            }
            else
            {
                continue;
            }

            recommendations.Add(new OptimizationRecommendation
            {
                ProcessId = proc.ProcessId,
                ProcessName = proc.ProcessName,
                ExecutablePath = proc.ExecutablePath,
                CurrentMemoryBytes = proc.WorkingSetBytes,
                EstimatedReclaimBytes = estimatedReclaim,
                IdleDuration = proc.IdleTime,
                Risk = proc.Risk,
                Reason = reason,
                IsPreselected = isPreselected
            });
        }

        return recommendations
            .OrderByDescending(r => r.IsPreselected)
            .ThenByDescending(r => r.EstimatedReclaimBytes)
            .ToList();
    }
}
