namespace MemoryGuard.Core.Models;

public enum ProcessSafetyCategory
{
    CRITICAL_SYSTEM,
    PROTECTED_SYSTEM,
    SECURITY_SOFTWARE,
    DRIVER_RELATED,
    WINDOWS_SERVICE,
    FOREGROUND_APPLICATION,
    USER_PROTECTED,
    USER_APPLICATION,
    BACKGROUND_APPLICATION,
    IDLE_APPLICATION,
    HIGH_MEMORY_APPLICATION,
    POSSIBLE_MEMORY_LEAK,
    UNKNOWN
}

public enum RiskLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum UserProtectionLevel
{
    Normal = 0,
    AskBeforeClosing = 1,
    NeverClose = 2
}

public enum MemoryPressureLevel
{
    Normal = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

public enum OptimizationMode
{
    Off = 0,
    SuggestOnly = 1,
    ConservativeAuto = 2
}

public enum CleanupRiskLevel
{
    Safe = 0,
    Moderate = 1,
    High = 2
}

public enum ThemeMode
{
    Dark = 0,
    Light = 1,
    System = 2
}
