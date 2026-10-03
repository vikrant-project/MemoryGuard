using MemoryGuard.Core.Interfaces;
using MemoryGuard.Platform.Native;

namespace MemoryGuard.Platform.Services;

public sealed class Win32GlobalShortcutService : IGlobalShortcutService
{
    public const int HOTKEY_ID_OPEN_DASHBOARD = 9001;
    public const int HOTKEY_ID_QUICK_OPTIMIZE = 9002;

    public event EventHandler? OpenDashboardRequested;
    public event EventHandler? QuickOptimizeRequested;

    private IntPtr _registeredHwnd = IntPtr.Zero;
    private bool _isRegistered;

    public bool RegisterShortcuts(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return false;

        UnregisterShortcuts(windowHandle);
        _registeredHwnd = windowHandle;

        // Hotkey 1: Ctrl + Alt + M (VK_M = 0x4D)
        bool success1 = NativeMethods.RegisterHotKey(
            windowHandle,
            HOTKEY_ID_OPEN_DASHBOARD,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
            0x4D
        );

        // Hotkey 2: Ctrl + Alt + Shift + M
        bool success2 = NativeMethods.RegisterHotKey(
            windowHandle,
            HOTKEY_ID_QUICK_OPTIMIZE,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            0x4D
        );

        _isRegistered = success1 || success2;
        return _isRegistered;
    }

    public void UnregisterShortcuts(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero && _registeredHwnd != IntPtr.Zero)
        {
            windowHandle = _registeredHwnd;
        }

        if (windowHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterHotKey(windowHandle, HOTKEY_ID_OPEN_DASHBOARD);
                NativeMethods.UnregisterHotKey(windowHandle, HOTKEY_ID_QUICK_OPTIMIZE);
            }
            catch
            {
                // Ignore
            }
        }

        _isRegistered = false;
        _registeredHwnd = IntPtr.Zero;
    }

    public void HandleHotKeyMessage(int hotKeyId)
    {
        if (hotKeyId == HOTKEY_ID_OPEN_DASHBOARD)
        {
            OpenDashboardRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (hotKeyId == HOTKEY_ID_QUICK_OPTIMIZE)
        {
            QuickOptimizeRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_registeredHwnd != IntPtr.Zero)
        {
            UnregisterShortcuts(_registeredHwnd);
        }
    }
}
