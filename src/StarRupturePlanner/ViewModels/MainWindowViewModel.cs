using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IApiProcessManager _apiProcessManager;
    private readonly IPlannerApiClient _apiClient;
    private readonly ISchemeStore _schemeStore;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IBackgroundTaskRunner _backgroundTaskRunner;
    private PlannerCatalog _catalog = new();
    private SchemeDocument _scheme = new();
    private AppSettings _settings = new();
    private string _status = "";
    private string _schemeFolderPath = "";
    private string _lastSavedText = "Not saved yet";
    private CancellationTokenSource? _startupCancellation;
    private CancellationTokenSource? _schemeListCancellation;

    public MainWindowViewModel(
        IPlannerApiClient apiClient,
        IApiProcessManager apiProcessManager,
        ISchemeStore schemeStore,
        IAppSettingsStore settingsStore,
        IUiDispatcher uiDispatcher,
        IBackgroundTaskRunner backgroundTaskRunner)
    {
        _apiClient = apiClient;
        _apiProcessManager = apiProcessManager;
        _schemeStore = schemeStore;
        _settingsStore = settingsStore;
        _uiDispatcher = uiDispatcher;
        _backgroundTaskRunner = backgroundTaskRunner;
        Toolbox = new ToolboxViewModel(apiClient, uiDispatcher, backgroundTaskRunner);
        Settings = _settingsStore.Load();
        SchemeFolderPath = _schemeStore.FolderPath;
        NewScheme();
    }

    public ToolboxViewModel Toolbox { get; }

    public PlannerCatalog Catalog
    {
        get => _catalog;
        private set => SetProperty(ref _catalog, value);
    }

    public SchemeDocument Scheme
    {
        get => _scheme;
        private set => SetProperty(ref _scheme, value);
    }

    public AppSettings Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string SchemeFolderPath
    {
        get => _schemeFolderPath;
        private set => SetProperty(ref _schemeFolderPath, value);
    }

    public string LastSavedText
    {
        get => _lastSavedText;
        private set => SetProperty(ref _lastSavedText, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _startupCancellation?.Cancel();
        _startupCancellation?.Dispose();
        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _startupCancellation.Token;

        try
        {
            SetStatus("Starting local API...");
            var apiStatus = await _apiProcessManager.EnsureStartedAsync(token);
            var catalog = await _apiClient.GetCatalogAsync(token);
            await _uiDispatcher.InvokeAsync(() => Catalog = catalog, token);
            await Toolbox.SetCatalogAsync(catalog, token);
            SetStatus($"{apiStatus} Loaded {catalog.Recipes.Count} recipes.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"API startup failed: {ex.Message}");
        }
    }

    public async Task RefreshSchemeListAsync(CancellationToken cancellationToken = default)
    {
        _schemeListCancellation?.Cancel();
        _schemeListCancellation?.Dispose();
        _schemeListCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _schemeListCancellation.Token;
        var schemes = await _backgroundTaskRunner.RunAsync(() => _schemeStore.ListSchemes(), token);
        await _uiDispatcher.InvokeAsync(() => SchemeFolderPath = _schemeStore.FolderPath, token);
        await Toolbox.SetSchemesAsync(schemes, token);
    }

    public void NewScheme()
    {
        Scheme = new SchemeDocument { Name = "Untitled" };
        LastSavedText = "Not saved yet";
        SetStatus("New empty scheme.");
    }

    public async Task OpenSchemeAsync(SchemeListItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await _backgroundTaskRunner.RunAsync(() => _schemeStore.Load(item.FilePath), cancellationToken);
            await _uiDispatcher.InvokeAsync(() =>
            {
                Scheme = loaded;
                LastSavedText = FormatLastSaved(item.FilePath);
            }, cancellationToken);
            SetStatus($"Opened {item.Name}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus($"Could not open scheme: {ex.Message}");
        }
    }

    public async Task SaveSchemeAsync(CancellationToken cancellationToken = default)
    {
        var scheme = Scheme;
        var path = await _backgroundTaskRunner.RunAsync(() => _schemeStore.Save(scheme), cancellationToken);
        LastSavedText = FormatLastSaved(path);
        await RefreshSchemeListAsync(cancellationToken);
        SetStatus($"Saved {System.IO.Path.GetFileName(path)}.");
    }

    private static string FormatLastSaved(string? path)
    {
        DateTime when;
        try
        {
            when = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)
                ? System.IO.File.GetLastWriteTime(path)
                : DateTime.Now;
        }
        catch
        {
            when = DateTime.Now;
        }

        var day = when.Date == DateTime.Today
            ? "Today"
            : when.Date == DateTime.Today.AddDays(-1) ? "Yesterday" : when.ToString("MMM d");
        return $"Last saved: {day}, {when:t}";
    }

    public void SetSchemeFolder(string folderPath)
    {
        _schemeStore.SetFolder(folderPath);
        SchemeFolderPath = _schemeStore.FolderPath;
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(Settings);
        SetStatus("Settings saved.");
    }

    public void SetStatus(string status)
    {
        Status = Catalog.TransportTiers.Missing && !string.IsNullOrWhiteSpace(Catalog.TransportTiers.Message)
            ? $"{status} {Catalog.TransportTiers.Message}"
            : status;
    }

    public void Dispose()
    {
        _startupCancellation?.Cancel();
        _startupCancellation?.Dispose();
        _schemeListCancellation?.Cancel();
        _schemeListCancellation?.Dispose();
        _apiProcessManager.Dispose();
    }
}
