namespace MemoryGuard.Core.Models;

public sealed class MemoryGuardSettings
{
    public bool LaunchOnStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = false;
    public int MonitoringIntervalMs { get; set; } = 2000;
    public bool NotificationsEnabled { get; set; } = true;

    // Memory thresholds
    public double MemoryWarningThresholdPercent { get; set; } = 80.0;
    public double MemoryHighThresholdPercent { get; set; } = 90.0;
    public double MemoryCriticalThresholdPercent { get; set; } = 95.0;

    // Optimization & Idle
    public OptimizationMode AutoOptimizationMode { get; set; } = OptimizationMode.Off;
    public int IdleTimeoutMinutes { get; set; } = 30;
    public int MinProcessMemoryMb { get; set; } = 150;

    // Shortcuts
    public bool GlobalShortcutsEnabled { get; set; } = true;

    // Theme & UX
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public bool EnableAnimations { get; set; } = true;

    // Logging & Diagnostics
    public bool EnableLogging { get; set; } = true;
    public int LogRetentionDays { get; set; } = 7;

    // User protections: Key is executable name or lowercased full path
    public Dictionary<string, UserProtectionLevel> ProcessProtections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Enabled cleanup categories
    public Dictionary<string, bool> EnabledCleanupCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UserTemp"] = true,
        ["WindowsTemp"] = true,
        ["CrashDumps"] = true,
        ["ThumbnailCache"] = true,
        ["AppCache"] = true,
        ["BrowserCache"] = false // Safe by default, opt-in for browser cache
    };
}
