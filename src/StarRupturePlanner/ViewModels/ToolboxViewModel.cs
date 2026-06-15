using System.Collections.ObjectModel;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.ViewModels;

public sealed class ToolboxViewModel : ViewModelBase
{
    private readonly IPlannerApiClient _apiClient;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IBackgroundTaskRunner _backgroundTaskRunner;
    private IReadOnlyList<SchemeListItem> _allSchemes = [];
    private IReadOnlyList<ResourceToolboxItem> _allResources = [];
    private IReadOnlyList<MachineToolboxItem> _allMachines = [];
    private CancellationTokenSource? _filterCancellation;
    private string _filterText = "";

    public ToolboxViewModel(
        IPlannerApiClient apiClient,
        IUiDispatcher uiDispatcher,
        IBackgroundTaskRunner backgroundTaskRunner)
    {
        _apiClient = apiClient;
        _uiDispatcher = uiDispatcher;
        _backgroundTaskRunner = backgroundTaskRunner;
    }

    public ObservableCollection<SchemeListItem> Schemes { get; } = [];
    public ObservableCollection<ResourceToolboxItem> Resources { get; } = [];
    public ObservableCollection<MachineToolboxItem> Machines { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                _ = ApplyFilterAsync();
            }
        }
    }

    public async Task SetSchemesAsync(IReadOnlyList<SchemeListItem> schemes, CancellationToken cancellationToken = default)
    {
        _allSchemes = schemes;
        await ApplyFilterAsync(cancellationToken);
    }

    public async Task SetCatalogAsync(PlannerCatalog catalog, CancellationToken cancellationToken = default)
    {
        var resources = await _backgroundTaskRunner.RunAsync(
            () => catalog.Recipes
                .OrderBy(recipe => recipe.Output.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(recipe => recipe.BuildingName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(recipe => recipe.Output.QuantityPerMinute)
                .Select(recipe => new ResourceToolboxItem
                {
                    Recipe = recipe,
                    ResourceImageUrl = _apiClient.ToAbsoluteAssetUrl(recipe.Output.ImageUrl),
                    MachineImageUrl = _apiClient.ToAbsoluteAssetUrl(recipe.BuildingImageUrl),
                })
                .ToList(),
            cancellationToken);

        var machines = await _backgroundTaskRunner.RunAsync(
            () => catalog.Buildings
                .OrderBy(building => building.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(building => new MachineToolboxItem
                {
                    Building = building,
                    ImageUrl = _apiClient.ToAbsoluteAssetUrl(building.ImageUrl),
                })
                .ToList(),
            cancellationToken);

        _allResources = resources;
        _allMachines = machines;
        await ApplyFilterAsync(cancellationToken);
    }

    public async Task ApplyFilterAsync(CancellationToken cancellationToken = default)
    {
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _filterCancellation.Token;
        var query = FilterText.Trim();
        var schemes = _allSchemes.ToList();
        var resources = _allResources.ToList();
        var machines = _allMachines.ToList();

        var result = await _backgroundTaskRunner.RunAsync(
            () => FilterSnapshot(query, schemes, resources, machines),
            token);

        await _uiDispatcher.InvokeAsync(
            () =>
            {
                ReplaceCollection(Schemes, result.Schemes);
                ReplaceCollection(Resources, result.Resources);
                ReplaceCollection(Machines, result.Machines);
            },
            token);
    }

    private static ToolboxFilterResult FilterSnapshot(
        string query,
        IReadOnlyList<SchemeListItem> schemes,
        IReadOnlyList<ResourceToolboxItem> resources,
        IReadOnlyList<MachineToolboxItem> machines)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolboxFilterResult(schemes, resources, machines);
        }

        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return new ToolboxFilterResult(
            schemes.Where(item => item.Name.Contains(query, comparison)).ToList(),
            resources
                .Where(item =>
                    item.ResourceName.Contains(query, comparison)
                    || item.MachineName.Contains(query, comparison))
                .ToList(),
            machines.Where(item => item.Name.Contains(query, comparison)).ToList());
    }

    private sealed record ToolboxFilterResult(
        IReadOnlyList<SchemeListItem> Schemes,
        IReadOnlyList<ResourceToolboxItem> Resources,
        IReadOnlyList<MachineToolboxItem> Machines);
}
