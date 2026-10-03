using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Platform.Native;
using MemoryGuard.UI.ViewModels;

namespace MemoryGuard.UI.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IGlobalShortcutService _shortcutService;
    private bool _isExplicitExit;

    public MainWindow(
        MainViewModel viewModel,
        ISettingsService settingsService,
        IGlobalShortcutService shortcutService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settingsService = settingsService;
        _shortcutService = shortcutService;

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // Apply Windows 11 immersive dark mode title bar
        int darkMode = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        // Apply Windows 11 rounded window corners
        int cornerPref = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Register Win32 message hook for HotKeys
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        if (_settingsService.Current.GlobalShortcutsEnabled)
        {
            _shortcutService.RegisterShortcuts(hwnd);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();
            _shortcutService.HandleHotKeyMessage(hotkeyId);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit && _settingsService.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _shortcutService.UnregisterShortcuts(hwnd);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settingsService.Current.MinimizeToTray)
        {
            Hide();
        }
    }

    public void ForceClose()
    {
        _isExplicitExit = true;
        Close();
    }
}
