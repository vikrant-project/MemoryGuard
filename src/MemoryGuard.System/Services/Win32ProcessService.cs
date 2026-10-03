using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MemoryGuard.Core.Models;
using MemoryGuard.Platform.Native;

namespace MemoryGuard.Platform.Services;

public sealed class Win32ProcessService
{
    private readonly Win32SecurityService _securityService;
    private readonly Dictionary<int, (long LastCpuTime, DateTime LastSampleTime)> _cpuHistory = new();
    private readonly object _cpuLock = new();

    public Win32ProcessService(Win32SecurityService securityService)
    {
        _securityService = securityService;
    }

    public int GetForegroundProcessId()
    {
        try
        {
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out int pid);
                return pid;
            }
        }
        catch
        {
            // Ignore
        }
        return -1;
    }

    public TimeSpan GetSystemIdleTime()
    {
        try
        {
            var lii = NativeMethods.LASTINPUTINFO.Create();
            if (NativeMethods.GetLastInputInfo(ref lii))
            {
                uint tickCount = (uint)Environment.TickCount;
                uint idleTicks = tickCount >= lii.dwTime ? tickCount - lii.dwTime : 0;
                return TimeSpan.FromMilliseconds(idleTicks);
            }
        }
        catch
        {
            // Ignore
        }
        return TimeSpan.Zero;
    }

    public List<ProcessRawData> EnumerateProcesses()
    {
        var result = new List<ProcessRawData>();
        var processes = Process.GetProcesses();
        int foregroundPid = GetForegroundProcessId();
        var systemIdle = GetSystemIdleTime();
        var now = DateTime.UtcNow;

        foreach (var p in processes)
        {
            int pid = p.Id;
            string processName = p.ProcessName;

            // Handle system process with PID 0
            if (pid == 0)
            {
                result.Add(new ProcessRawData
                {
                    ProcessId = 0,
                    ProcessName = "System Idle Process",
                    ExecutablePath = string.Empty,
                    Publisher = "Microsoft Corporation",
                    IsSignatureVerified = true,
                    WorkingSetBytes = 0,
                    PrivateBytes = 0,
                    VirtualMemoryBytes = 0,
                    CpuPercent = 0,
                    StartTime = DateTime.MinValue,
                    SessionId = 0,
                    IsForeground = false,
                    IdleTime = TimeSpan.Zero
                });
                p.Dispose();
                continue;
            }

            try
            {
                // Working set & memory counters
                long workingSet = 0;
                long privateBytes = 0;
                long virtualMemory = 0;

                try
                {
                    workingSet = p.WorkingSet64;
                    privateBytes = p.PrivateMemorySize64;
                    virtualMemory = p.VirtualMemorySize64;
                }
                catch
                {
                    // If process already exited or access denied
                }

                // Query process path and CPU via limited handle
                string exePath = string.Empty;
                double cpuPercent = 0.0;
                DateTime startTime = DateTime.MinValue;
                int sessionId = 0;

                try
                {
                    sessionId = p.SessionId;
                    startTime = p.StartTime;
                }
                catch
                {
                    // Access denied or process exited
                }

                IntPtr hProcess = NativeMethods.OpenProcess(
                    NativeMethods.ProcessAccessFlags.QueryLimitedInformation,
                    false,
                    pid
                );

                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                        {
                            exePath = sb.ToString();
                        }

                        // Extended memory counters if available
                        var memCounters = NativeMethods.PROCESS_MEMORY_COUNTERS_EX.Create();
                        if (NativeMethods.GetProcessMemoryInfo(hProcess, out memCounters, memCounters.cb))
                        {
                            if (memCounters.WorkingSetSize > 0)
                            {
                                workingSet = (long)memCounters.WorkingSetSize;
                            }
                            if (memCounters.PrivateUsage > 0)
                            {
                                privateBytes = (long)memCounters.PrivateUsage;
                            }
                        }

                        // Calculate CPU usage
                        if (NativeMethods.GetProcessTimes(hProcess, out _, out _, out long kernelTime, out long userTime))
                        {
                            long totalCpuTime = kernelTime + userTime;
                            lock (_cpuLock)
                            {
                                if (_cpuHistory.TryGetValue(pid, out var prev))
                                {
                                    double timeDeltaMs = (now - prev.LastSampleTime).TotalMilliseconds;
                                    if (timeDeltaMs > 200)
                                    {
                                        // 100ns units -> ms: divide by 10,000
                                        double cpuDeltaMs = (totalCpuTime - prev.LastCpuTime) / 10000.0;
                                        cpuPercent = Math.Clamp((cpuDeltaMs / (timeDeltaMs * Environment.ProcessorCount)) * 100.0, 0.0, 100.0);
                                    }
                                }
                                _cpuHistory[pid] = (totalCpuTime, now);
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }

                // If path couldn't be obtained via QueryFullProcessImageName, fallback to MainModule
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    try
                    {
                        exePath = p.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                bool isForeground = (pid == foregroundPid);
                TimeSpan idleTime = isForeground ? TimeSpan.Zero : systemIdle;

                result.Add(new ProcessRawData
                {
                    ProcessId = pid,
                    ProcessName = processName,
                    ExecutablePath = exePath,
                    Publisher = string.Empty, // Evaluated on-demand or cached
                    IsSignatureVerified = false,
                    WorkingSetBytes = workingSet,
                    PrivateBytes = privateBytes,
                    VirtualMemoryBytes = virtualMemory,
                    CpuPercent = Math.Round(cpuPercent, 1),
                    StartTime = startTime,
                    SessionId = sessionId,
                    IsForeground = isForeground,
                    IdleTime = idleTime
                });
            }
            catch
            {
                // Never let a single inaccessible process crash the enumerator
            }
            finally
            {
                p.Dispose();
            }
        }

        // Clean up dead PIDs from CPU history
        lock (_cpuLock)
        {
            var activePids = new HashSet<int>(result.Select(r => r.ProcessId));
            var deadPids = _cpuHistory.Keys.Where(k => !activePids.Contains(k)).ToList();
            foreach (var dead in deadPids)
            {
                _cpuHistory.Remove(dead);
            }
        }

        return result;
    }

    public async Task<bool> CloseProcessGracefullyAsync(int processId, TimeSpan timeout)
    {
        Process? proc = null;
        try
        {
            proc = Process.GetProcessById(processId);
        }
        catch
        {
            return true; // Already exited
        }

        using (proc)
        {
            // 1. Try CloseMainWindow()
            try
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    NativeMethods.SendMessageTimeout(
                        proc.MainWindowHandle,
                        NativeMethods.WM_CLOSE,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        NativeMethods.SMTO_ABORTIFHUNG,
                        (uint)timeout.TotalMilliseconds,
                        out _
                    );
                }
            }
            catch
            {
                // Fallback to CloseMainWindow
                try
                {
                    proc.CloseMainWindow();
                }
                catch
                {
                    // Ignore
                }
            }

            // Wait for exit
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (proc.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }

                await Task.Delay(200).ConfigureAwait(false);
            }

            try
            {
                return proc.HasExited;
            }
            catch
            {
                return true;
            }
        }
    }

    public bool TerminateProcessForce(int processId)
    {
        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessFlags.Terminate | NativeMethods.ProcessAccessFlags.Synchronize,
            false,
            processId
        );

        if (hProcess == IntPtr.Zero)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                p.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            return NativeMethods.TerminateProcess(hProcess, 1);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    public bool TrimProcessWorkingSet(int processId)
    {
        if (processId <= 4) return false;

        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessFlags.SetQuota | NativeMethods.ProcessAccessFlags.QueryInformation,
            false,
            processId
        );

        if (hProcess == IntPtr.Zero)
        {
            hProcess = NativeMethods.OpenProcess(
                NativeMethods.ProcessAccessFlags.SetQuota | NativeMethods.ProcessAccessFlags.QueryLimitedInformation,
                false,
                processId
            );
        }

        if (hProcess == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            bool success = NativeMethods.EmptyWorkingSet(hProcess);
            if (!success)
            {
                success = NativeMethods.SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));
            }
            return success;
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    public (int TrimmedCount, long ReclaimedEstimate) TrimProcesses(IEnumerable<int> processIds)
    {
        int count = 0;
        foreach (var pid in processIds)
        {
            if (TrimProcessWorkingSet(pid))
            {
                count++;
            }
        }
        return (count, 0);
    }
}

public sealed record ProcessRawData
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public bool IsSignatureVerified { get; set; }
    public long WorkingSetBytes { get; init; }
    public long PrivateBytes { get; init; }
    public long VirtualMemoryBytes { get; init; }
    public double CpuPercent { get; init; }
    public DateTime StartTime { get; init; }
    public int SessionId { get; init; }
    public bool IsForeground { get; init; }
    public TimeSpan IdleTime { get; init; }
}
