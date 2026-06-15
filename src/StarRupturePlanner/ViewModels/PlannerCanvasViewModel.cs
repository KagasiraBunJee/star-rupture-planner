using System.Windows;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.ViewModels;

public sealed class PlannerCanvasViewModel : ViewModelBase
{
    private readonly IPlannerCalculator _calculator;
    private readonly ICanvasLayoutService _layoutService;
    private SchemeDocument _scheme = new();
    private PlannerCatalog _catalog = new();
    private AppSettings _settings = new();

    public PlannerCanvasViewModel(IPlannerCalculator calculator, ICanvasLayoutService layoutService)
    {
        _calculator = calculator;
        _layoutService = layoutService;
    }

    public SchemeDocument Scheme
    {
        get => _scheme;
        set => SetProperty(ref _scheme, value);
    }

    public PlannerCatalog Catalog
    {
        get => _catalog;
        set => SetProperty(ref _catalog, value);
    }

    public AppSettings Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }

    public SchemeNode CreateRecipeNode(RecipeInfo recipe, Point position)
    {
        var snapped = _layoutService.Snap(position);
        return new SchemeNode
        {
            BuildingId = recipe.BuildingId,
            SelectedRecipeKey = recipe.RecipeKey,
            MachineCount = 1,
            Priority = ProductionPriority.Mid,
            X = snapped.X,
            Y = snapped.Y,
        };
    }

    public SchemeNode CreateMachineNode(BuildingInfo building, Point position)
    {
        var snapped = _layoutService.Snap(position);
        return new SchemeNode
        {
            BuildingId = building.BuildingId,
            SelectedRecipeKey = null,
            MachineCount = 0,
            Priority = ProductionPriority.Mid,
            X = snapped.X,
            Y = snapped.Y,
        };
    }

    public bool CanConnect(PlannerPortReference first, PlannerPortReference second)
    {
        if (first.NodeId == second.NodeId || first.Direction == second.Direction || first.ItemId != second.ItemId)
        {
            return false;
        }

        var source = first.Direction == "output" ? first : second;
        var target = first.Direction == "input" ? first : second;
        var sourceRecipe = PlannerEdgeService.RecipeForNode(Catalog, Scheme.Nodes.FirstOrDefault(node => node.Id == source.NodeId));
        var targetRecipe = PlannerEdgeService.RecipeForNode(Catalog, Scheme.Nodes.FirstOrDefault(node => node.Id == target.NodeId));
        return _calculator.CanConnectOutputToInput(sourceRecipe, targetRecipe, source.ItemId);
    }

    public bool TryCreateEdge(PlannerPortReference first, PlannerPortReference second, out SchemeEdge? edge)
    {
        edge = null;
        if (!CanConnect(first, second))
        {
            return false;
        }

        var source = first.Direction == "output" ? first : second;
        var target = first.Direction == "input" ? first : second;
        if (Scheme.Edges.Any(existing =>
                existing.SourceNodeId == source.NodeId
                && existing.TargetNodeId == target.NodeId
                && existing.SourceItemId == source.ItemId))
        {
            return false;
        }

        edge = new SchemeEdge
        {
            SourceNodeId = source.NodeId,
            SourceItemId = source.ItemId,
            TargetNodeId = target.NodeId,
            TargetItemId = target.ItemId,
        };
        Scheme.Edges.Add(edge);
        return true;
    }

    public string EdgeLabel(SchemeEdge edge)
    {
        return PlannerEdgeService.EdgeLabel(Scheme, Catalog, Settings, _calculator, edge);
    }
}

public sealed record PlannerPortReference(string NodeId, string Direction, string ItemId);
