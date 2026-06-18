using System.Diagnostics;
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
    private string? _lastApiStartupError;
    private string _schemeFolderPath = "";
    private string _lastSavedText = UiText.T("Status.NotSavedYet");
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
        Settings = _settingsStore.Load();
        Settings.ApiPort = AppSettings.NormalizeApiPort(Settings.ApiPort);
        Settings.SchemeFolderPath = NormalizeSchemeFolderPath(Settings.SchemeFolderPath);
        _schemeStore.SetFolder(Settings.SchemeFolderPath);
        _apiClient.ConfigurePort(Settings.ApiPort);
        _apiClient.PlannerLanguage = Settings.PlannerLanguage;
        Toolbox = new ToolboxViewModel(apiClient, uiDispatcher, backgroundTaskRunner);
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

    public string? LastApiStartupError
    {
        get => _lastApiStartupError;
        private set => SetProperty(ref _lastApiStartupError, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _startupCancellation?.Cancel();
        _startupCancellation?.Dispose();
        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _startupCancellation.Token;

        try
        {
            LastApiStartupError = null;
            SetStatus(UiText.T("Status.StartingApi"));
            _apiClient.ConfigurePort(Settings.ApiPort);
            _apiClient.PlannerLanguage = Settings.PlannerLanguage;
            var apiStatus = await _apiProcessManager.EnsureStartedAsync(token);
            SaveResolvedApiPortIfChanged();
            var catalog = await _apiClient.GetCatalogAsync(token);
            await _uiDispatcher.InvokeAsync(() => Catalog = catalog, token);
            await Toolbox.SetCatalogAsync(catalog, token);
            SetStatus($"{apiStatus} {UiText.Format("Status.LoadedRecipes", catalog.Recipes.Count)}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastApiStartupError = UiText.Format("Status.ApiStartupFailed", ex.Message);
            SetStatus(UiText.Format("Status.ApiStartupFailed", ex.Message));
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
        RefreshLocalizedText();
        SetStatus(UiText.T("Status.NewEmptyScheme"));
    }

    public void RefreshLocalizedText()
    {
        LastSavedText = string.IsNullOrWhiteSpace(Scheme.FilePath)
            ? UiText.T("Status.NotSavedYet")
            : FormatLastSaved(Scheme.FilePath);
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
            SetStatus(UiText.Format("Status.Opened", item.Name));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(UiText.Format("Status.CouldNotOpenScheme", ex.Message));
        }
    }

    public async Task SaveSchemeAsync(CancellationToken cancellationToken = default)
    {
        var scheme = Scheme;
        var path = await _backgroundTaskRunner.RunAsync(() => _schemeStore.Save(scheme), cancellationToken);
        LastSavedText = FormatLastSaved(path);
        await RefreshSchemeListAsync(cancellationToken);
        SetStatus(UiText.Format("Status.Saved", System.IO.Path.GetFileName(path)));
    }

    public async Task<bool> DeleteSchemeAsync(SchemeListItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            await _backgroundTaskRunner.RunAsync(() => _schemeStore.Delete(item.FilePath), cancellationToken);
            await RefreshSchemeListAsync(cancellationToken);
            SetStatus(UiText.Format("Status.Deleted", item.Name));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(UiText.Format("Status.CouldNotDeleteScheme", ex.Message));
            return false;
        }
    }

    public bool SchemeFileNameExists(string filePath) => _schemeStore.SchemeFileNameExists(filePath);

    public async Task<SchemeListItem?> AddSchemeFileAsync(
        string filePath,
        SchemeImportMode importMode = SchemeImportMode.KeepBoth,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _backgroundTaskRunner.RunAsync(() => _schemeStore.ImportSchemeFile(filePath, importMode), cancellationToken);
            await RefreshSchemeListAsync(cancellationToken);
            SetStatus(UiText.Format("Status.AddedScheme", item.Name));
            return item;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(UiText.Format("Status.CouldNotAddScheme", ex.Message));
            return null;
        }
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindowViewModel] Failed to inspect last saved time: {ex.Message}");
            when = DateTime.Now;
        }

        var day = when.Date == DateTime.Today
            ? UiText.T("Status.Today")
            : when.Date == DateTime.Today.AddDays(-1) ? UiText.T("Status.Yesterday") : when.ToString("MMM d");
        return UiText.Format("Status.LastSaved", day, when.ToString("t"));
    }

    public void SetSchemeFolder(string folderPath)
    {
        _schemeStore.SetFolder(folderPath);
        SchemeFolderPath = _schemeStore.FolderPath;
        Settings.SchemeFolderPath = _schemeStore.FolderPath;
    }

    public void SaveSettings(AppSettings settings)
    {
        var previousPort = AppSettings.NormalizeApiPort(Settings.ApiPort);
        settings.PlannerLanguage = PlannerLanguages.Normalize(settings.PlannerLanguage);
        settings.ApiPort = AppSettings.NormalizeApiPort(settings.ApiPort);
        settings.SchemeFolderPath = NormalizeSchemeFolderPath(settings.SchemeFolderPath);
        Settings = settings;
        if (previousPort != Settings.ApiPort)
        {
            _apiProcessManager.StopStartedProcess();
        }

        _schemeStore.SetFolder(Settings.SchemeFolderPath);
        SchemeFolderPath = _schemeStore.FolderPath;
        _apiClient.ConfigurePort(Settings.ApiPort);
        _apiClient.PlannerLanguage = Settings.PlannerLanguage;
        _settingsStore.Save(Settings);
        SetStatus(UiText.T("Status.SettingsSaved"));
    }

    public async Task ReloadCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LastApiStartupError = null;
            SetStatus(UiText.T("Status.ReloadingLanguage"));
            _apiClient.ConfigurePort(Settings.ApiPort);
            _apiClient.PlannerLanguage = Settings.PlannerLanguage;
            await _apiProcessManager.EnsureStartedAsync(cancellationToken);
            SaveResolvedApiPortIfChanged();
            var catalog = await _apiClient.GetCatalogAsync(cancellationToken);
            await _uiDispatcher.InvokeAsync(() => Catalog = catalog, cancellationToken);
            await Toolbox.SetCatalogAsync(catalog, cancellationToken);
            SetStatus(UiText.Format("Status.LoadedRecipes", catalog.Recipes.Count));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LastApiStartupError = UiText.Format("Status.ApiStartupFailed", ex.Message);
            SetStatus(UiText.Format("Status.CouldNotReloadLanguage", ex.Message));
        }
    }

    public void SetStatus(string status)
    {
        Status = Catalog.TransportTiers.Missing && !string.IsNullOrWhiteSpace(Catalog.TransportTiers.Message)
            ? $"{status} {Catalog.TransportTiers.Message}"
            : status;
    }

    private void SaveResolvedApiPortIfChanged()
    {
        var resolvedPort = AppSettings.NormalizeApiPort(_apiClient.BaseUri.Port);
        if (Settings.ApiPort == resolvedPort)
        {
            return;
        }

        Settings.ApiPort = resolvedPort;
        _settingsStore.Save(Settings);
    }

    private static string NormalizeSchemeFolderPath(string? folderPath) =>
        string.IsNullOrWhiteSpace(folderPath) ? SchemeStore.DefaultFolderPath() : folderPath.Trim();

    public void Dispose()
    {
        _startupCancellation?.Cancel();
        _startupCancellation?.Dispose();
        _schemeListCancellation?.Cancel();
        _schemeListCancellation?.Dispose();
        _apiProcessManager.Dispose();
    }
}
