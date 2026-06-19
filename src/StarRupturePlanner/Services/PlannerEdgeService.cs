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
        var sourceNode = scheme.Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var sourceOutput = OutputForNode(catalog, sourceNode, edge.SourceItemId);
        var targetNode = scheme.Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        var targetRecipe = RecipeForNode(catalog, targetNode);
        if (targetNode?.OnlyOutput == true)
        {
            return false;
        }

        return sourceOutput is not null
            && targetRecipe is not null
            && targetRecipe.Inputs.Any(input => input.ItemId == edge.SourceItemId)
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
        var sourceOutput = OutputForNode(catalog, source, edge.SourceItemId);
        var targetRecipe = RecipeForNode(catalog, target);
        if (target?.OnlyOutput == true)
        {
            return "Invalid connection: target is output-only";
        }

        if (source is null || target is null || sourceOutput is null || targetRecipe is null)
        {
            return UiText.T("Text.InvalidConnection");
        }

        var input = targetRecipe.Inputs.FirstOrDefault(item => item.ItemId == edge.TargetItemId);
        if (input is null)
        {
            return UiText.T("Text.InvalidInput");
        }

        var metrics = ComputeFlow(scheme, catalog, calculator, source, target, sourceOutput, input, edge, analysis);
        var core = metrics.IsShort
            ? $"{metrics.Delivered:g} of {metrics.Required:g}/min ({metrics.Deficit:g} {UiText.T("Text.Short")})"
            : $"{metrics.Delivered:g}/min, {UiText.T("Text.MeetsDemand")}";

        if (metrics.OverCapacity)
        {
            core = metrics.IsShort
                ? $"{metrics.Delivered:g} of {metrics.Required:g}/min ({metrics.Deficit:g} {UiText.T("Text.Short")}), {UiText.T("Text.RailOverCapacity")}"
                : $"{metrics.Delivered:g}/min, {UiText.T("Text.RailOverCapacity")}";
        }

        var tierText = metrics.RecommendedTier is null
            ? UiText.T("Text.TransportTierMissing")
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
        var sourceOutput = OutputForNode(catalog, source, edge.SourceItemId);
        var targetRecipe = RecipeForNode(catalog, target);
        if (target?.OnlyOutput == true)
        {
            return "Invalid connection\nTarget is marked Only output and cannot consume inputs.";
        }

        if (source is null || target is null || sourceOutput is null || targetRecipe is null)
        {
            return UiText.T("Text.InvalidConnection");
        }

        var input = targetRecipe.Inputs.FirstOrDefault(item => item.ItemId == edge.TargetItemId);
        if (input is null)
        {
            return UiText.T("Text.InvalidInput");
        }

        var metrics = ComputeFlow(scheme, catalog, calculator, source, target, sourceOutput, input, edge, analysis);
        var throughputStatus = metrics.IsShort ? $"{metrics.Deficit:g}/min {UiText.T("Text.Short")}" : UiText.T("Text.MeetsDemand");
        var lines = new List<string>
        {
            $"{UiText.T("Text.Item")}: {input.Name}",
            $"{UiText.T("Text.From")}: {SourceNodeName(catalog, source)} -> {targetRecipe.BuildingName}",
            $"{UiText.T("Text.Throughput")}: {metrics.Delivered:g} / {metrics.Required:g} /min - {throughputStatus}",
        };

        if (metrics.RecommendedTier is not null)
        {
            var capStatus = metrics.OverCapacity ? UiText.T("Text.OverCapacity") : UiText.T("Text.Ok");
            lines.Add($"{UiText.T("Text.Transport")}: {metrics.RecommendedTier.Name} - {metrics.RecommendedTier.ItemsPerMinute:g}/min capacity ({capStatus})");
        }
        else
        {
            var maxAvailable = PlannerUnlockService.MaxAvailableRailTier(catalog, scheme);
            lines.Add(maxAvailable is null
                ? $"{UiText.T("Text.Transport")}: {UiText.T("Text.NoRailTierAvailable")}"
                : $"{UiText.T("Text.Transport")}: {UiText.T("Text.ExceedsAvailableRails")} - {UiText.T("Text.Max")} {maxAvailable.Name} ({maxAvailable.ItemsPerMinute:g}/min)");
        }

        return string.Join("\n", lines);
    }

    public static IReadOnlyList<SchemeEdge> ConnectedEdgesForPort(
        SchemeDocument scheme,
        string nodeId,
        string direction,
        string itemId)
    {
        return direction == "input"
            ? scheme.Edges
                .Where(edge => edge.TargetNodeId == nodeId && edge.TargetItemId == itemId)
                .ToList()
            : scheme.Edges
                .Where(edge => edge.SourceNodeId == nodeId && edge.SourceItemId == itemId)
                .ToList();
    }

    public static IReadOnlyList<ConnectionMenuItemInfo> ConnectionMenuItemsForPort(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        string nodeId,
        string direction,
        string itemId,
        ProductionAnalysisResult? analysis = null)
    {
        return ConnectedEdgesForPort(scheme, nodeId, direction, itemId)
            .Select(edge => ConnectionMenuItemForEdge(scheme, catalog, calculator, edge, direction, analysis))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private static ConnectionMenuItemInfo? ConnectionMenuItemForEdge(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeEdge edge,
        string portDirection,
        ProductionAnalysisResult? analysis)
    {
        var source = scheme.Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var target = scheme.Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        if (source is null && target is null)
        {
            return null;
        }

        var sourceOutput = OutputForNode(catalog, source, edge.SourceItemId);
        var targetRecipe = RecipeForNode(catalog, target);
        var input = targetRecipe?.Inputs.FirstOrDefault(item => item.ItemId == edge.TargetItemId);
        var itemName = sourceOutput?.Name ?? input?.Name ?? edge.SourceItemId;
        var rate = analysis?.EdgeDeliveries.GetValueOrDefault(edge.Id)
            ?? sourceOutput?.RatePerMinute
            ?? 0;
        var oppositeNode = portDirection == "input" ? source : target;
        var oppositeName = oppositeNode is null
            ? portDirection == "input" ? edge.SourceNodeId : edge.TargetNodeId
            : NodeName(catalog, oppositeNode);

        return new ConnectionMenuItemInfo(
            edge.Id,
            oppositeName,
            itemName,
            rate,
            $"{oppositeName} - {itemName} {rate:g}/min");
    }

    private static FlowMetrics ComputeFlow(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeNode source,
        SchemeNode target,
        SourceOutputInfo sourceOutput,
        RecipePortInfo input,
        SchemeEdge edge,
        ProductionAnalysisResult? analysis)
    {
        const double epsilon = 0.0001;
        var targetRecipe = RecipeForNode(catalog, target)!;
        var targetMachineCount = ProductionAnalysisService.EffectiveMachineCount(target);
        var required = calculator.RequiredInputPerMinute(targetRecipe, input, targetMachineCount);
        var delivered = analysis?.EdgeDeliveries.GetValueOrDefault(edge.Id)
            ?? sourceOutput.RatePerMinute;
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

    public static SourceOutputInfo? OutputForNode(PlannerCatalog catalog, SchemeNode? node, string itemId)
    {
        if (node is null)
        {
            return null;
        }

        if (node.NodeType == SchemeNodeType.BlueprintSource)
        {
            var output = node.BlueprintOutputs.FirstOrDefault(item => item.ItemId == itemId);
            return output is null ? null : new SourceOutputInfo(output.ItemId, output.Name, output.RatePerMinute);
        }

        var recipe = RecipeForNode(catalog, node);
        if (recipe?.Output.ItemId != itemId)
        {
            return null;
        }

        var rate = recipe.Output.QuantityPerMinute * ProductionAnalysisService.EffectiveMachineCount(node);
        return new SourceOutputInfo(recipe.Output.ItemId, recipe.Output.Name, rate);
    }

    public static string SourceNodeName(PlannerCatalog catalog, SchemeNode source)
    {
        return NodeName(catalog, source);
    }

    public static string NodeName(PlannerCatalog catalog, SchemeNode node)
    {
        if (node.NodeType == SchemeNodeType.BlueprintSource)
        {
            return string.IsNullOrWhiteSpace(node.SourceSchemeName) ? "Blueprint source" : node.SourceSchemeName;
        }

        var recipe = RecipeForNode(catalog, node);
        var building = BuildingForNode(catalog, node);
        return recipe?.BuildingName ?? building?.Name ?? node.BuildingId;
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
        return tier is null ? UiText.T("Text.TransportTierMissing") : $"{tier.Name} {tier.ItemsPerMinute:g}/min";
    }
}

public sealed record SourceOutputInfo(
    string ItemId,
    string Name,
    double RatePerMinute);

public sealed record ConnectionMenuItemInfo(
    string EdgeId,
    string OppositeNodeName,
    string ItemName,
    double RatePerMinute,
    string DisplayName);
