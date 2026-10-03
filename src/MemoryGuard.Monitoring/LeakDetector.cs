using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;

namespace MemoryGuard.Monitoring;

public sealed class LeakDetector : ILeakDetector
{
    private sealed class ProcessSampleHistory
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; set; } = string.Empty;
        public List<(DateTime Timestamp, long WorkingSetBytes, long PrivateBytes)> Samples { get; } = new();
    }

    private readonly Dictionary<int, ProcessSampleHistory> _history = new();
    private readonly object _lock = new();
    private readonly TimeSpan _retentionWindow = TimeSpan.FromMinutes(60);

    // Minimum samples required before forming an opinion (e.g. at least 5 samples across at least 10 seconds)
    private const int MinSamplesRequired = 5;

    public void RecordSample(int processId, string processName, long workingSetBytes, long privateBytes)
    {
        if (processId <= 4) return;

        var now = DateTime.UtcNow;
        lock (_lock)
        {
            if (!_history.TryGetValue(processId, out var history))
            {
                history = new ProcessSampleHistory
                {
                    ProcessId = processId,
                    ProcessName = processName
                };
                _history[processId] = history;
            }

            history.ProcessName = processName;
            history.Samples.Add((now, workingSetBytes, privateBytes));

            // Prune samples older than 60 minutes
            var cutoff = now - _retentionWindow;
            history.Samples.RemoveAll(s => s.Timestamp < cutoff);
        }
    }

    public LeakDetectionResult EvaluateProcess(int processId)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(processId, out var history) || history.Samples.Count < MinSamplesRequired)
            {
                return new LeakDetectionResult
                {
                    ProcessId = processId,
                    ProcessName = history?.ProcessName ?? string.Empty,
                    IsPossibleLeak = false,
                    Explanation = "Insufficient historical samples."
                };
            }

            var samples = history.Samples;
            var first = samples.First();
            var last = samples.Last();
            var duration = last.Timestamp - first.Timestamp;

            if (duration.TotalMinutes < 0.5)
            {
                return new LeakDetectionResult
                {
                    ProcessId = processId,
                    ProcessName = history.ProcessName,
                    IsPossibleLeak = false,
                    Explanation = "Sample observation window too short."
                };
            }

            // Count consecutive private bytes or working set increases
            int consecutiveIncreases = 0;
            int maxConsecutive = 0;

            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].PrivateBytes > samples[i - 1].PrivateBytes ||
                    samples[i].WorkingSetBytes > samples[i - 1].WorkingSetBytes)
                {
                    consecutiveIncreases++;
                    if (consecutiveIncreases > maxConsecutive)
                    {
                        maxConsecutive = consecutiveIncreases;
                    }
                }
                else if (samples[i].PrivateBytes < samples[i - 1].PrivateBytes)
                {
                    consecutiveIncreases = 0;
                }
            }

            long netGrowthBytes = last.PrivateBytes > 0 
                ? (last.PrivateBytes - first.PrivateBytes)
                : (last.WorkingSetBytes - first.WorkingSetBytes);

            double growthRatePerMinute = duration.TotalMinutes > 0
                ? (netGrowthBytes / duration.TotalMinutes)
                : 0.0;

            // Flag as possible leak if:
            // 1. Net growth > 30 MB
            // 2. Growth rate > 5 MB/min
            // 3. At least 4 consecutive increases without significant drops
            bool isPossibleLeak = netGrowthBytes > 30L * 1024 * 1024 &&
                                 growthRatePerMinute > 5.0 * 1024 * 1024 &&
                                 maxConsecutive >= 4;

            string explanation = isPossibleLeak
                ? $"Possible memory leak detected: Sustained growth of {netGrowthBytes / (1024 * 1024):N0} MB over {duration.TotalMinutes:F1} min ({growthRatePerMinute / (1024 * 1024):F1} MB/min)."
                : "Memory usage stable or within normal fluctuation bounds.";

            return new LeakDetectionResult
            {
                ProcessId = processId,
                ProcessName = history.ProcessName,
                IsPossibleLeak = isPossibleLeak,
                GrowthRateBytesPerMinute = growthRatePerMinute,
                BaselineWorkingSetBytes = first.WorkingSetBytes,
                CurrentWorkingSetBytes = last.WorkingSetBytes,
                MonitoredDuration = duration,
                ConsecutiveGrowthSamples = maxConsecutive,
                Explanation = explanation
            };
        }
    }

    public void RemoveProcess(int processId)
    {
        lock (_lock)
        {
            _history.Remove(processId);
        }
    }

    public IReadOnlyList<LeakDetectionResult> GetAllPotentialLeaks()
    {
        var results = new List<LeakDetectionResult>();
        List<int> pids;
        lock (_lock)
        {
            pids = _history.Keys.ToList();
        }

        foreach (var pid in pids)
        {
            var res = EvaluateProcess(pid);
            if (res.IsPossibleLeak)
            {
                results.Add(res);
            }
        }

        return results;
    }
}
