using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.UI.Views;
using Application = System.Windows.Application;

namespace MemoryGuard.App;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    private readonly IMemoryMonitor _monitor;
    private readonly Action _triggerQuickOptimize;
    private readonly Action _openSettings;
    private readonly ILoggerService _logger;

    private bool _isDisposed;

    public TrayIconManager(
        MainWindow mainWindow,
        IMemoryMonitor monitor,
        Action triggerQuickOptimize,
        Action openSettings,
        ILoggerService logger)
    {
        _mainWindow = mainWindow;
        _monitor = monitor;
        _triggerQuickOptimize = triggerQuickOptimize;
        _openSettings = openSettings;
        _logger = logger;

        _notifyIcon = new NotifyIcon();

        // Load Icon
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/MemoryGuard;component/Resources/app.ico"));
            if (sri != null)
            {
                using var stream = sri.Stream;
                _notifyIcon.Icon = new Icon(stream);
            }
            else
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Text = "MemoryGuard - Real-time RAM Optimizer";
        _notifyIcon.Visible = true;

        // Context Menu
        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => ShowMainWindow());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);

        var optimizeItem = new ToolStripMenuItem("Quick Optimize", null, (s, e) =>
        {
            ShowMainWindow();
            _triggerQuickOptimize();
        });

        var pauseItem = new ToolStripMenuItem("Pause Monitoring", null, (s, e) =>
        {
            var mi = (ToolStripMenuItem)s!;
            if (_monitor.IsMonitoring)
            {
                _monitor.Stop();
                mi.Text = "Resume Monitoring";
            }
            else
            {
                _monitor.Start();
                mi.Text = "Pause Monitoring";
            }
        });

        var settingsItem = new ToolStripMenuItem("Settings", null, (s, e) =>
        {
            ShowMainWindow();
            _openSettings();
        });

        var exitItem = new ToolStripMenuItem("Exit MemoryGuard", null, (s, e) =>
        {
            _logger.LogInfo("User requested exit via System Tray");
            _mainWindow.ForceClose();
            Application.Current.Shutdown();
        });

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(optimizeItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(pauseItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        _notifyIcon.Click += (s, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };

        _monitor.MetricsUpdated += OnMetricsUpdated;
    }

    private void OnMetricsUpdated(object? sender, SystemMemoryMetrics metrics)
    {
        try
        {
            string tooltip = $"MemoryGuard: {metrics.MemoryLoadPercent:F0}% RAM ({metrics.PressureLevel})";
            if (tooltip.Length >= 64)
            {
                tooltip = tooltip.Substring(0, 63);
            }
            _notifyIcon.Text = tooltip;
        }
        catch
        {
            // Ignore length or formatting errors
        }
    }

    public void ShowMainWindow()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == System.Windows.WindowState.Minimized)
            {
                _mainWindow.WindowState = System.Windows.WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _monitor.MetricsUpdated -= OnMetricsUpdated;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
