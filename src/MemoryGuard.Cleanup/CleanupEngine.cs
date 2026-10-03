using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;

namespace MemoryGuard.Cleanup;

public sealed class CleanupEngine : ICleanupEngine
{
    private readonly ISafetyEngine _safetyEngine;
    private readonly ISettingsService _settingsService;

    public CleanupEngine(ISafetyEngine safetyEngine, ISettingsService settingsService)
    {
        _safetyEngine = safetyEngine;
        _settingsService = settingsService;
    }

    public Task<IReadOnlyList<CleanupItem>> ScanLocationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupItem>>(() =>
        {
            var items = new List<CleanupItem>();
            var enabledCats = _settingsService.Current.EnabledCleanupCategories;

            // 1. User Temp Files
            if (enabledCats.GetValueOrDefault("UserTemp", true))
            {
                string userTemp = Path.GetTempPath().TrimEnd('\\', '/');
                if (Directory.Exists(userTemp))
                {
                    var (size, count) = CalculateSafeDirectoryMetrics(userTemp, cancellationToken);
                    items.Add(new CleanupItem
                    {
                        Id = "user_temp",
                        Name = "User Temporary Files",
                        Description = "Temporary files created by running user applications in %TEMP%.",
                        Category = "UserTemp",
                        TargetPath = userTemp,
                        Risk = CleanupRiskLevel.Safe,
                        EstimatedSizeBytes = size,
                        FileCount = count,
                        IsSelected = true,
                        RequiresElevation = false
                    });
                }
            }

            // 2. Windows Temp Files
            if (enabledCats.GetValueOrDefault("WindowsTemp", true))
            {
                string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (Directory.Exists(winTemp))
                {
                    var (size, count) = CalculateSafeDirectoryMetrics(winTemp, cancellationToken);
                    items.Add(new CleanupItem
                    {
                        Id = "windows_temp",
                        Name = "System Temporary Files",
                        Description = "Windows system temporary files in C:\\Windows\\Temp.",
                        Category = "WindowsTemp",
                        TargetPath = winTemp,
                        Risk = CleanupRiskLevel.Safe,
                        EstimatedSizeBytes = size,
                        FileCount = count,
                        IsSelected = true,
                        RequiresElevation = true
                    });
                }
            }

            // 3. Crash Dumps
            if (enabledCats.GetValueOrDefault("CrashDumps", true))
            {
                string crashDumps = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "CrashDumps"
                );
                if (Directory.Exists(crashDumps))
                {
                    var (size, count) = CalculateSafeDirectoryMetrics(crashDumps, cancellationToken);
                    items.Add(new CleanupItem
                    {
                        Id = "crash_dumps",
                        Name = "Application Crash Dumps",
                        Description = "Post-mortem memory crash dumps (.dmp) left by crashed applications.",
                        Category = "CrashDumps",
                        TargetPath = crashDumps,
                        Risk = CleanupRiskLevel.Safe,
                        EstimatedSizeBytes = size,
                        FileCount = count,
                        IsSelected = true,
                        RequiresElevation = false
                    });
                }
            }

            // 4. Windows Error Reporting Archive
            string werReportArchive = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\WER\ReportArchive"
            );
            if (Directory.Exists(werReportArchive))
            {
                var (size, count) = CalculateSafeDirectoryMetrics(werReportArchive, cancellationToken);
                items.Add(new CleanupItem
                {
                    Id = "wer_reports",
                    Name = "Windows Error Reports",
                    Description = "Archived error logs and diagnostic crash reports.",
                    Category = "CrashDumps",
                    TargetPath = werReportArchive,
                    Risk = CleanupRiskLevel.Safe,
                    EstimatedSizeBytes = size,
                    FileCount = count,
                    IsSelected = true,
                    RequiresElevation = false
                });
            }

            // 5. Browser Cache (Opt-in only)
            if (enabledCats.GetValueOrDefault("BrowserCache", false))
            {
                string chromeCache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data\Default\Cache\Cache_Data"
                );
                if (Directory.Exists(chromeCache))
                {
                    var (size, count) = CalculateSafeDirectoryMetrics(chromeCache, cancellationToken);
                    items.Add(new CleanupItem
                    {
                        Id = "chrome_cache",
                        Name = "Chrome Cache (Cache_Data only)",
                        Description = "Cached web page assets. Does NOT touch cookies, logins, or history.",
                        Category = "BrowserCache",
                        TargetPath = chromeCache,
                        Risk = CleanupRiskLevel.Moderate,
                        EstimatedSizeBytes = size,
                        FileCount = count,
                        IsSelected = false,
                        RequiresElevation = false
                    });
                }

                string edgeCache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\User Data\Default\Cache\Cache_Data"
                );
                if (Directory.Exists(edgeCache))
                {
                    var (size, count) = CalculateSafeDirectoryMetrics(edgeCache, cancellationToken);
                    items.Add(new CleanupItem
                    {
                        Id = "edge_cache",
                        Name = "Edge Cache (Cache_Data only)",
                        Description = "Cached web page assets. Does NOT touch cookies, logins, or history.",
                        Category = "BrowserCache",
                        TargetPath = edgeCache,
                        Risk = CleanupRiskLevel.Moderate,
                        EstimatedSizeBytes = size,
                        FileCount = count,
                        IsSelected = false,
                        RequiresElevation = false
                    });
                }
            }

            return items;
        }, cancellationToken);
    }

    public async Task<CleanupResult> CleanItemAsync(
        CleanupItem item, 
        IProgress<string>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            long reclaimedBytes = 0;
            int deletedCount = 0;
            int skippedCount = 0;
            var skippedReasons = new List<string>();

            if (!Directory.Exists(item.TargetPath))
            {
                return new CleanupResult
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    ReclaimedBytes = 0,
                    FilesDeleted = 0,
                    FilesSkipped = 0,
                    Succeeded = true
                };
            }

            // Enumerate files safely
            var files = SafeEnumerateFiles(item.TargetPath);

            foreach (var filePath in files)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // 1. Validate path against safety rules (must be within approved root, no reparse point, no traversal)
                if (!_safetyEngine.ValidatePathSafety(filePath, item.TargetPath))
                {
                    skippedCount++;
                    skippedReasons.Add($"Skipped potentially unsafe path or reparse point: {Path.GetFileName(filePath)}");
                    continue;
                }

                // 2. Perform deletion with strict error handling
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    long fileLength = fileInfo.Length;

                    // Remove readonly attribute if present
                    if ((fileInfo.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    fileInfo.Delete();
                    reclaimedBytes += fileLength;
                    deletedCount++;
                    progress?.Report($"Deleted {Path.GetFileName(filePath)} ({fileLength / 1024:N0} KB)");
                }
                catch (IOException)
                {
                    // File is locked in use by another process - perfectly normal for temp files
                    skippedCount++;
                }
                catch (UnauthorizedAccessException)
                {
                    // Lack of permissions - skip safely
                    skippedCount++;
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    skippedReasons.Add($"Failed deleting {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            // Attempt to clean empty subdirectories (never delete target root itself)
            try
            {
                var subdirs = SafeEnumerateDirectories(item.TargetPath)
                    .OrderByDescending(d => d.Length); // Deepest first

                foreach (var dir in subdirs)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (_safetyEngine.ValidatePathSafety(dir, item.TargetPath))
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                Directory.Delete(dir, recursive: false);
                            }
                        }
                        catch
                        {
                            // Ignore locked directory
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal
            }

            return new CleanupResult
            {
                ItemId = item.Id,
                ItemName = item.Name,
                ReclaimedBytes = reclaimedBytes,
                FilesDeleted = deletedCount,
                FilesSkipped = skippedCount,
                SkippedReasons = skippedReasons.Take(10).ToList(),
                Succeeded = true
            };
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CleanupResult>> CleanMultipleAsync(
        IEnumerable<CleanupItem> items, 
        IProgress<string>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        var results = new List<CleanupResult>();
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var res = await CleanItemAsync(item, progress, cancellationToken).ConfigureAwait(false);
            results.Add(res);
        }
        return results;
    }

    private (long SizeBytes, int FileCount) CalculateSafeDirectoryMetrics(string rootPath, CancellationToken token)
    {
        long totalSize = 0;
        int fileCount = 0;

        try
        {
            var files = SafeEnumerateFiles(rootPath);
            foreach (var f in files)
            {
                if (token.IsCancellationRequested) break;

                // Validate safety: skip reparse points and traversal
                if (!_safetyEngine.ValidatePathSafety(f, rootPath)) continue;

                try
                {
                    var fi = new FileInfo(f);
                    totalSize += fi.Length;
                    fileCount++;
                }
                catch
                {
                    // Inaccessible file
                }
            }
        }
        catch
        {
            // Access denied or enumeration error
        }

        return (totalSize, fileCount);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootPath)
    {
        var pending = new Queue<string>();
        pending.Enqueue(rootPath);

        while (pending.Count > 0)
        {
            string currentDir = pending.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(currentDir);
            }
            catch
            {
                subdirs = Array.Empty<string>();
            }

            foreach (var subdir in subdirs)
            {
                try
                {
                    var attr = File.GetAttributes(subdir);
                    if ((attr & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Enqueue(subdir);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }
    }

    private static List<string> SafeEnumerateDirectories(string rootPath)
    {
        var result = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(rootPath);

        while (pending.Count > 0)
        {
            string currentDir = pending.Dequeue();
            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(currentDir);
            }
            catch
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                try
                {
                    var attr = File.GetAttributes(subdir);
                    if ((attr & FileAttributes.ReparsePoint) == 0)
                    {
                        result.Add(subdir);
                        pending.Enqueue(subdir);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }
        return result;
    }
}
