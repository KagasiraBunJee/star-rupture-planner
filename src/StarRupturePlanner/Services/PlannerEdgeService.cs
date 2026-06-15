using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class PlannerEdgeService
{
    public static bool IsEdgeValid(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeEdge edge)
    {
        var sourceRecipe = RecipeForNode(catalog, scheme.Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId));
        var targetRecipe = RecipeForNode(catalog, scheme.Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId));
        return calculator.CanConnectOutputToInput(sourceRecipe, targetRecipe, edge.SourceItemId)
            && edge.SourceItemId == edge.TargetItemId;
    }

    public static string EdgeLabel(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        AppSettings settings,
        IPlannerCalculator calculator,
        SchemeEdge edge,
        ProductionAnalysisResult? analysis = null)
    {
        var source = scheme.Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var target = scheme.Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        var sourceRecipe = RecipeForNode(catalog, source);
        var targetRecipe = RecipeForNode(catalog, target);
        if (source is null || target is null || sourceRecipe is null || targetRecipe is null)
        {
            return "Invalid connection";
        }

        var input = targetRecipe.Inputs.FirstOrDefault(item => item.ItemId == edge.TargetItemId);
        if (input is null)
        {
            return "Invalid input";
        }

        var targetMachineCount = ProductionAnalysisService.EffectiveMachineCount(target);
        var requiredRate = calculator.RequiredInputPerMinute(targetRecipe, input, targetMachineCount);
        var deliveredRate = analysis?.EdgeDeliveries.GetValueOrDefault(edge.Id)
            ?? calculator.OutputPerMinute(sourceRecipe, ProductionAnalysisService.EffectiveMachineCount(source));
        var currentTier = CurrentRailTier(catalog, settings);
        var tierText = currentTier is not null
            ? $"{currentTier.Name} {currentTier.ItemsPerMinute:g}/min {(currentTier.ItemsPerMinute >= requiredRate ? "ok" : "over capacity")}"
            : RecommendedTierText(catalog, calculator, requiredRate);
        return $"del {deliveredRate:g}/min  req {requiredRate:g}/min  {tierText}";
    }

    public static RecipeInfo? RecipeForNode(PlannerCatalog catalog, SchemeNode? node)
    {
        if (node?.SelectedRecipeKey is null)
        {
            return null;
        }

        return catalog.Recipes.FirstOrDefault(recipe => recipe.RecipeKey == node.SelectedRecipeKey);
    }

    public static BuildingInfo? BuildingForNode(PlannerCatalog catalog, SchemeNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return catalog.Buildings.FirstOrDefault(building => building.BuildingId == node.BuildingId);
    }

    public static IReadOnlyList<RecipeInfo> RecipesForNode(PlannerCatalog catalog, SchemeNode node)
    {
        var recipes = catalog.Recipes
            .Where(recipe => recipe.BuildingId == node.BuildingId)
            .OrderBy(recipe => recipe.Output.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return recipes.Count > 0 ? recipes : catalog.Recipes;
    }

    public static TransportTierInfo? CurrentRailTier(PlannerCatalog catalog, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.CurrentRailTierId))
        {
            return null;
        }

        return catalog.TransportTiers.Tiers.FirstOrDefault(tier => tier.Id == settings.CurrentRailTierId);
    }

    public static string RecommendedTierText(
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        double requiredRate)
    {
        var tier = calculator.RecommendTransportTier(catalog.TransportTiers.Tiers, requiredRate);
        return tier is null ? "transport tier missing" : $"{tier.Name} {tier.ItemsPerMinute:g}/min";
    }
}
