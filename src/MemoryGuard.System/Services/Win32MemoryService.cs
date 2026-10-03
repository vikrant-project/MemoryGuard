using MemoryGuard.Core.Models;
using MemoryGuard.Platform.Native;

namespace MemoryGuard.Platform.Services;

public sealed class Win32MemoryService
{
    public SystemMemoryMetrics GetSystemMemoryMetrics(double warningThreshold = 80.0, double highThreshold = 90.0, double criticalThreshold = 95.0)
    {
        var memStatus = NativeMethods.MEMORYSTATUSEX.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref memStatus))
        {
            throw new InvalidOperationException("Failed to query GlobalMemoryStatusEx");
        }

        long totalPhys = (long)memStatus.ullTotalPhys;
        long availPhys = (long)memStatus.ullAvailPhys;
        long usedPhys = Math.Max(0, totalPhys - availPhys);
        double loadPercent = memStatus.dwMemoryLoad;

        long commitTotal = 0;
        long commitLimit = 0;
        long pageSize = 4096;
        long pageFileTotal = (long)memStatus.ullTotalPageFile;
        long pageFileAvail = (long)memStatus.ullAvailPageFile;

        var perfInfo = NativeMethods.PERFORMANCE_INFORMATION.Create();
        if (NativeMethods.GetPerformanceInfo(out perfInfo, perfInfo.cb))
        {
            pageSize = (long)perfInfo.PageSize;
            commitTotal = (long)perfInfo.CommitTotal * pageSize;
            commitLimit = (long)perfInfo.CommitLimit * pageSize;
        }

        double commitPercent = commitLimit > 0 ? (double)commitTotal / commitLimit * 100.0 : 0.0;
        long pageFileUsed = Math.Max(0, pageFileTotal - pageFileAvail);

        var pressure = MemoryPressureLevel.Normal;
        if (loadPercent >= criticalThreshold || commitPercent >= 95.0)
        {
            pressure = MemoryPressureLevel.Critical;
        }
        else if (loadPercent >= highThreshold || commitPercent >= 88.0)
        {
            pressure = MemoryPressureLevel.High;
        }
        else if (loadPercent >= warningThreshold || commitPercent >= 80.0)
        {
            pressure = MemoryPressureLevel.Warning;
        }

        return new SystemMemoryMetrics
        {
            TotalPhysicalBytes = totalPhys,
            AvailablePhysicalBytes = availPhys,
            UsedPhysicalBytes = usedPhys,
            MemoryLoadPercent = loadPercent,
            CommitChargeBytes = commitTotal,
            CommitLimitBytes = commitLimit,
            CommitPercent = commitPercent,
            PageFileTotalBytes = pageFileTotal,
            PageFileAvailableBytes = pageFileAvail,
            PageFileUsedBytes = pageFileUsed,
            PressureLevel = pressure,
            Timestamp = DateTime.UtcNow
        };
    }
}
