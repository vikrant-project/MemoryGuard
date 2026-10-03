using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.Platform.Services;
using MemoryGuard.UI.Common;

namespace MemoryGuard.UI.ViewModels;

public sealed class ProcessExplorerViewModel : ViewModelBase
{
    private readonly IMemoryMonitor _monitor;
    private readonly ISafetyEngine _safetyEngine;
    private readonly ISettingsService _settingsService;
    private readonly Win32ProcessService _processService;
    private readonly ILoggerService _logger;

    private string _searchText = string.Empty;
    private string _selectedFilter = "All";
    private ProcessMetrics? _selectedProcess;
    private string _statusMessage = string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public ProcessMetrics? SelectedProcess
    {
        get => _selectedProcess;
        set => SetProperty(ref _selectedProcess, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<ProcessMetrics> FilteredProcesses { get; } = new();

    public ICommand CloseGracefulCommand { get; }
    public ICommand TerminateForceCommand { get; }
    public ICommand SetProtectionNeverCloseCommand { get; }
    public ICommand SetProtectionAskCommand { get; }
    public ICommand SetProtectionNormalCommand { get; }
    public ICommand RevealInExplorerCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SetFilterCommand { get; }

    public ProcessExplorerViewModel(
        IMemoryMonitor monitor,
        ISafetyEngine safetyEngine,
        ISettingsService settingsService,
        Win32ProcessService processService,
        ILoggerService logger)
    {
        _monitor = monitor;
        _safetyEngine = safetyEngine;
        _settingsService = settingsService;
        _processService = processService;
        _logger = logger;

        SetFilterCommand = new RelayCommand(p => SelectedFilter = p?.ToString() ?? "All");
        CloseGracefulCommand = new AsyncRelayCommand(CloseSelectedGracefullyAsync, () => SelectedProcess != null && SelectedProcess.CanCloseGracefully);
        TerminateForceCommand = new AsyncRelayCommand(TerminateSelectedForceAsync, () => SelectedProcess != null && SelectedProcess.CanTerminateSafely);
        SetProtectionNeverCloseCommand = new RelayCommand(() => SetProtectionForSelected(UserProtectionLevel.NeverClose), () => SelectedProcess != null);
        SetProtectionAskCommand = new RelayCommand(() => SetProtectionForSelected(UserProtectionLevel.AskBeforeClosing), () => SelectedProcess != null);
        SetProtectionNormalCommand = new RelayCommand(() => SetProtectionForSelected(UserProtectionLevel.Normal), () => SelectedProcess != null);
        RevealInExplorerCommand = new RelayCommand(RevealSelectedInExplorer, () => SelectedProcess != null && !string.IsNullOrWhiteSpace(SelectedProcess.ExecutablePath));
        RefreshCommand = new AsyncRelayCommand(async () => await _monitor.RefreshAsync());

        _monitor.ProcessesUpdated += OnProcessesUpdated;
        ApplyFilter();
    }

    private void OnProcessesUpdated(object? sender, IReadOnlyList<ProcessMetrics> processes)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => ApplyFilter());
    }

    private void ApplyFilter()
    {
        var all = _monitor.CurrentProcesses;
        if (all == null) return;

        IEnumerable<ProcessMetrics> query = all;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string term = _searchText.Trim();
            query = query.Where(p => 
                p.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.ProcessId.ToString().Contains(term) ||
                p.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase)
            );
        }

        query = _selectedFilter switch
        {
            "UserApps" => query.Where(p => p.Categories.Contains(ProcessSafetyCategory.USER_APPLICATION)),
            "HighMemory" => query.Where(p => p.WorkingSetBytes > 200L * 1024 * 1024),
            "Leaks" => query.Where(p => p.IsPossibleMemoryLeak),
            "Protected" => query.Where(p => p.UserProtection != UserProtectionLevel.Normal || p.Risk == RiskLevel.Critical),
            _ => query
        };

        FilteredProcesses.Clear();
        foreach (var p in query)
        {
            FilteredProcesses.Add(p);
        }
    }

    private async Task CloseSelectedGracefullyAsync()
    {
        if (SelectedProcess == null) return;

        var proc = SelectedProcess;
        var verify = _safetyEngine.VerifyProcessClosure(proc);
        if (!verify.IsSafeToClose)
        {
            StatusMessage = $"Safety block: {verify.Reason}";
            _logger.LogWarning($"Blocked closure of PID {proc.ProcessId} ({proc.ProcessName}): {verify.Reason}");
            return;
        }

        StatusMessage = $"Attempting graceful close for {proc.ProcessName} (PID {proc.ProcessId})...";
        _logger.LogInfo($"Closing process gracefully: PID {proc.ProcessId} ({proc.ProcessName})");

        bool closed = await _processService.CloseProcessGracefullyAsync(proc.ProcessId, TimeSpan.FromSeconds(3));
        if (closed)
        {
            StatusMessage = $"Successfully closed {proc.ProcessName}. Estimated reclaimed: ~{proc.EstimatedReclaimableBytes / (1024 * 1024):N0} MB";
            _logger.LogInfo($"Process closed: {proc.ProcessName}");
        }
        else
        {
            StatusMessage = $"{proc.ProcessName} did not close within timeout.";
            _logger.LogWarning($"Process {proc.ProcessName} did not respond to close request.");
        }

        await _monitor.RefreshAsync();
    }

    private async Task TerminateSelectedForceAsync()
    {
        if (SelectedProcess == null) return;

        var proc = SelectedProcess;
        var verify = _safetyEngine.VerifyProcessTermination(proc);
        if (!verify.IsSafeToTerminate)
        {
            StatusMessage = $"Safety block: {verify.Reason}";
            _logger.LogWarning($"Blocked termination of PID {proc.ProcessId}: {verify.Reason}");
            return;
        }

        StatusMessage = $"Force terminating {proc.ProcessName} (PID {proc.ProcessId})...";
        _logger.LogWarning($"Force terminating process PID {proc.ProcessId} ({proc.ProcessName})");

        bool killed = _processService.TerminateProcessForce(proc.ProcessId);
        if (killed)
        {
            StatusMessage = $"Terminated {proc.ProcessName}.";
        }
        else
        {
            StatusMessage = $"Failed to terminate {proc.ProcessName}. Access denied.";
        }

        await _monitor.RefreshAsync();
    }

    private void SetProtectionForSelected(UserProtectionLevel level)
    {
        if (SelectedProcess == null) return;

        _settingsService.SetProcessProtection(
            SelectedProcess.ProcessName,
            SelectedProcess.ExecutablePath,
            level
        );

        StatusMessage = $"Protection for {SelectedProcess.ProcessName} set to: {level}";
        _logger.LogInfo($"User protection changed: {SelectedProcess.ProcessName} -> {level}");

        _ = _monitor.RefreshAsync();
    }

    private void RevealSelectedInExplorer()
    {
        if (SelectedProcess == null || string.IsNullOrWhiteSpace(SelectedProcess.ExecutablePath)) return;

        try
        {
            if (File.Exists(SelectedProcess.ExecutablePath))
            {
                Process.Start("explorer.exe", $"/select,\"{SelectedProcess.ExecutablePath}\"");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to reveal file: {ex.Message}");
        }
    }
}
