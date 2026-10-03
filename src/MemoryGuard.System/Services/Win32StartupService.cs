using System.Diagnostics;
using Microsoft.Win32;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;

namespace MemoryGuard.Platform.Services;

public sealed class Win32StartupService : IStartupManager
{
    private const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RUN_APPROVED_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly Win32SecurityService _securityService;

    public Win32StartupService(Win32SecurityService securityService)
    {
        _securityService = securityService;
    }

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<StartupItem>>(() =>
        {
            var items = new List<StartupItem>();

            // 1. Current User Registry Run
            ScanRegistryRunKey(Registry.CurrentUser, "Registry (Current User)", items);

            // 2. Local Machine Registry Run
            ScanRegistryRunKey(Registry.LocalMachine, "Registry (Local Machine)", items);

            // 3. User Startup Folder
            ScanStartupFolder(Environment.SpecialFolder.Startup, "Startup Folder (User)", items);

            // 4. Common Startup Folder
            ScanStartupFolder(Environment.SpecialFolder.CommonStartup, "Startup Folder (All Users)", items);

            return items;
        }, cancellationToken);
    }

    public Task<bool> SetStartupItemStateAsync(
        StartupItem item, 
        bool enable, 
        bool stopRunningProcess = true, 
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            bool modified = false;
            try
            {
                if (item.Location.Contains("Current User"))
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RUN_APPROVED_KEY_PATH);
                    if (key != null)
                    {
                        // Microsoft uses a binary value where first byte 0x02 is enabled, 0x03 is disabled
                        byte[] value = enable 
                            ? new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                            : new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        key.SetValue(item.Name, value, RegistryValueKind.Binary);
                        modified = true;
                    }
                }
                else if (item.Location.Contains("Startup Folder"))
                {
                    string path = item.Command;
                    if (enable)
                    {
                        if (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            string targetPath = path.Substring(0, path.Length - ".disabled".Length);
                            if (File.Exists(path))
                            {
                                if (File.Exists(targetPath)) File.Delete(targetPath);
                                File.Move(path, targetPath);
                                modified = true;
                            }
                        }
                    }
                    else
                    {
                        if (!path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            string targetPath = path + ".disabled";
                            if (File.Exists(path))
                            {
                                if (File.Exists(targetPath)) File.Delete(targetPath);
                                File.Move(path, targetPath);
                                modified = true;
                            }
                        }
                    }
                }

                // If disabled by user and stopRunningProcess is requested:
                if (!enable && stopRunningProcess)
                {
                    await StopRunningStartupProcessAsync(item);
                }
            }
            catch
            {
                // Access denied or not permitted
            }
            return modified;
        }, cancellationToken);
    }

    public bool IsProcessRunningForStartupItem(StartupItem item)
    {
        string procName = GetProcessNameFromStartupItem(item);
        if (string.IsNullOrWhiteSpace(procName)) return false;

        try
        {
            var procs = Process.GetProcessesByName(procName);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> StopRunningStartupProcessAsync(StartupItem item)
    {
        string procName = GetProcessNameFromStartupItem(item);
        if (string.IsNullOrWhiteSpace(procName)) return false;

        return await Task.Run(() =>
        {
            try
            {
                var procs = Process.GetProcessesByName(procName);
                bool anyKilled = false;
                foreach (var p in procs)
                {
                    using (p)
                    {
                        if (p.Id <= 4) continue;
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                p.CloseMainWindow();
                                if (p.WaitForExit(1500))
                                {
                                    anyKilled = true;
                                    continue;
                                }
                            }
                            p.Kill();
                            anyKilled = true;
                        }
                        catch
                        {
                            // Skip locked/unauthorized process
                        }
                    }
                }
                return anyKilled;
            }
            catch
            {
                return false;
            }
        });
    }

    public static string GetProcessNameFromStartupItem(StartupItem item)
    {
        string candidate = string.Empty;
        if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            candidate = Path.GetFileNameWithoutExtension(item.ExecutablePath);
        }
        if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(item.Command))
        {
            string exe = ExtractExePath(item.Command);
            if (!string.IsNullOrWhiteSpace(exe))
            {
                candidate = Path.GetFileNameWithoutExtension(exe);
            }
        }
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = item.Name;
        }
        return candidate.TrimEnd();
    }

    private void ScanRegistryRunKey(RegistryKey rootKey, string locationName, List<StartupItem> results)
    {
        try
        {
            using var runKey = rootKey.OpenSubKey(RUN_KEY_PATH, false);
            if (runKey == null) return;

            using var approvedKey = rootKey.OpenSubKey(RUN_APPROVED_KEY_PATH, false);

            foreach (var valueName in runKey.GetValueNames())
            {
                var cmdObj = runKey.GetValue(valueName);
                if (cmdObj is not string cmdStr || string.IsNullOrWhiteSpace(cmdStr)) continue;

                string exePath = ExtractExePath(cmdStr);
                string publisher = string.Empty;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var (_, pub) = _securityService.VerifyFileSignature(exePath);
                    publisher = pub;
                }

                bool isEnabled = true;
                if (approvedKey != null)
                {
                    var approvedBytes = approvedKey.GetValue(valueName) as byte[];
                    if (approvedBytes != null && approvedBytes.Length > 0 && approvedBytes[0] == 0x03)
                    {
                        isEnabled = false;
                    }
                }

                string impact = DetermineImpact(exePath);

                string checkProcName = !string.IsNullOrWhiteSpace(exePath) 
                    ? Path.GetFileNameWithoutExtension(exePath) 
                    : valueName;
                bool isRunning = false;
                try
                {
                    var procs = Process.GetProcessesByName(checkProcName);
                    isRunning = procs.Length > 0;
                    foreach (var p in procs) p.Dispose();
                }
                catch { }

                results.Add(new StartupItem
                {
                    Id = $"{locationName}_{valueName}",
                    Name = valueName,
                    Command = cmdStr,
                    ExecutablePath = exePath,
                    Publisher = publisher,
                    Location = locationName,
                    IsEnabled = isEnabled,
                    IsRunning = isRunning,
                    Impact = impact,
                    RecommendedAction = isEnabled ? (isRunning ? "Running in background" : "Enabled") : "Disabled by user",
                    CanDisable = !locationName.Contains("Local Machine") // Safe: avoid requiring UAC unless necessary
                });
            }
        }
        catch
        {
            // Ignore restricted registry keys
        }
    }

    private void ScanStartupFolder(Environment.SpecialFolder folder, string locationName, List<StartupItem> results)
    {
        try
        {
            string folderPath = Environment.GetFolderPath(folder);
            if (!Directory.Exists(folderPath)) return;

            var files = Directory.GetFiles(folderPath, "*.*");
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".lnk" && ext != ".url" && ext != ".disabled") continue;

                string rawName = Path.GetFileNameWithoutExtension(file);
                string name = rawName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) 
                    ? Path.GetFileNameWithoutExtension(rawName) 
                    : rawName;
                bool isEnabled = ext != ".disabled";

                bool isRunning = false;
                try
                {
                    var procs = Process.GetProcessesByName(name);
                    isRunning = procs.Length > 0;
                    foreach (var p in procs) p.Dispose();
                }
                catch { }

                results.Add(new StartupItem
                {
                    Id = $"{locationName}_{name}",
                    Name = name,
                    Command = file,
                    ExecutablePath = file,
                    Publisher = string.Empty,
                    Location = locationName,
                    IsEnabled = isEnabled,
                    IsRunning = isRunning,
                    Impact = "Low",
                    RecommendedAction = isEnabled ? (isRunning ? "Running in background" : "Enabled") : "Disabled",
                    CanDisable = true
                });
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static string ExtractExePath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;

        string trimmed = commandLine.Trim();
        if (trimmed.StartsWith("\""))
        {
            int nextQuote = trimmed.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                return trimmed.Substring(1, nextQuote - 1);
            }
        }

        int spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed.Substring(0, spaceIdx) : trimmed;
    }

    private static string DetermineImpact(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return "Low";
        }

        try
        {
            var fi = new FileInfo(exePath);
            if (fi.Length > 50 * 1024 * 1024) return "High";
            if (fi.Length > 10 * 1024 * 1024) return "Medium";
        }
        catch
        {
            // Ignore
        }
        return "Low";
    }
}
