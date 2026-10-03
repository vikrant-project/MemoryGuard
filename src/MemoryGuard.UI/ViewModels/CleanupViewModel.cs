using System.Collections.ObjectModel;
using System.Windows.Input;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.UI.Common;

namespace MemoryGuard.UI.ViewModels;

public sealed class CleanupItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public CleanupItem Item { get; }
    public string Name => Item.Name;
    public string Description => Item.Description;
    public long EstimatedSizeBytes => Item.EstimatedSizeBytes;
    public int FileCount => Item.FileCount;
    public string TargetPath => Item.TargetPath;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public CleanupItemViewModel(CleanupItem item)
    {
        Item = item;
        _isSelected = item.IsSelected;
    }
}

public sealed class CleanupViewModel : ViewModelBase
{
    private readonly ICleanupEngine _cleanupEngine;
    private readonly ILoggerService _logger;

    private bool _isScanning;
    private bool _isCleaning;
    private string _statusText = "Ready to scan temporary locations.";
    private string _progressText = string.Empty;
    private long _totalSizeBytes;
    private int _totalFileCount;
    private string _resultSummary = string.Empty;

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        set => SetProperty(ref _isCleaning, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public long TotalSizeBytes
    {
        get => _totalSizeBytes;
        set => SetProperty(ref _totalSizeBytes, value);
    }

    public int TotalFileCount
    {
        get => _totalFileCount;
        set => SetProperty(ref _totalFileCount, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        set => SetProperty(ref _resultSummary, value);
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; } = new();

    public ICommand ScanCommand { get; }
    public ICommand CleanSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }

    public CleanupViewModel(ICleanupEngine cleanupEngine, ILoggerService logger)
    {
        _cleanupEngine = cleanupEngine;
        _logger = logger;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && !IsCleaning);
        CleanSelectedCommand = new AsyncRelayCommand(CleanAsync, () => !IsScanning && !IsCleaning && Items.Any(i => i.IsSelected));
        SelectAllCommand = new RelayCommand(() => 
        { 
            foreach (var i in Items) i.IsSelected = true; 
            RecalculateTotals();
            CommandManager.InvalidateRequerySuggested();
        });
        DeselectAllCommand = new RelayCommand(() => 
        { 
            foreach (var i in Items) i.IsSelected = false; 
            RecalculateTotals();
            CommandManager.InvalidateRequerySuggested();
        });

        _ = ScanAsync();
    }

    public async Task ScanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning disposable directories...";
        ProgressText = string.Empty;
        ResultSummary = string.Empty;
        Items.Clear();

        var locations = await _cleanupEngine.ScanLocationsAsync();

        foreach (var loc in locations)
        {
            var vm = new CleanupItemViewModel(loc);
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CleanupItemViewModel.IsSelected))
                {
                    RecalculateTotals();
                    CommandManager.InvalidateRequerySuggested();
                }
            };
            Items.Add(vm);
        }

        RecalculateTotals();
        StatusText = $"Found {TotalFileCount:N0} temporary files occupying {TotalSizeBytes / (1024 * 1024):N0} MB.";
        _logger.LogInfo($"Temp cleaner scanned {Items.Count} locations: {TotalFileCount:N0} files, {TotalSizeBytes / (1024 * 1024):N0} MB", "Cleanup");
        IsScanning = false;
        CommandManager.InvalidateRequerySuggested();
    }

    private void RecalculateTotals()
    {
        long size = 0;
        int files = 0;
        foreach (var item in Items)
        {
            if (item.IsSelected)
            {
                size += item.Item.EstimatedSizeBytes;
                files += item.Item.FileCount;
            }
        }
        TotalSizeBytes = size;
        TotalFileCount = files;
    }

    public async Task CleanAsync()
    {
        IsCleaning = true;
        StatusText = "Cleaning selected temporary files...";
        ResultSummary = string.Empty;

        var selected = Items.Where(i => i.IsSelected).Select(i => i.Item).ToList();
        if (selected.Count == 0)
        {
            StatusText = "No items selected.";
            IsCleaning = false;
            return;
        }

        var progress = new Progress<string>(msg => ProgressText = msg);
        _logger.LogInfo($"Starting temporary file cleanup on {selected.Count} selected locations", "Cleanup");

        var results = await _cleanupEngine.CleanMultipleAsync(selected, progress);

        long reclaimed = results.Sum(r => r.ReclaimedBytes);
        int deleted = results.Sum(r => r.FilesDeleted);
        int skipped = results.Sum(r => r.FilesSkipped);

        ResultSummary = $"Cleanup finished: Deleted {deleted:N0} files, freed {reclaimed / (1024 * 1024):N0} MB ({skipped:N0} in-use files safely skipped).";
        StatusText = "Cleanup complete.";
        _logger.LogInfo(ResultSummary, "Cleanup");

        IsCleaning = false;
        CommandManager.InvalidateRequerySuggested();
        await ScanAsync();
    }
}
