using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Services;
using MemoryGuard.UI.Common;

namespace MemoryGuard.UI.ViewModels;

public sealed class LogItemModel
{
    public string Timestamp { get; init; } = string.Empty;
    public string Level { get; init; } = "INFO";
    public string Category { get; init; } = "General";
    public string Message { get; init; } = string.Empty;
    public string RawLine { get; init; } = string.Empty;

    public static LogItemModel Parse(string line)
    {
        // Format: [2026-10-03 10:45:00.123] [INFO] [Category] Message
        try
        {
            if (line.StartsWith("[") && line.Contains("] ["))
            {
                var parts = line.Split(new[] { "] [" }, 3, StringSplitOptions.None);
                if (parts.Length >= 3)
                {
                    string time = parts[0].TrimStart('[');
                    string level = parts[1];
                    string rest = parts[2];
                    int catEnd = rest.IndexOf(']');
                    if (catEnd > 0)
                    {
                        string category = rest.Substring(0, catEnd);
                        string message = rest.Substring(catEnd + 1).Trim();
                        return new LogItemModel
                        {
                            Timestamp = time,
                            Level = level.ToUpperInvariant(),
                            Category = category,
                            Message = message,
                            RawLine = line
                        };
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        return new LogItemModel
        {
            Timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            Level = "INFO",
            Category = "System",
            Message = line,
            RawLine = line
        };
    }
}

public sealed class LogsViewModel : ViewModelBase
{
    private readonly ILoggerService _logger;
    private readonly List<LogItemModel> _allLogs = new();
    private string _filterLevel = "ALL";
    private string _searchText = string.Empty;
    private string _statusText = string.Empty;

    public ObservableCollection<LogItemModel> FilteredLogs { get; } = new();

    public string FilterLevel
    {
        get => _filterLevel;
        set
        {
            if (SetProperty(ref _filterLevel, value))
            {
                ApplyFilter();
            }
        }
    }

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

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand SetFilterCommand { get; }

    public LogsViewModel(ILoggerService logger)
    {
        _logger = logger;

        RefreshCommand = new RelayCommand(RefreshLogs);
        ClearLogsCommand = new RelayCommand(ClearLogs);
        CopyLogsCommand = new RelayCommand(CopyLogs);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        SetFilterCommand = new RelayCommand(param =>
        {
            if (param is string level) FilterLevel = level;
        });

        _logger.LogEntryAdded += OnLogEntryAdded;
        _logger.LogsCleared += OnLogsCleared;

        RefreshLogs();
    }

    private void OnLogEntryAdded(string logLine)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var item = LogItemModel.Parse(logLine);
            _allLogs.Add(item);
            if (_allLogs.Count > 1000) _allLogs.RemoveAt(0);

            if (MatchesFilter(item))
            {
                FilteredLogs.Add(item);
                if (FilteredLogs.Count > 1000) FilteredLogs.RemoveAt(0);
            }

            UpdateStatusText();
        });
    }

    private void OnLogsCleared()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _allLogs.Clear();
            FilteredLogs.Clear();
            UpdateStatusText();
        });
    }

    public void RefreshLogs()
    {
        _allLogs.Clear();
        FilteredLogs.Clear();

        var logs = _logger.GetRecentLogs(500);
        foreach (var l in logs)
        {
            var item = LogItemModel.Parse(l);
            _allLogs.Add(item);
            if (MatchesFilter(item))
            {
                FilteredLogs.Add(item);
            }
        }

        UpdateStatusText();
    }

    public void ClearLogs()
    {
        _logger.ClearLogs();
    }

    private void CopyLogs()
    {
        try
        {
            var text = string.Join(Environment.NewLine, FilteredLogs.Select(l => l.RawLine));
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                StatusText = $"Copied {FilteredLogs.Count} log lines to clipboard.";
            }
        }
        catch
        {
            // Clipboard access error
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            string dir = (_logger as FileLoggerService)?.LogDirectory ??
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryGuard", "logs");

            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch
        {
            // Ignore
        }
    }

    private void ApplyFilter()
    {
        FilteredLogs.Clear();
        foreach (var log in _allLogs)
        {
            if (MatchesFilter(log))
            {
                FilteredLogs.Add(log);
            }
        }
        UpdateStatusText();
    }

    private bool MatchesFilter(LogItemModel item)
    {
        if (FilterLevel != "ALL" && !string.Equals(item.Level, FilterLevel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return item.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   item.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void UpdateStatusText()
    {
        int info = _allLogs.Count(l => l.Level == "INFO");
        int warn = _allLogs.Count(l => l.Level == "WARN");
        int error = _allLogs.Count(l => l.Level == "ERROR");
        StatusText = $"{_allLogs.Count} total events ({info} info, {warn} warnings, {error} errors)";
    }
}
