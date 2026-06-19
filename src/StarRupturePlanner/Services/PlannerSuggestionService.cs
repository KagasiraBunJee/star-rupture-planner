using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class PlannerSuggestionService
{
    private const double Epsilon = 0.0001;

    public static IReadOnlyList<PlannerSuggestionItem> ExistingMachineSuggestions(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        ProductionAnalysisResult analysis,
        IPlannerCalculator calculator,
        string sourceNodeId,
        string sourceDirection,
        string itemId)
    {
        return sourceDirection == "input"
            ? ExistingProducers(scheme, catalog, analysis, calculator, sourceNodeId, itemId)
            : ExistingConsumers(scheme, catalog, analysis, calculator, sourceNodeId, itemId);
    }

    public static double FreeOutputPerMinute(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        ProductionAnalysisResult analysis,
        IPlannerCalculator calculator,
        SchemeNode sourceNode,
        string itemId)
    {
        var maxProduction = MaxOutputPerMinute(catalog, calculator, sourceNode, itemId);
        if (maxProduction <= Epsilon)
        {
            return 0;
        }

        var consumed = scheme.Edges
            .Where(edge => string.Equals(edge.SourceNodeId, sourceNode.Id, StringComparison.Ordinal)
                && string.Equals(edge.SourceItemId, itemId, StringComparison.Ordinal)
                && PlannerEdgeService.IsEdgeValid(scheme, catalog, calculator, edge))
            .Sum(edge => ConnectedConsumption(scheme, catalog, calculator, edge, analysis));
        return Math.Max(0, maxProduction - consumed);
    }

    public static double RequiredInputPerMinute(
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeNode targetNode,
        string itemId)
    {
        var recipe = PlannerEdgeService.RecipeForNode(catalog, targetNode);
        var input = recipe?.Inputs.FirstOrDefault(item => string.Equals(item.ItemId, itemId, StringComparison.Ordinal));
        return recipe is null || input is null
            ? 0
            : calculator.RequiredInputPerMinute(recipe, input, ProductionAnalysisService.EffectiveMachineCount(targetNode));
    }

    private static IReadOnlyList<PlannerSuggestionItem> ExistingProducers(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        ProductionAnalysisResult analysis,
        IPlannerCalculator calculator,
        string targetNodeId,
        string itemId)
    {
        var targetNode = scheme.Nodes.FirstOrDefault(node => string.Equals(node.Id, targetNodeId, StringComparison.Ordinal));
        var required = targetNode is null ? 0 : RequiredInputPerMinute(catalog, calculator, targetNode, itemId);
        return scheme.Nodes
            .Where(node => !string.Equals(node.Id, targetNodeId, StringComparison.Ordinal))
            .Select(node => ExistingProducerSuggestion(scheme, catalog, analysis, calculator, node, targetNodeId, itemId, required))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.FreePerMinute)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static PlannerSuggestionItem? ExistingProducerSuggestion(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        ProductionAnalysisResult analysis,
        IPlannerCalculator calculator,
        SchemeNode sourceNode,
        string targetNodeId,
        string itemId,
        double required)
    {
        var output = PlannerEdgeService.OutputForNode(catalog, sourceNode, itemId);
        if (output is null || HasDuplicateEdge(scheme, sourceNode.Id, targetNodeId, itemId))
        {
            return null;
        }

        if (!IsExistingOutputAvailable(catalog, scheme, sourceNode, itemId))
        {
            return null;
        }

        var maxProduction = output.RatePerMinute;
        var free = FreeOutputPerMinute(scheme, catalog, analysis, calculator, sourceNode, itemId);
        return new PlannerSuggestionItem
        {
            Kind = PlannerSuggestionItemKind.ExistingMachine,
            ExistingNodeId = sourceNode.Id,
            ExistingPortDirection = "output",
            ItemId = itemId,
            ImageUrl = ExistingNodeImageUrl(catalog, sourceNode, itemId),
            Title = PlannerEdgeService.SourceNodeName(catalog, sourceNode),
            Subtitle = FormatItemRate(output.Name, maxProduction),
            SuggestedMaterialName = output.Name,
            SuggestedMaterialRateText = FormatRateText(maxProduction),
            Detail = $"{UiText.Format("Text.FreeProduction", free)}  {UiText.Format("Text.ConsumesProduction", required)}",
            MaxProductionPerMinute = maxProduction,
            FreePerMinute = free,
            RequiredPerMinute = required,
            ConsumptionPerMinute = required,
            HasShortageRisk = required > free + Epsilon,
        };
    }

    private static IReadOnlyList<PlannerSuggestionItem> ExistingConsumers(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        ProductionAnalysisResult analysis,
        IPlannerCalculator calculator,
        string sourceNodeId,
        string itemId)
    {
        var sourceNode = scheme.Nodes.FirstOrDefault(node => string.Equals(node.Id, sourceNodeId, StringComparison.Ordinal));
        var sourceFree = sourceNode is null ? 0 : FreeOutputPerMinute(scheme, catalog, analysis, calculator, sourceNode, itemId);
        return scheme.Nodes
            .Where(node => !string.Equals(node.Id, sourceNodeId, StringComparison.Ordinal))
            .Select(node => ExistingConsumerSuggestion(scheme, catalog, calculator, node, sourceNodeId, itemId, sourceFree))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderByDescending(item => item.RequiredPerMinute)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static PlannerSuggestionItem? ExistingConsumerSuggestion(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeNode targetNode,
        string sourceNodeId,
        string itemId,
        double sourceFree)
    {
        if (targetNode.OnlyOutput || HasDuplicateEdge(scheme, sourceNodeId, targetNode.Id, itemId))
        {
            return null;
        }

        var recipe = PlannerEdgeService.RecipeForNode(catalog, targetNode);
        var input = recipe?.Inputs.FirstOrDefault(item => string.Equals(item.ItemId, itemId, StringComparison.Ordinal));
        if (recipe is null || input is null || !PlannerUnlockService.IsBuildingUnlocked(catalog, scheme, recipe.BuildingId))
        {
            return null;
        }

        var required = calculator.RequiredInputPerMinute(
            recipe,
            input,
            ProductionAnalysisService.EffectiveMachineCount(targetNode));
        return new PlannerSuggestionItem
        {
            Kind = PlannerSuggestionItemKind.ExistingMachine,
            ExistingNodeId = targetNode.Id,
            ExistingPortDirection = "input",
            ItemId = itemId,
            ImageUrl = recipe.BuildingImageUrl ?? "",
            Title = recipe.BuildingName,
            Subtitle = FormatItemRate(input.Name, required),
            SuggestedMaterialName = input.Name,
            SuggestedMaterialRateText = FormatRateText(required),
            Detail = FormatItemRate(recipe.Output.Name, recipe.Output.QuantityPerMinute),
            MaxProductionPerMinute = recipe.Output.QuantityPerMinute,
            FreePerMinute = sourceFree,
            RequiredPerMinute = required,
            ConsumptionPerMinute = required,
            HasShortageRisk = required > sourceFree + Epsilon,
        };
    }

    private static double ConnectedConsumption(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeEdge edge,
        ProductionAnalysisResult analysis)
    {
        var targetNode = scheme.Nodes.FirstOrDefault(node => string.Equals(node.Id, edge.TargetNodeId, StringComparison.Ordinal));
        if (targetNode is null)
        {
            return 0;
        }

        var required = RequiredInputPerMinute(catalog, calculator, targetNode, edge.TargetItemId);
        var delivered = analysis.EdgeDeliveries.GetValueOrDefault(edge.Id);
        return Math.Min(required, delivered > Epsilon ? delivered : required);
    }

    private static double MaxOutputPerMinute(
        PlannerCatalog catalog,
        IPlannerCalculator calculator,
        SchemeNode sourceNode,
        string itemId)
    {
        if (sourceNode.NodeType == SchemeNodeType.BlueprintSource)
        {
            return sourceNode.BlueprintOutputs
                .Where(output => string.Equals(output.ItemId, itemId, StringComparison.Ordinal))
                .Sum(output => output.RatePerMinute);
        }

        var recipe = PlannerEdgeService.RecipeForNode(catalog, sourceNode);
        return recipe is not null && string.Equals(recipe.Output.ItemId, itemId, StringComparison.Ordinal)
            ? calculator.OutputPerMinute(recipe, ProductionAnalysisService.EffectiveMachineCount(sourceNode))
            : 0;
    }

    private static bool IsExistingOutputAvailable(
        PlannerCatalog catalog,
        SchemeDocument scheme,
        SchemeNode sourceNode,
        string itemId)
    {
        if (sourceNode.NodeType == SchemeNodeType.BlueprintSource)
        {
            return sourceNode.BlueprintOutputs.Any(output => string.Equals(output.ItemId, itemId, StringComparison.Ordinal));
        }

        var recipe = PlannerEdgeService.RecipeForNode(catalog, sourceNode);
        return recipe is not null
            && string.Equals(recipe.Output.ItemId, itemId, StringComparison.Ordinal)
            && PlannerUnlockService.IsBuildingUnlocked(catalog, scheme, recipe.BuildingId);
    }

    private static string ExistingNodeImageUrl(PlannerCatalog catalog, SchemeNode sourceNode, string itemId)
    {
        if (sourceNode.NodeType == SchemeNodeType.BlueprintSource)
        {
            return sourceNode.BlueprintOutputs.FirstOrDefault(output => string.Equals(output.ItemId, itemId, StringComparison.Ordinal))?.ImageUrl ?? "";
        }

        var recipe = PlannerEdgeService.RecipeForNode(catalog, sourceNode);
        return recipe?.BuildingImageUrl ?? "";
    }

    private static bool HasDuplicateEdge(SchemeDocument scheme, string sourceNodeId, string targetNodeId, string itemId)
    {
        return scheme.Edges.Any(edge =>
            string.Equals(edge.SourceNodeId, sourceNodeId, StringComparison.Ordinal)
            && string.Equals(edge.TargetNodeId, targetNodeId, StringComparison.Ordinal)
            && string.Equals(edge.SourceItemId, itemId, StringComparison.Ordinal));
    }

    public static string FormatInputRates(RecipeInfo recipe) =>
        string.Join("  ", recipe.Inputs.Select(input => FormatItemRate(input.Name, input.QuantityPerMinute)));

    public static string FormatItemRate(string itemName, double ratePerMinute) => $"{itemName} - {FormatRateText(ratePerMinute)}";

    public static string FormatRateText(double ratePerMinute) => $"{ratePerMinute:g}/min";
}
