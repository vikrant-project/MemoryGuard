using System.Windows.Input;
using MemoryGuard.Core.Interfaces;
using MemoryGuard.Core.Models;
using MemoryGuard.UI.Common;

namespace MemoryGuard.UI.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    private string _currentTab = "Dashboard";
    private double _headerRamPercent;
    private string _headerHealthText = "Healthy";
    private MemoryPressureLevel _headerPressure;

    public DashboardViewModel DashboardVM { get; }
    public ProcessExplorerViewModel ProcessExplorerVM { get; }
    public QuickOptimizeViewModel QuickOptimizeVM { get; }
    public CleanupViewModel CleanupVM { get; }
    public StartupViewModel StartupVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public LogsViewModel LogsVM { get; }

    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            if (SetProperty(ref _currentTab, value))
            {
                OnPropertyChanged(nameof(IsDashboardTab));
                OnPropertyChanged(nameof(IsQuickOptimizeTab));
                OnPropertyChanged(nameof(IsProcessesTab));
                OnPropertyChanged(nameof(IsCleanupTab));
                OnPropertyChanged(nameof(IsStartupTab));
                OnPropertyChanged(nameof(IsSettingsTab));
                OnPropertyChanged(nameof(IsLogsTab));
            }
        }
    }

    public bool IsDashboardTab
    {
        get => CurrentTab == "Dashboard";
        set { if (value) SwitchView(DashboardVM, "Dashboard"); }
    }

    public bool IsQuickOptimizeTab
    {
        get => CurrentTab == "QuickOptimize";
        set { if (value) SwitchView(QuickOptimizeVM, "QuickOptimize"); }
    }

    public bool IsProcessesTab
    {
        get => CurrentTab == "Processes";
        set { if (value) SwitchView(ProcessExplorerVM, "Processes"); }
    }

    public bool IsCleanupTab
    {
        get => CurrentTab == "Cleanup";
        set { if (value) SwitchView(CleanupVM, "Cleanup"); }
    }

    public bool IsStartupTab
    {
        get => CurrentTab == "Startup";
        set { if (value) SwitchView(StartupVM, "Startup"); }
    }

    public bool IsSettingsTab
    {
        get => CurrentTab == "Settings";
        set { if (value) SwitchView(SettingsVM, "Settings"); }
    }

    public bool IsLogsTab
    {
        get => CurrentTab == "Logs";
        set { if (value) SwitchView(LogsVM, "Logs"); }
    }

    public double HeaderRamPercent
    {
        get => _headerRamPercent;
        set => SetProperty(ref _headerRamPercent, value);
    }

    public string HeaderHealthText
    {
        get => _headerHealthText;
        set => SetProperty(ref _headerHealthText, value);
    }

    public MemoryPressureLevel HeaderPressure
    {
        get => _headerPressure;
        set => SetProperty(ref _headerPressure, value);
    }

    public ICommand NavigateDashboardCommand { get; }
    public ICommand NavigateProcessesCommand { get; }
    public ICommand NavigateQuickOptimizeCommand { get; }
    public ICommand NavigateCleanupCommand { get; }
    public ICommand NavigateStartupCommand { get; }
    public ICommand NavigateSettingsCommand { get; }
    public ICommand NavigateLogsCommand { get; }

    public MainViewModel(
        DashboardViewModel dashboardVM,
        ProcessExplorerViewModel processExplorerVM,
        QuickOptimizeViewModel quickOptimizeVM,
        CleanupViewModel cleanupVM,
        StartupViewModel startupVM,
        SettingsViewModel settingsVM,
        LogsViewModel logsVM,
        IMemoryMonitor monitor)
    {
        DashboardVM = dashboardVM;
        ProcessExplorerVM = processExplorerVM;
        QuickOptimizeVM = quickOptimizeVM;
        CleanupVM = cleanupVM;
        StartupVM = startupVM;
        SettingsVM = settingsVM;
        LogsVM = logsVM;

        _currentViewModel = DashboardVM;

        NavigateDashboardCommand = new RelayCommand(() => SwitchView(DashboardVM, "Dashboard"));
        NavigateProcessesCommand = new RelayCommand(() => SwitchView(ProcessExplorerVM, "Processes"));
        NavigateQuickOptimizeCommand = new RelayCommand(() => SwitchView(QuickOptimizeVM, "QuickOptimize"));
        NavigateCleanupCommand = new RelayCommand(() => SwitchView(CleanupVM, "Cleanup"));
        NavigateStartupCommand = new RelayCommand(() => SwitchView(StartupVM, "Startup"));
        NavigateSettingsCommand = new RelayCommand(() => SwitchView(SettingsVM, "Settings"));
        NavigateLogsCommand = new RelayCommand(() => SwitchView(LogsVM, "Logs"));

        monitor.MetricsUpdated += (s, m) =>
        {
            HeaderRamPercent = m.MemoryLoadPercent;
            HeaderPressure = m.PressureLevel;
            HeaderHealthText = m.PressureLevel switch
            {
                MemoryPressureLevel.Critical => "Critical",
                MemoryPressureLevel.High => "High Pressure",
                MemoryPressureLevel.Warning => "Elevated",
                _ => "Healthy"
            };
        };
    }

    public void SwitchView(ViewModelBase vm, string tabName)
    {
        CurrentViewModel = vm;
        CurrentTab = tabName;

        if (vm is LogsViewModel logsVM)
        {
            logsVM.RefreshLogs();
        }
        else if (vm is StartupViewModel startupVM)
        {
            _ = startupVM.LoadAsync();
        }
    }

    public void OpenQuickOptimize() => SwitchView(QuickOptimizeVM, "QuickOptimize");
    public void OpenDashboard() => SwitchView(DashboardVM, "Dashboard");
}
