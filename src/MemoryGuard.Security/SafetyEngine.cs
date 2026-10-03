using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;

namespace MemoryGuard.Security;

public sealed class SafetyEngine : ISafetyEngine
{
    private static readonly HashSet<string> ExcludedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.ssh",
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.git"
    };

    public SafetyVerificationResult VerifyProcessClosure(ProcessMetrics process)
    {
        var violations = new List<string>();

        // 1. Process existence & PID check
        if (process.ProcessId <= 4)
        {
            return SafetyVerificationResult.Denied(
                "System kernel processes cannot be terminated.",
                "PID <= 4 (Kernel / System Idle)"
            );
        }

        // 2. MemoryGuard Self-Protection
        if (ImmutableDenylists.SelfProcesses.Contains(process.ProcessName) ||
            process.ProcessName.StartsWith("MemoryGuard", StringComparison.OrdinalIgnoreCase))
        {
            return SafetyVerificationResult.Denied(
                "MemoryGuard cannot terminate its own processes.",
                "Self-Protection Rule"
            );
        }

        // 3. Immutable System Process Denylist
        if (IsImmutableSystemProcess(process.ProcessName, process.ExecutablePath))
        {
            violations.Add("Process is on immutable system denylist");
            return SafetyVerificationResult.Denied(
                "Critical Windows operating system component cannot be terminated.",
                violations.ToArray()
            );
        }

        // 4. Security & Antivirus Software Denylist
        if (IsSecurityProcess(process.ProcessName, process.ExecutablePath))
        {
            violations.Add("Process is protected security software");
            return SafetyVerificationResult.Denied(
                "Security and antivirus software cannot be terminated.",
                violations.ToArray()
            );
        }

        // 5. Driver & Hardware Peripherals
        if (ImmutableDenylists.DriverAndHardwareProcesses.Contains(process.ProcessName))
        {
            violations.Add("Process is a device driver or hardware interface");
            return SafetyVerificationResult.Denied(
                "Hardware drivers and input devices cannot be terminated.",
                violations.ToArray()
            );
        }

        // 6. User Protection List (Never Close)
        if (process.UserProtection == UserProtectionLevel.NeverClose)
        {
            violations.Add("User configured this process as 'Never Close'");
            return SafetyVerificationResult.Denied(
                "Process is marked as 'Never Close' by user configuration.",
                violations.ToArray()
            );
        }

        // 7. Active Foreground Application Guard
        if (process.IsForeground)
        {
            violations.Add("Process is currently in the foreground / user focused");
            return SafetyVerificationResult.Denied(
                "Cannot terminate the active foreground application that the user is currently using.",
                violations.ToArray()
            );
        }

        // 8. Session 0 Windows Service Protection
        if (process.SessionId == 0)
        {
            violations.Add("Process runs in Session 0 (Windows System Service)");
            return SafetyVerificationResult.Denied(
                "Session 0 Windows background services cannot be automatically terminated.",
                violations.ToArray()
            );
        }

        // 9. Unknown Process Safety Guard
        if (process.Categories.Contains(ProcessSafetyCategory.UNKNOWN) || string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            violations.Add("Process executable path is unknown or inaccessible");
            return SafetyVerificationResult.Denied(
                "Processes with unverified identities cannot be automatically terminated.",
                violations.ToArray()
            );
        }

        // 10. Risk Level Guard
        if (process.Risk == RiskLevel.Critical)
        {
            violations.Add("Process risk level is Critical");
            return SafetyVerificationResult.Denied(
                "High-risk system components cannot be terminated.",
                violations.ToArray()
            );
        }

        return SafetyVerificationResult.Allowed(
            allowForceKill: false,
            reason: "Passed all safety verifications for graceful closure."
        );
    }

    public SafetyVerificationResult VerifyProcessTermination(ProcessMetrics process)
    {
        // First must pass standard closure verification
        var closureResult = VerifyProcessClosure(process);
        if (!closureResult.IsSafeToClose)
        {
            return closureResult;
        }

        // Force-kill is only allowed for User Applications, never for background services or unknown
        if (!process.Categories.Contains(ProcessSafetyCategory.USER_APPLICATION))
        {
            return SafetyVerificationResult.Denied(
                "Force termination is restricted exclusively to user desktop applications.",
                "Non-user application category"
            );
        }

        return SafetyVerificationResult.Allowed(
            allowForceKill: true,
            reason: "Passed all safety verifications for force termination fallback."
        );
    }

    public bool IsImmutableSystemProcess(string processName, string? executablePath)
    {
        if (ImmutableDenylists.CriticalSystemProcesses.Contains(processName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            string fileName = Path.GetFileName(executablePath);
            if (ImmutableDenylists.CriticalSystemProcesses.Contains(fileName))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsSecurityProcess(string processName, string? executablePath)
    {
        if (ImmutableDenylists.SecurityProcesses.Contains(processName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            string fileName = Path.GetFileName(executablePath);
            if (ImmutableDenylists.SecurityProcesses.Contains(fileName))
            {
                return true;
            }
        }

        return false;
    }

    public bool ValidatePathSafety(string path, string approvedRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(approvedRoot))
        {
            return false;
        }

        try
        {
            string canonicalPath = Path.GetFullPath(path).TrimEnd('\\', '/');
            string canonicalRoot = Path.GetFullPath(approvedRoot).TrimEnd('\\', '/');

            // 1. Must start with approved root path
            if (!canonicalPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 2. Reject if identical to approved root (never delete root itself)
            if (string.Equals(canonicalPath, canonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 3. Ensure no excluded user critical roots match
            foreach (var excluded in ExcludedRoots)
            {
                if (string.IsNullOrWhiteSpace(excluded)) continue;
                string canonicalExcluded = Path.GetFullPath(excluded).TrimEnd('\\', '/');
                if (canonicalPath.StartsWith(canonicalExcluded, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 4. Reparse point (junction/symlink) inspection
            if (File.Exists(canonicalPath))
            {
                var attr = File.GetAttributes(canonicalPath);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    return false; // Skip symbolic links/junctions
                }
            }
            else if (Directory.Exists(canonicalPath))
            {
                var attr = File.GetAttributes(canonicalPath);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    return false; // Skip directory junctions
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
