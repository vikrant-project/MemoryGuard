using MemoryGuard.Core.Models;

namespace MemoryGuard.Monitoring;

public sealed class MemoryHistoryTracker
{
    private readonly List<MemoryHistoryPoint> _points = new();
    private readonly object _lock = new();
    private readonly TimeSpan _retentionWindow = TimeSpan.FromMinutes(60);

    public void AddPoint(SystemMemoryMetrics metrics)
    {
        var point = new MemoryHistoryPoint(
            metrics.Timestamp,
            metrics.MemoryLoadPercent,
            metrics.UsedPhysicalBytes,
            metrics.AvailablePhysicalBytes,
            metrics.CommitChargeBytes
        );

        lock (_lock)
        {
            _points.Add(point);
            var cutoff = metrics.Timestamp - _retentionWindow;
            _points.RemoveAll(p => p.Timestamp < cutoff);
        }
    }

    public IReadOnlyList<MemoryHistoryPoint> GetPoints()
    {
        lock (_lock)
        {
            return _points.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _points.Clear();
        }
    }
}
