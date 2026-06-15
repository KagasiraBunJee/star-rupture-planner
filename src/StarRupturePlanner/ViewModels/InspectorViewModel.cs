using System.Collections.ObjectModel;
using System.Globalization;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.ViewModels;

public sealed class InspectorViewModel : ViewModelBase
{
    private readonly IPlannerCalculator _calculator;
    private PlannerCatalog _catalog = new();
    private SchemeNode? _selectedNode;
    private RecipeInfo? _selectedRecipe;
    private string _title = "";
    private string _recipeSearchText = "";
    private string _machineCountText = "";
    private string _readOnlyText = "";

    public InspectorViewModel(IPlannerCalculator calculator)
    {
        _calculator = calculator;
    }

    public ObservableCollection<RecipeInfo> Recipes { get; } = [];
    public ObservableCollection<string> Inputs { get; } = [];
    public ObservableCollection<string> Unlocks { get; } = [];

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string RecipeSearchText
    {
        get => _recipeSearchText;
        set
        {
            if (SetProperty(ref _recipeSearchText, value))
            {
                ApplyRecipeFilter(value);
            }
        }
    }

    public string MachineCountText
    {
        get => _machineCountText;
        private set => SetProperty(ref _machineCountText, value);
    }

    public string ReadOnlyText
    {
        get => _readOnlyText;
        private set => SetProperty(ref _readOnlyText, value);
    }

    public void LoadNode(PlannerCatalog catalog, SchemeNode? node)
    {
        _catalog = catalog;
        _selectedNode = node;
        _selectedRecipe = PlannerEdgeService.RecipeForNode(catalog, node);
        var building = PlannerEdgeService.BuildingForNode(catalog, node);
        Title = _selectedRecipe?.BuildingName ?? building?.Name ?? "";
        MachineCountText = node?.MachineCount > 0
            ? node.MachineCount.ToString(CultureInfo.CurrentCulture)
            : "";
        RecipeSearchText = _selectedRecipe?.InspectorDisplayName ?? "";
        ApplyRecipeFilter("");
        RefreshCalculatedValues();
    }

    public void ApplyRecipeFilter(string? query)
    {
        if (_selectedNode is null)
        {
            ReplaceCollection(Recipes, []);
            return;
        }

        var recipes = PlannerEdgeService.RecipesForNode(_catalog, _selectedNode);
        var filtered = string.IsNullOrWhiteSpace(query)
            ? recipes
            : recipes
                .Where(recipe => recipe.Output.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        ReplaceCollection(Recipes, filtered);
    }

    public void RefreshCalculatedValues()
    {
        Inputs.Clear();
        Unlocks.Clear();
        if (_selectedNode is null)
        {
            ReadOnlyText = "";
            return;
        }

        if (_selectedRecipe is null)
        {
            ReadOnlyText = UiText.T("Text.NoRecipeSelected");
            return;
        }

        var machines = ProductionAnalysisService.EffectiveMachineCount(_selectedNode);
        var outputPerMinute = _calculator.OutputPerMinute(_selectedRecipe, machines);
        ReadOnlyText =
            $"{UiText.T("Text.Building")}: {_selectedRecipe.BuildingName}\n" +
            $"{UiText.T("Text.Machines")}: {machines}\n" +
            $"{UiText.T("Text.Output")}: {_selectedRecipe.Output.Name} {outputPerMinute:g}/min\n" +
            $"{UiText.T("Text.RecipeBase")}: {_selectedRecipe.Output.QuantityPerMinute:g}/min ({_selectedRecipe.OriginalRateText})\n" +
            $"{UiText.T("Text.Priority")}: {_selectedNode.Priority}";

        ReplaceCollection(
            Inputs,
            _selectedRecipe.Inputs.Select(input =>
                $"{input.Name}: {_calculator.RequiredInputPerMinute(_selectedRecipe, input, machines):g}/min"));
        ReplaceCollection(
            Unlocks,
            _selectedRecipe.UnlockRequirements.Count == 0
                ? [UiText.T("Text.None")]
                : _selectedRecipe.UnlockRequirements.Select(item => $"{item.Name}: {item.RequiredQuantity:g}"));
    }
}
