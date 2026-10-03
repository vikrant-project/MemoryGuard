using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.Platform.Services;

namespace MemoryGuard.Monitoring;

public sealed class MemoryMonitor : IMemoryMonitor
{
    private readonly Win32MemoryService _memoryService;
    private readonly Win32ProcessService _processService;
    private readonly Win32SecurityService _securityService;
    private readonly IProcessClassifier _classifier;
    private readonly ISafetyEngine _safetyEngine;
    private readonly ISettingsService _settingsService;
    private readonly ILeakDetector _leakDetector;
    private readonly MemoryHistoryTracker _historyTracker = new();

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private TimeSpan _interval = TimeSpan.FromSeconds(2);
    private bool _isMonitoring;

    public event EventHandler<SystemMemoryMetrics>? MetricsUpdated;
    public event EventHandler<IReadOnlyList<ProcessMetrics>>? ProcessesUpdated;

    public SystemMemoryMetrics CurrentSystemMetrics { get; private set; } = new();
    public IReadOnlyList<ProcessMetrics> CurrentProcesses { get; private set; } = Array.Empty<ProcessMetrics>();
    public IReadOnlyList<MemoryHistoryPoint> HistoryPoints => _historyTracker.GetPoints();

    public bool IsMonitoring
    {
        get
        {
            lock (_lock) return _isMonitoring;
        }
    }

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            if (value < TimeSpan.FromMilliseconds(500))
                value = TimeSpan.FromMilliseconds(500);
            _interval = value;
        }
    }

    public MemoryMonitor(
        Win32MemoryService memoryService,
        Win32ProcessService processService,
        Win32SecurityService securityService,
        IProcessClassifier classifier,
        ISafetyEngine safetyEngine,
        ISettingsService settingsService,
        ILeakDetector leakDetector)
    {
        _memoryService = memoryService;
        _processService = processService;
        _securityService = securityService;
        _classifier = classifier;
        _safetyEngine = safetyEngine;
        _settingsService = settingsService;
        _leakDetector = leakDetector;

        if (_settingsService.Current.MonitoringIntervalMs > 0)
        {
            _interval = TimeSpan.FromMilliseconds(_settingsService.Current.MonitoringIntervalMs);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _cts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await SampleMetricsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        // Initial sample
        try
        {
            await SampleMetricsAsync(token).ConfigureAwait(false);
        }
        catch
        {
            // Ignore
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, token).ConfigureAwait(false);
                await SampleMetricsAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Resilient: never crash the loop on unexpected sampling failure
            }
        }
    }

    private async Task SampleMetricsAsync(CancellationToken token)
    {
        var settings = _settingsService.Current;

        // 1. System Memory Metrics
        var sysMetrics = _memoryService.GetSystemMemoryMetrics(
            settings.MemoryWarningThresholdPercent,
            settings.MemoryHighThresholdPercent,
            settings.MemoryCriticalThresholdPercent
        );

        CurrentSystemMetrics = sysMetrics;
        _historyTracker.AddPoint(sysMetrics);
        MetricsUpdated?.Invoke(this, sysMetrics);

        if (token.IsCancellationRequested) return;

        // 2. Process Enumeration
        var rawProcesses = await Task.Run(() => _processService.EnumerateProcesses(), token).ConfigureAwait(false);
        var enrichedProcesses = new List<ProcessMetrics>(rawProcesses.Count);

        foreach (var raw in rawProcesses)
        {
            if (token.IsCancellationRequested) return;

            var protection = _settingsService.GetProcessProtection(raw.ProcessName, raw.ExecutablePath);

            var (categories, risk, riskScore, reason) = _classifier.Classify(
                raw.ProcessName,
                raw.ExecutablePath,
                raw.SessionId,
                raw.IsForeground,
                raw.IdleTime,
                raw.WorkingSetBytes,
                raw.IsSignatureVerified,
                protection
            );

            // Record sample for leak detection
            _leakDetector.RecordSample(raw.ProcessId, raw.ProcessName, raw.WorkingSetBytes, raw.PrivateBytes);
            var leakResult = _leakDetector.EvaluateProcess(raw.ProcessId);

            if (leakResult.IsPossibleLeak)
            {
                var mutableCats = categories.ToList();
                if (!mutableCats.Contains(ProcessSafetyCategory.POSSIBLE_MEMORY_LEAK))
                {
                    mutableCats.Add(ProcessSafetyCategory.POSSIBLE_MEMORY_LEAK);
                }
                categories = mutableCats;
            }

            long reclaimable = (long)(raw.WorkingSetBytes * 0.80);

            var tempMetric = new ProcessMetrics
            {
                ProcessId = raw.ProcessId,
                ProcessName = raw.ProcessName,
                ExecutablePath = raw.ExecutablePath,
                Publisher = raw.Publisher,
                IsSignatureVerified = raw.IsSignatureVerified,
                WorkingSetBytes = raw.WorkingSetBytes,
                PrivateBytes = raw.PrivateBytes,
                VirtualMemoryBytes = raw.VirtualMemoryBytes,
                CpuPercent = raw.CpuPercent,
                StartTime = raw.StartTime,
                SessionId = raw.SessionId,
                IsForeground = raw.IsForeground,
                IdleTime = raw.IdleTime,
                LastActiveTime = DateTime.UtcNow - raw.IdleTime,
                Categories = categories,
                Risk = risk,
                RiskScore = riskScore,
                RecommendedAction = reason,
                EstimatedReclaimableBytes = reclaimable,
                IsPossibleMemoryLeak = leakResult.IsPossibleLeak,
                UserProtection = protection
            };

            var closureSafety = _safetyEngine.VerifyProcessClosure(tempMetric);
            var termSafety = _safetyEngine.VerifyProcessTermination(tempMetric);

            var finalMetric = tempMetric with
            {
                CanCloseGracefully = closureSafety.IsSafeToClose,
                CanTerminateSafely = termSafety.IsSafeToTerminate,
                SafetyReason = closureSafety.IsSafeToClose ? "Safe to close gracefully" : closureSafety.Reason
            };

            enrichedProcesses.Add(finalMetric);
        }

        CurrentProcesses = enrichedProcesses.OrderByDescending(p => p.WorkingSetBytes).ToList();
        ProcessesUpdated?.Invoke(this, CurrentProcesses);
    }

    public void Dispose()
    {
        Stop();
    }
}
