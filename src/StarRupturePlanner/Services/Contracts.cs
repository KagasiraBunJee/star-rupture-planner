using System.Windows;
using StarRupturePlanner.Models;
using Directory = System.IO.Directory;

namespace StarRupturePlanner.Services;

public interface IPlannerApiClient
{
    Uri BaseUri { get; }
    string PlannerLanguage { get; set; }
    void ConfigurePort(int port);
    Task<bool> IsApiAvailableAsync(CancellationToken cancellationToken = default);
    Task<PlannerCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<SuggestionResponse> GetSuggestionsAsync(string direction, string itemId, CancellationToken cancellationToken = default);
    string ToAbsoluteAssetUrl(string? assetUrl);
}

public interface IApiProcessManager : IDisposable
{
    Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default);
    void StopStartedProcess();
}

public interface ISchemeStore
{
    string FolderPath { get; }
    void SetFolder(string folderPath);
    IReadOnlyList<SchemeListItem> ListSchemes();
    SchemeDocument Load(string filePath);
    string Save(SchemeDocument document);
    bool SchemeFileNameExists(string filePath);
    SchemeListItem ImportSchemeFile(string filePath, SchemeImportMode importMode = SchemeImportMode.KeepBoth);
    void Delete(string filePath);
}

public enum SchemeImportMode
{
    KeepBoth,
    Replace,
}

public interface IAppSettingsStore
{
    string FilePath { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface IPlannerCalculator
{
    double DefaultTargetOutput(RecipeInfo? recipe);
    double MachineCount(RecipeInfo? recipe, double targetOutputPerMinute);
    double OutputPerMinute(RecipeInfo? recipe, int machineCount);
    double RequiredInputPerMinute(RecipeInfo recipe, RecipePortInfo input, double targetOutputPerMinute);
    double RequiredInputPerMinute(RecipeInfo recipe, RecipePortInfo input, int machineCount);
    bool CanConnectOutputToInput(RecipeInfo? sourceRecipe, RecipeInfo? targetRecipe, string itemId);
    TransportTierInfo? RecommendTransportTier(IEnumerable<TransportTierInfo> tiers, double requiredPerMinute);
}

public interface ICanvasLayoutService
{
    double GridSize { get; }
    double Snap(double value);
    Point Snap(Point point);
}

public interface IUiDispatcher
{
    bool CheckAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}

public interface IBackgroundTaskRunner
{
    Task RunAsync(Action action, CancellationToken cancellationToken = default);
    Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public abstract class DocumentStoreBase<TDocument, TListItem>
{
    protected DocumentStoreBase(string folderPath)
    {
        FolderPath = folderPath;
        Directory.CreateDirectory(FolderPath);
    }

    public string FolderPath { get; private set; }

    public void SetFolder(string folderPath)
    {
        FolderPath = folderPath;
        Directory.CreateDirectory(FolderPath);
    }

    public abstract IReadOnlyList<TListItem> List();
    public abstract TDocument Load(string filePath);
    public abstract string Save(TDocument document);
}
