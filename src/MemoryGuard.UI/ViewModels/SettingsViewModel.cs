using System.Windows.Input;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.UI.Common;
using MemoryGuard.UI.Themes;
using Microsoft.Win32;

namespace MemoryGuard.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;

    private bool _launchOnStartup;
    private bool _minimizeToTray;
    private bool _startMinimizedToTray;
    private int _monitoringIntervalMs;
    private bool _notificationsEnabled;
    private double _warningThreshold;
    private double _highThreshold;
    private double _criticalThreshold;
    private OptimizationMode _autoOptimizationMode;
    private int _idleTimeoutMinutes;
    private bool _globalShortcutsEnabled;
    private ThemeMode _theme;
    private string _saveMessage = string.Empty;

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        set => SetProperty(ref _launchOnStartup, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool StartMinimizedToTray
    {
        get => _startMinimizedToTray;
        set => SetProperty(ref _startMinimizedToTray, value);
    }

    public int MonitoringIntervalMs
    {
        get => _monitoringIntervalMs;
        set => SetProperty(ref _monitoringIntervalMs, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public double WarningThreshold
    {
        get => _warningThreshold;
        set => SetProperty(ref _warningThreshold, value);
    }

    public double HighThreshold
    {
        get => _highThreshold;
        set => SetProperty(ref _highThreshold, value);
    }

    public double CriticalThreshold
    {
        get => _criticalThreshold;
        set => SetProperty(ref _criticalThreshold, value);
    }

    public OptimizationMode AutoOptimizationMode
    {
        get => _autoOptimizationMode;
        set => SetProperty(ref _autoOptimizationMode, value);
    }

    public int IdleTimeoutMinutes
    {
        get => _idleTimeoutMinutes;
        set => SetProperty(ref _idleTimeoutMinutes, value);
    }

    public bool GlobalShortcutsEnabled
    {
        get => _globalShortcutsEnabled;
        set => SetProperty(ref _globalShortcutsEnabled, value);
    }

    public ThemeMode Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsSystemTheme));
                ThemeManager.ApplyTheme(value);
            }
        }
    }

    public bool IsDarkTheme
    {
        get => _theme == ThemeMode.Dark;
        set { if (value) Theme = ThemeMode.Dark; }
    }

    public bool IsLightTheme
    {
        get => _theme == ThemeMode.Light;
        set { if (value) Theme = ThemeMode.Light; }
    }

    public bool IsSystemTheme
    {
        get => _theme == ThemeMode.System;
        set { if (value) Theme = ThemeMode.System; }
    }

    public string SaveMessage
    {
        get => _saveMessage;
        set => SetProperty(ref _saveMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand ResetDefaultsCommand { get; }

    public SettingsViewModel(ISettingsService settingsService, ILoggerService logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        LoadFromSettings(_settingsService.Current);

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
    }

    private void LoadFromSettings(MemoryGuardSettings s)
    {
        _launchOnStartup = s.LaunchOnStartup;
        _minimizeToTray = s.MinimizeToTray;
        _startMinimizedToTray = s.StartMinimizedToTray;
        _monitoringIntervalMs = s.MonitoringIntervalMs;
        _notificationsEnabled = s.NotificationsEnabled;
        _warningThreshold = s.MemoryWarningThresholdPercent;
        _highThreshold = s.MemoryHighThresholdPercent;
        _criticalThreshold = s.MemoryCriticalThresholdPercent;
        _autoOptimizationMode = s.AutoOptimizationMode;
        _idleTimeoutMinutes = s.IdleTimeoutMinutes;
        _globalShortcutsEnabled = s.GlobalShortcutsEnabled;
        _theme = s.Theme;

        OnPropertyChanged(string.Empty);
    }

    public async Task SaveAsync()
    {
        var s = _settingsService.Current;
        s.LaunchOnStartup = LaunchOnStartup;
        s.MinimizeToTray = MinimizeToTray;
        s.StartMinimizedToTray = StartMinimizedToTray;
        s.MonitoringIntervalMs = MonitoringIntervalMs;
        s.NotificationsEnabled = NotificationsEnabled;
        s.MemoryWarningThresholdPercent = WarningThreshold;
        s.MemoryHighThresholdPercent = HighThreshold;
        s.MemoryCriticalThresholdPercent = CriticalThreshold;
        s.AutoOptimizationMode = AutoOptimizationMode;
        s.IdleTimeoutMinutes = IdleTimeoutMinutes;
        s.GlobalShortcutsEnabled = GlobalShortcutsEnabled;
        s.Theme = Theme;

        await _settingsService.SaveAsync();
        UpdateWindowsStartupRegistry(LaunchOnStartup);

        SaveMessage = "Settings saved successfully.";
        _logger.LogInfo("Settings updated by user");
    }

    private void ResetDefaults()
    {
        _settingsService.ResetToDefaults();
        LoadFromSettings(_settingsService.Current);
        ThemeManager.ApplyTheme(Theme);
        SaveMessage = "Settings reset to defaults.";
    }

    private static void UpdateWindowsStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                string exePath = Environment.ProcessPath ?? string.Empty;
                if (enable && !string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue("MemoryGuard", $"\"{exePath}\" --minimized");
                }
                else
                {
                    key.DeleteValue("MemoryGuard", false);
                }
            }
        }
        catch
        {
            // Ignore restricted registry errors
        }
    }
}
