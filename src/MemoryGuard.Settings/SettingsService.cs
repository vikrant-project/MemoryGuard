using System.Text.Json;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;

namespace MemoryGuard.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDirectory;
    private readonly object _lock = new();
    private MemoryGuardSettings _current = new();

    public event EventHandler<MemoryGuardSettings>? SettingsChanged;

    public MemoryGuardSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
        private set
        {
            lock (_lock)
            {
                _current = value;
            }
        }
    }

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _settingsFilePath = customPath;
            _settingsDirectory = Path.GetDirectoryName(customPath) ?? AppContext.BaseDirectory;
        }
        else
        {
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MemoryGuard"
            );
            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        }
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            ResetToDefaults();
            await SaveAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                HandleCorruptedSettings("File was empty");
                return;
            }

            var loaded = JsonSerializer.Deserialize<MemoryGuardSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (loaded != null)
            {
                Current = loaded;
                SettingsChanged?.Invoke(this, Current);
            }
            else
            {
                HandleCorruptedSettings("Deserialized to null");
            }
        }
        catch (Exception ex)
        {
            HandleCorruptedSettings(ex.Message);
        }
    }

    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    public async Task SaveAsync()
    {
        await _saveSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_current, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }

            Directory.CreateDirectory(_settingsDirectory);

            string tempFilePath = Path.Combine(_settingsDirectory, $"settings_{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(tempFilePath, json).ConfigureAwait(false);

            // Atomic file update
            File.Move(tempFilePath, _settingsFilePath, overwrite: true);

            SettingsChanged?.Invoke(this, Current);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public void ResetToDefaults()
    {
        Current = new MemoryGuardSettings();
        SettingsChanged?.Invoke(this, Current);
    }

    public UserProtectionLevel GetProcessProtection(string processName, string? executablePath)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(executablePath) &&
                _current.ProcessProtections.TryGetValue(executablePath, out var pathProtection))
            {
                return pathProtection;
            }

            if (_current.ProcessProtections.TryGetValue(processName, out var nameProtection))
            {
                return nameProtection;
            }

            string nameWithExt = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                ? processName 
                : processName + ".exe";

            if (_current.ProcessProtections.TryGetValue(nameWithExt, out var extProtection))
            {
                return extProtection;
            }

            return UserProtectionLevel.Normal;
        }
    }

    public void SetProcessProtection(string processName, string? executablePath, UserProtectionLevel level)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                _current.ProcessProtections[executablePath] = level;
            }
            _current.ProcessProtections[processName] = level;
        }

        _ = Task.Run(SaveAsync);
    }

    private void HandleCorruptedSettings(string reason)
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string backup = Path.Combine(
                    _settingsDirectory, 
                    $"settings_corrupted_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak"
                );
                File.Copy(_settingsFilePath, backup, overwrite: true);
            }
        }
        catch
        {
            // Best effort backup, never throw
        }

        ResetToDefaults();
        _ = Task.Run(SaveAsync);
    }
}
