using System.Collections.ObjectModel;
using System.Windows.Input;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.UI.Common;

namespace MemoryGuard.UI.ViewModels;

public sealed class StartupItemViewModel : ViewModelBase
{
    private readonly IStartupManager _startupManager;
    private readonly ILoggerService _logger;
    private bool _isEnabled;
    private bool _isRunning;
    private string _statusMessage = string.Empty;

    public StartupItem Item { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _ = ToggleStateAsync(value);
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand StopProcessCommand { get; }

    public StartupItemViewModel(StartupItem item, IStartupManager startupManager, ILoggerService logger)
    {
        Item = item;
        _startupManager = startupManager;
        _logger = logger;
        _isEnabled = item.IsEnabled;
        _isRunning = item.IsRunning;
        _statusMessage = item.IsEnabled ? (item.IsRunning ? "Active" : "Enabled") : (item.IsRunning ? "Disabled (Running)" : "Disabled");
        StopProcessCommand = new AsyncRelayCommand(StopProcessAsync, () => IsRunning);
    }

    private async Task ToggleStateAsync(bool enable)
    {
        bool success = await _startupManager.SetStartupItemStateAsync(Item, enable, stopRunningProcess: true);
        if (!success)
        {
            // Revert on failure
            _isEnabled = !enable;
            OnPropertyChanged(nameof(IsEnabled));
            StatusMessage = "Failed to modify (access restricted).";
        }
        else
        {
            if (!enable)
            {
                IsRunning = false;
                StatusMessage = "Disabled & Stopped";
                _logger.LogInfo($"Startup item '{Item.Name}' disabled and background processes stopped", "Startup");
            }
            else
            {
                StatusMessage = "Enabled";
                _logger.LogInfo($"Startup item '{Item.Name}' enabled", "Startup");
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task StopProcessAsync()
    {
        bool stopped = await _startupManager.StopRunningStartupProcessAsync(Item);
        if (stopped)
        {
            IsRunning = false;
            StatusMessage = IsEnabled ? "Stopped" : "Disabled & Stopped";
            _logger.LogInfo($"Manually stopped running background process for '{Item.Name}'", "Startup");
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public sealed class StartupViewModel : ViewModelBase
{
    private readonly IStartupManager _startupManager;
    private readonly ILoggerService _logger;

    private bool _isLoading;
    private string _statusText = "Loading startup entries...";

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ObservableCollection<StartupItemViewModel> StartupItems { get; } = new();

    public ICommand RefreshCommand { get; }

    public StartupViewModel(IStartupManager startupManager, ILoggerService logger)
    {
        _startupManager = startupManager;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Inspecting startup items from Windows Registry and Startup folders...";
        StartupItems.Clear();

        var items = await _startupManager.GetStartupItemsAsync();
        foreach (var item in items)
        {
            StartupItems.Add(new StartupItemViewModel(item, _startupManager, _logger));
        }

        StatusText = $"Found {StartupItems.Count} startup applications ({StartupItems.Count(s => s.IsRunning)} currently running in background).";
        _logger.LogInfo($"Startup manager loaded {StartupItems.Count} entries ({StartupItems.Count(s => s.IsRunning)} running)", "Startup");
        IsLoading = false;
    }
}
