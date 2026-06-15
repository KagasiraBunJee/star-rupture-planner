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

        var metrics = ComputeFlow(scheme, catalog, calculator, source, target, sourceRecipe, input, edge, analysis);
        var core = metrics.IsShort
            ? $"{metrics.Delivered:g} of {metrics.Required:g}/min ({metrics.Deficit:g} short)"
            : $"{metrics.Delivered:g}/min, meets demand";

        if (metrics.OverCapacity)
        {
            core = metrics.IsShort
                ? $"{metrics.Delivered:g} of {metrics.Required:g}/min ({metrics.Deficit:g} short), rail over capacity"
                : $"{metrics.Delivered:g}/min, rail over capacity";
        }

        var tierText = metrics.RecommendedTier is null
            ? "transport tier missing"
            : $"{metrics.RecommendedTier.Name} {metrics.RecommendedTier.ItemsPerMinute:g}/min";
        return $"{input.Name} - {core} - {tierText}";
    }

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

        var metrics = ComputeFlow(scheme, catalog, calculator, source, target, sourceRecipe, input, edge, analysis);
        var throughputStatus = metrics.IsShort ? $"{metrics.Deficit:g}/min short" : "meets demand";
        var lines = new List<string>
        {
            $"Item: {input.Name}",
            $"From: {sourceRecipe.BuildingName} -> {targetRecipe.BuildingName}",
            $"Throughput: {metrics.Delivered:g} / {metrics.Required:g} /min - {throughputStatus}",
        };

        if (metrics.RecommendedTier is not null)
        {
            var capStatus = metrics.OverCapacity ? "over capacity" : "OK";
            lines.Add($"Transport: {metrics.RecommendedTier.Name} - {metrics.RecommendedTier.ItemsPerMinute:g}/min capacity ({capStatus})");
        }
        else
        {
            var maxAvailable = PlannerUnlockService.MaxAvailableRailTier(catalog, scheme);
            lines.Add(maxAvailable is null
                ? "Transport: no rail tier available"
                : $"Transport: exceeds available rails - max {maxAvailable.Name} ({maxAvailable.ItemsPerMinute:g}/min)");
        }

        return string.Join("\n", lines);
    }

    private static FlowMetrics ComputeFlow(
        SchemeDocument scheme,
        PlannerCatalog catalog,
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
        var availableTiers = PlannerUnlockService.AvailableRailTiers(catalog, scheme).ToList();
        var recommendedTier = calculator.RecommendTransportTier(availableTiers, required);
        var isShort = delivered + epsilon < required;
        var overCapacity = recommendedTier is null && availableTiers.Count > 0;
        return new FlowMetrics(required, delivered, Math.Max(0, required - delivered), isShort, overCapacity, recommendedTier);
    }

    private readonly record struct FlowMetrics(
        double Required,
        double Delivered,
        double Deficit,
        bool IsShort,
        bool OverCapacity,
        TransportTierInfo? RecommendedTier);

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

    public static string RecommendedTierText(
        PlannerCatalog catalog,
        SchemeDocument scheme,
        IPlannerCalculator calculator,
        double requiredRate)
    {
        var tier = calculator.RecommendTransportTier(
            PlannerUnlockService.AvailableRailTiers(catalog, scheme),
            requiredRate);
        return tier is null ? "transport tier missing" : $"{tier.Name} {tier.ItemsPerMinute:g}/min";
    }
}
