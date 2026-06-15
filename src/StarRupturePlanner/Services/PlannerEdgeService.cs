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

        var metrics = ComputeFlow(catalog, settings, calculator, source, target, sourceRecipe, input, edge, analysis);

        // Compact, natural-language label for the connection line.
        var core = metrics.IsShort
            ? $"{metrics.Delivered:g} of {metrics.Required:g}/min ({metrics.Deficit:g} short)"
            : $"{metrics.Delivered:g}/min, meets demand";
        if (metrics.OverCapacity)
        {
            core = metrics.IsShort
                ? $"{metrics.Delivered:g} of {metrics.Required:g}/min ({metrics.Deficit:g} short), rail over capacity"
                : $"{metrics.Delivered:g}/min, rail over capacity";
        }

        return $"{input.Name} — {core}";
    }

    // Multi-line breakdown shown in the connection inspector.
    public static string EdgeDetail(
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

        var metrics = ComputeFlow(catalog, settings, calculator, source, target, sourceRecipe, input, edge, analysis);

        var throughputStatus = metrics.IsShort ? $"{metrics.Deficit:g}/min short" : "meets demand";
        var lines = new List<string>
        {
            $"Item: {input.Name}",
            $"From: {sourceRecipe.BuildingName}  →  {targetRecipe.BuildingName}",
            $"Throughput: {metrics.Delivered:g} / {metrics.Required:g} /min — {throughputStatus}",
        };

        if (metrics.CurrentTier is not null)
        {
            var capStatus = metrics.OverCapacity ? "over capacity" : "OK";
            lines.Add($"Transport: {metrics.CurrentTier.Name} — {metrics.CurrentTier.ItemsPerMinute:g}/min capacity ({capStatus})");
        }
        else
        {
            var recommended = calculator.RecommendTransportTier(catalog.TransportTiers.Tiers, metrics.Required);
            lines.Add(recommended is null
                ? "Transport: tiers not configured"
                : $"Transport: no rail tier selected — recommended {recommended.Name} ({recommended.ItemsPerMinute:g}/min)");
        }

        return string.Join("\n", lines);
    }

    private static FlowMetrics ComputeFlow(
        PlannerCatalog catalog,
        AppSettings settings,
        IPlannerCalculator calculator,
        SchemeNode source,
        SchemeNode target,
        RecipeInfo sourceRecipe,
        RecipePortInfo input,
        SchemeEdge edge,
        ProductionAnalysisResult? analysis)
    {
        const double epsilon = 0.0001;
        var targetRecipe = RecipeForNode(catalog, target)!;
        var targetMachineCount = ProductionAnalysisService.EffectiveMachineCount(target);
        var required = calculator.RequiredInputPerMinute(targetRecipe, input, targetMachineCount);
        var delivered = analysis?.EdgeDeliveries.GetValueOrDefault(edge.Id)
            ?? calculator.OutputPerMinute(sourceRecipe, ProductionAnalysisService.EffectiveMachineCount(source));
        var currentTier = CurrentRailTier(catalog, settings);
        var isShort = delivered + epsilon < required;
        var overCapacity = currentTier is not null && currentTier.ItemsPerMinute + epsilon < required;
        return new FlowMetrics(required, delivered, Math.Max(0, required - delivered), isShort, overCapacity, currentTier);
    }

    private readonly record struct FlowMetrics(
        double Required,
        double Delivered,
        double Deficit,
        bool IsShort,
        bool OverCapacity,
        TransportTierInfo? CurrentTier);

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
