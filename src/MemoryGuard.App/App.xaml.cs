using System.Threading;
using System.Windows;
using MemoryGuard.Cleanup;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Services;
using MemoryGuard.Monitoring;
using MemoryGuard.Platform.Services;
using MemoryGuard.Security;
using MemoryGuard.Settings;
using MemoryGuard.UI.Themes;
using MemoryGuard.UI.ViewModels;
using MemoryGuard.UI.Views;

namespace MemoryGuard.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Global\\MemoryGuard_SingleInstance_Mutex_2026";
    private Mutex? _singleInstanceMutex;
    private TrayIconManager? _trayIconManager;
    private IMemoryMonitor? _memoryMonitor;
    private IGlobalShortcutService? _shortcutService;
    private ILoggerService? _logger;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Single Instance Enforcement
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // Another instance is already running
            Shutdown();
            return;
        }

        // 2. Logging & Exception Handling Setup
        _logger = new FileLoggerService();
        _logger.LogInfo("=== MemoryGuard Application Starting ===");

        SetupGlobalExceptionHandling();

        // 3. Settings Initialization
        var settingsService = new SettingsService();
        await settingsService.LoadAsync();
        ThemeManager.ApplyTheme(settingsService.Current.Theme);

        // 4. Core Subsystem Services
        var securityService = new Win32SecurityService();
        var memoryService = new Win32MemoryService();
        var processService = new Win32ProcessService(securityService);
        var startupService = new Win32StartupService(securityService);
        var classifier = new ProcessClassifier();
        var safetyEngine = new SafetyEngine();
        var leakDetector = new LeakDetector();
        var cleanupEngine = new CleanupEngine(safetyEngine, settingsService);

        _shortcutService = new Win32GlobalShortcutService();

        _memoryMonitor = new MemoryMonitor(
            memoryService,
            processService,
            securityService,
            classifier,
            safetyEngine,
            settingsService,
            leakDetector
        );

        // 5. MVVM ViewModels
        var logsVM = new LogsViewModel(_logger);
        var settingsVM = new SettingsViewModel(settingsService, _logger);
        var startupVM = new StartupViewModel(startupService, _logger);
        var cleanupVM = new CleanupViewModel(cleanupEngine, _logger);
        var quickOptimizeVM = new QuickOptimizeViewModel(_memoryMonitor, cleanupEngine, processService, safetyEngine, _logger);
        var processExplorerVM = new ProcessExplorerViewModel(_memoryMonitor, safetyEngine, settingsService, processService, _logger);

        Action navigateToQuickOptimize = () => { };

        var dashboardVM = new DashboardViewModel(_memoryMonitor, processService, _logger, () => navigateToQuickOptimize());

        var mainVM = new MainViewModel(
            dashboardVM,
            processExplorerVM,
            quickOptimizeVM,
            cleanupVM,
            startupVM,
            settingsVM,
            logsVM,
            _memoryMonitor
        );

        navigateToQuickOptimize = () => mainVM.OpenQuickOptimize();

        // 6. Main Window Creation
        _mainWindow = new MainWindow(mainVM, settingsService, _shortcutService);

        // 7. System Tray Setup
        _trayIconManager = new TrayIconManager(
            _mainWindow,
            _memoryMonitor,
            () => mainVM.OpenQuickOptimize(),
            () => mainVM.SwitchView(settingsVM, "Settings"),
            _logger
        );

        // 8. Global Shortcut Handlers
        _shortcutService.OpenDashboardRequested += (s, ev) =>
        {
            _trayIconManager.ShowMainWindow();
            mainVM.OpenDashboard();
        };

        _shortcutService.QuickOptimizeRequested += (s, ev) =>
        {
            _trayIconManager.ShowMainWindow();
            mainVM.OpenQuickOptimize();
        };

        // 9. Start Real-time Monitoring
        _memoryMonitor.Start();
        _logger.LogInfo("Real-time memory monitor started");

        // 10. Show Window or Start Minimized
        bool startMinimized = settingsService.Current.StartMinimizedToTray ||
                             e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                             a.Equals("-m", StringComparison.OrdinalIgnoreCase));

        if (!startMinimized)
        {
            _mainWindow.Show();
        }
        else
        {
            _logger.LogInfo("Started minimized to system tray");
        }
    }

    private void SetupGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            _logger?.LogError("Unhandled AppDomain Exception", ex);
        };

        DispatcherUnhandledException += (s, e) =>
        {
            _logger?.LogError("Unhandled Dispatcher Exception", e.Exception);
            e.Handled = true; // Prevent crash, keep application running resiliently
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            _logger?.LogError("Unobserved Task Exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInfo("=== MemoryGuard Application Shutting Down ===");

        _memoryMonitor?.Stop();
        _memoryMonitor?.Dispose();
        _trayIconManager?.Dispose();
        _shortcutService?.Dispose();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
