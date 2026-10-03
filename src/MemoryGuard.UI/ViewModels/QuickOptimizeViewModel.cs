using System.Collections.ObjectModel;
using System.Windows.Input;
using MemoryGuard.Cleanup;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.Monitoring;
using MemoryGuard.Platform.Services;
using MemoryGuard.UI.Common;

namespace MemoryGuard.UI.ViewModels;

public sealed class RecommendationItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public OptimizationRecommendation Recommendation { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public RecommendationItemViewModel(OptimizationRecommendation recommendation)
    {
        Recommendation = recommendation;
        _isSelected = recommendation.IsPreselected;
    }
}

public sealed class QuickOptimizeViewModel : ViewModelBase
{
    private readonly IMemoryMonitor _monitor;
    private readonly RecommendationEngine _recommendationEngine = new();
    private readonly ICleanupEngine _cleanupEngine;
    private readonly Win32ProcessService _processService;
    private readonly ISafetyEngine _safetyEngine;
    private readonly ILoggerService _logger;

    private bool _isScanning;
    private bool _isOptimizing;
    private string _statusText = "Ready to scan system.";
    private long _totalReclaimableRamBytes;
    private long _totalReclaimableDiskBytes;
    private int _safeActionsCount;
    private string _resultSummary = string.Empty;

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public bool IsOptimizing
    {
        get => _isOptimizing;
        set => SetProperty(ref _isOptimizing, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public long TotalReclaimableRamBytes
    {
        get => _totalReclaimableRamBytes;
        set => SetProperty(ref _totalReclaimableRamBytes, value);
    }

    public long TotalReclaimableDiskBytes
    {
        get => _totalReclaimableDiskBytes;
        set => SetProperty(ref _totalReclaimableDiskBytes, value);
    }

    public int SafeActionsCount
    {
        get => _safeActionsCount;
        set => SetProperty(ref _safeActionsCount, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        set => SetProperty(ref _resultSummary, value);
    }

    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = new();
    public ObservableCollection<CleanupItemViewModel> CleanupItems { get; } = new();

    public ICommand ScanCommand { get; }
    public ICommand ExecuteOptimizationCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }

    public QuickOptimizeViewModel(
        IMemoryMonitor monitor,
        ICleanupEngine cleanupEngine,
        Win32ProcessService processService,
        ISafetyEngine safetyEngine,
        ILoggerService logger)
    {
        _monitor = monitor;
        _cleanupEngine = cleanupEngine;
        _processService = processService;
        _safetyEngine = safetyEngine;
        _logger = logger;

        ScanCommand = new AsyncRelayCommand(RunScanAsync, () => !IsScanning && !IsOptimizing);
        ExecuteOptimizationCommand = new AsyncRelayCommand(ExecuteOptimizationAsync, () => !IsScanning && !IsOptimizing);
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);

        _ = RunScanAsync();
    }

    public async Task RunScanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning memory consumers and temporary caches...";
        ResultSummary = string.Empty;

        Recommendations.Clear();
        CleanupItems.Clear();

        await _monitor.RefreshAsync();

        // 1. Process Recommendations
        var recs = _recommendationEngine.GenerateRecommendations(
            _monitor.CurrentSystemMetrics,
            _monitor.CurrentProcesses
        );

        foreach (var r in recs)
        {
            var item = new RecommendationItemViewModel(r);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(RecommendationItemViewModel.IsSelected))
                {
                    RecalculateTotals();
                    CommandManager.InvalidateRequerySuggested();
                }
            };
            Recommendations.Add(item);
        }

        // 2. Temp and Cache Locations
        var junkLocations = await _cleanupEngine.ScanLocationsAsync();
        foreach (var j in junkLocations)
        {
            var item = new CleanupItemViewModel(j);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CleanupItemViewModel.IsSelected))
                {
                    RecalculateTotals();
                    CommandManager.InvalidateRequerySuggested();
                }
            };
            CleanupItems.Add(item);
        }

        RecalculateTotals();
        StatusText = $"Scan complete. Found {SafeActionsCount} safe optimization opportunities.";
        _logger.LogInfo($"Quick Optimize scan complete: {Recommendations.Count} app recommendations, {CleanupItems.Count} cache locations", "QuickOptimize");
        IsScanning = false;
        CommandManager.InvalidateRequerySuggested();
    }

    private void RecalculateTotals()
    {
        long reclaimRam = 0;
        foreach (var r in Recommendations)
        {
            if (r.IsSelected) reclaimRam += r.Recommendation.EstimatedReclaimBytes;
        }

        long reclaimDisk = 0;
        foreach (var c in CleanupItems)
        {
            if (c.IsSelected) reclaimDisk += c.Item.EstimatedSizeBytes;
        }

        TotalReclaimableRamBytes = reclaimRam;
        TotalReclaimableDiskBytes = reclaimDisk;
        SafeActionsCount = Recommendations.Count(r => r.IsSelected) + CleanupItems.Count(c => c.IsSelected);
    }

    public async Task ExecuteOptimizationAsync()
    {
        IsOptimizing = true;
        StatusText = "Executing safe optimization actions...";
        _logger.LogInfo("Starting Quick Optimization sequence", "QuickOptimize");

        long ramAvailableBefore = _monitor.CurrentSystemMetrics.AvailablePhysicalBytes;
        int appsClosed = 0;
        long ramReclaimedFromApps = 0;

        // 1. Close Selected Applications Gracefully
        var selectedRecs = Recommendations.Where(r => r.IsSelected).ToList();
        foreach (var item in selectedRecs)
        {
            var rec = item.Recommendation;
            StatusText = $"Gracefully closing {rec.ProcessName}...";

            var currentProc = _monitor.CurrentProcesses.FirstOrDefault(p => p.ProcessId == rec.ProcessId);
            if (currentProc != null)
            {
                var verify = _safetyEngine.VerifyProcessClosure(currentProc);
                if (verify.IsSafeToClose && !currentProc.IsForeground)
                {
                    bool closed = await _processService.CloseProcessGracefullyAsync(rec.ProcessId, TimeSpan.FromSeconds(3));
                    if (closed)
                    {
                        appsClosed++;
                        ramReclaimedFromApps += rec.EstimatedReclaimBytes;
                        _logger.LogInfo($"QuickOptimize closed {rec.ProcessName} (~{rec.EstimatedReclaimBytes / (1024 * 1024):N0} MB)", "QuickOptimize");
                    }
                }
            }
        }

        // 2. Working Set Trimming across all background processes
        StatusText = "Trimming background process working sets...";
        var backgroundPids = _monitor.CurrentProcesses
            .Where(p => p.ProcessId > 4 && !p.IsForeground && p.Risk != RiskLevel.Critical && p.UserProtection != UserProtectionLevel.NeverClose)
            .Select(p => p.ProcessId)
            .ToList();

        var (trimmedCount, _) = _processService.TrimProcesses(backgroundPids);
        _logger.LogInfo($"Trimmed memory working sets of {trimmedCount} background processes", "QuickOptimize");

        // 3. Clean Selected Temporary Caches
        var selectedCleanup = CleanupItems.Where(c => c.IsSelected).Select(c => c.Item).ToList();
        int filesDeleted = 0;
        long diskReclaimed = 0;
        if (selectedCleanup.Count > 0)
        {
            StatusText = "Cleaning safe temporary files and caches...";
            var cleanupResults = await _cleanupEngine.CleanMultipleAsync(selectedCleanup);
            diskReclaimed = cleanupResults.Sum(c => c.ReclaimedBytes);
            filesDeleted = cleanupResults.Sum(c => c.FilesDeleted);
        }

        // 4. Re-sample memory metrics to calculate real reclaimed RAM
        await _monitor.RefreshAsync();
        long ramAvailableAfter = _monitor.CurrentSystemMetrics.AvailablePhysicalBytes;
        long measuredReclaim = Math.Max(0, ramAvailableAfter - ramAvailableBefore);
        long reportedRam = Math.Max(measuredReclaim, ramReclaimedFromApps);

        ResultSummary = $"Optimization complete!\n• Trimmed working sets of {trimmedCount} background processes\n• Closed {appsClosed} background applications (~{ramReclaimedFromApps / (1024 * 1024):N0} MB RAM freed)\n• Deleted {filesDeleted} temporary files ({diskReclaimed / (1024 * 1024):N0} MB disk freed)\n• Available RAM increase: ~{reportedRam / (1024 * 1024):N0} MB";
        StatusText = "Optimization finished.";
        _logger.LogInfo(ResultSummary.Replace("\n", " "), "QuickOptimize");

        IsOptimizing = false;
        CommandManager.InvalidateRequerySuggested();
        await RunScanAsync();
    }

    private void SelectAll()
    {
        foreach (var r in Recommendations) r.IsSelected = true;
        foreach (var c in CleanupItems) c.IsSelected = true;
        RecalculateTotals();
        CommandManager.InvalidateRequerySuggested();
    }

    private void DeselectAll()
    {
        foreach (var r in Recommendations) r.IsSelected = false;
        foreach (var c in CleanupItems) c.IsSelected = false;
        RecalculateTotals();
        CommandManager.InvalidateRequerySuggested();
    }
}
