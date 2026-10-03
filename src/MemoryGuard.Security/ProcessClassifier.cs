using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;

namespace MemoryGuard.Security;

public sealed class ProcessClassifier : IProcessClassifier
{
    private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string System32Directory = Environment.GetFolderPath(Environment.SpecialFolder.System);

    public (IReadOnlyList<ProcessSafetyCategory> Categories, RiskLevel Risk, int RiskScore, string Reason) Classify(
        string processName,
        string executablePath,
        int sessionId,
        bool isForeground,
        TimeSpan idleTime,
        long workingSetBytes,
        bool isSignatureVerified,
        UserProtectionLevel userProtection)
    {
        var categories = new List<ProcessSafetyCategory>();
        int riskScore = 0;
        string reason = string.Empty;

        // 1. MemoryGuard Self-Protection
        if (ImmutableDenylists.SelfProcesses.Contains(processName) ||
            processName.StartsWith("MemoryGuard", StringComparison.OrdinalIgnoreCase))
        {
            categories.Add(ProcessSafetyCategory.PROTECTED_SYSTEM);
            return (categories, RiskLevel.Critical, 100, "MemoryGuard self process");
        }

        // 2. Critical Windows OS Denylist
        if (ImmutableDenylists.CriticalSystemProcesses.Contains(processName) ||
            (!string.IsNullOrEmpty(executablePath) && IsInSystemDirectory(executablePath) && isSignatureVerified && sessionId == 0))
        {
            categories.Add(ProcessSafetyCategory.CRITICAL_SYSTEM);
            return (categories, RiskLevel.Critical, 100, "Critical Windows operating system process");
        }

        // 3. Security, Antivirus, EDR
        if (ImmutableDenylists.SecurityProcesses.Contains(processName))
        {
            categories.Add(ProcessSafetyCategory.SECURITY_SOFTWARE);
            return (categories, RiskLevel.Critical, 100, "Security / Antivirus software");
        }

        // 4. Driver and Hardware per-device components
        if (ImmutableDenylists.DriverAndHardwareProcesses.Contains(processName))
        {
            categories.Add(ProcessSafetyCategory.DRIVER_RELATED);
            return (categories, RiskLevel.Critical, 95, "Device driver or peripheral manager");
        }

        // 5. Active Installers & Updaters
        if (ImmutableDenylists.InstallerProcesses.Contains(processName))
        {
            categories.Add(ProcessSafetyCategory.PROTECTED_SYSTEM);
            return (categories, RiskLevel.Critical, 90, "Active installer or update manager");
        }

        // 6. User Protection List
        if (userProtection == UserProtectionLevel.NeverClose)
        {
            categories.Add(ProcessSafetyCategory.USER_PROTECTED);
            return (categories, RiskLevel.Critical, 100, "User configured: Never Close");
        }

        // 7. Active Foreground Application
        if (isForeground)
        {
            categories.Add(ProcessSafetyCategory.FOREGROUND_APPLICATION);
            return (categories, RiskLevel.Critical, 90, "Active foreground window in use by user");
        }

        // 8. Session 0 Windows Services
        if (sessionId == 0)
        {
            categories.Add(ProcessSafetyCategory.WINDOWS_SERVICE);
            riskScore = 80;
            reason = "Session 0 system service";
            return (categories, RiskLevel.High, riskScore, reason);
        }

        // 9. Unknown or Unverified process
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            categories.Add(ProcessSafetyCategory.UNKNOWN);
            riskScore = 75;
            reason = "Inaccessible process identity or restricted access";
            return (categories, RiskLevel.High, riskScore, reason);
        }

        // 10. User Desktop Applications (Session > 0)
        categories.Add(ProcessSafetyCategory.USER_APPLICATION);

        if (isForeground)
        {
            categories.Add(ProcessSafetyCategory.FOREGROUND_APPLICATION);
        }
        else
        {
            categories.Add(ProcessSafetyCategory.BACKGROUND_APPLICATION);
        }

        // High memory (> 200 MB)
        if (workingSetBytes > 200L * 1024 * 1024)
        {
            categories.Add(ProcessSafetyCategory.HIGH_MEMORY_APPLICATION);
        }

        // Idle evaluation (> 15 minutes)
        if (idleTime.TotalMinutes >= 15 && !isForeground)
        {
            categories.Add(ProcessSafetyCategory.IDLE_APPLICATION);
        }

        if (userProtection == UserProtectionLevel.AskBeforeClosing)
        {
            categories.Add(ProcessSafetyCategory.USER_PROTECTED);
            riskScore = 50;
            reason = "User configured: Ask Before Closing";
            return (categories, RiskLevel.Medium, riskScore, reason);
        }

        // Calculate risk score for user applications
        // Lower score = safer candidate to recommend for optimization
        if (categories.Contains(ProcessSafetyCategory.IDLE_APPLICATION) &&
            categories.Contains(ProcessSafetyCategory.BACKGROUND_APPLICATION))
        {
            if (idleTime.TotalMinutes >= 30)
            {
                riskScore = 15;
                reason = $"Background application idle for {Math.Round(idleTime.TotalMinutes)} minutes";
                return (categories, RiskLevel.Low, riskScore, reason);
            }
            else
            {
                riskScore = 30;
                reason = $"Background application idle for {Math.Round(idleTime.TotalMinutes)} minutes";
                return (categories, RiskLevel.Low, riskScore, reason);
            }
        }

        riskScore = 45;
        reason = "Active background application";
        return (categories, RiskLevel.Medium, riskScore, reason);
    }

    private static bool IsInSystemDirectory(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(System32Directory, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(WindowsDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
