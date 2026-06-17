using System.Windows;
using System.Windows.Threading;
using StarRupturePlanner.Services;

namespace StarRupturePlanner;

public partial class App : Application
{
    private readonly IAppLogger _logger = new AppLogger();

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IPlannerApiClient apiClient = new PlannerApiClient();
        IApiProcessManager apiProcessManager = new LocalApiProcessManager(apiClient);
        ISchemeStore schemeStore = new SchemeStore();
        IAppSettingsStore settingsStore = new AppSettingsStore();
        IPlannerCalculator calculator = new PlannerCalculator();
        ICanvasLayoutService layoutService = new CanvasLayoutService();
        IBackgroundTaskRunner backgroundTaskRunner = new BackgroundTaskRunner();
        ISchemeSession session = new SchemeSession();

        MainWindow mainWindow = new(
            apiClient,
            apiProcessManager,
            schemeStore,
            settingsStore,
            calculator,
            layoutService,
            backgroundTaskRunner,
            session);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger.Error("Unhandled application exception.", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
