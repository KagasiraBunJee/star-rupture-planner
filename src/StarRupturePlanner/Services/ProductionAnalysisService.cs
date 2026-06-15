using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class ProductionAnalysisService
{
    private const double Epsilon = 0.000001;

    public static ProductionAnalysisResult Analyze(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator)
    {
        var result = new ProductionAnalysisResult();
        var nodeRates = BuildNodeRates(scheme, catalog, calculator);
        foreach (var (nodeId, rates) in nodeRates)
        {
            result.NodeRates[nodeId] = rates;
        }

        var demands = BuildDemands(scheme, catalog, calculator);
        var demandByKey = demands.ToDictionary(demand => demand.Key);
        var validEdges = scheme.Edges
            .Where(edge => PlannerEdgeService.IsEdgeValid(scheme, catalog, calculator, edge))
            .ToList();

        foreach (var sourceGroup in validEdges
                     .GroupBy(edge => (edge.SourceNodeId, edge.SourceItemId))
                     .OrderBy(group => group.Key.SourceNodeId, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.SourceItemId, StringComparer.Ordinal))
        {
            var sourceRate = nodeRates.TryGetValue(sourceGroup.Key.SourceNodeId, out var rates)
                ? rates.OutputsPerMinute.GetValueOrDefault(sourceGroup.Key.SourceItemId, rates.OutputPerMinute)
                : 0;
            AllocateSource(sourceGroup.ToList(), sourceRate, demandByKey, result);
        }

        foreach (var demand in demands)
        {
            var delivered = demand.RequiredPerMinute - demand.RemainingPerMinute;
            result.Inputs[demand.Key] = new ProductionInputAnalysis(
                demand.NodeId,
                demand.ItemId,
                demand.ItemName,
                demand.BuildingName,
                demand.RequiredPerMinute,
                Math.Max(0, delivered),
                demand.Priority);

            if (demand.RemainingPerMinute > Epsilon)
            {
                result.ShortInputs.Add(demand.Key);
                result.Alerts.Add(new ProductionAlert(
                    demand.NodeId,
                    demand.ItemId,
                    $"{demand.ItemName} shortage: {demand.BuildingName} receives {Math.Max(0, delivered):g}/min of {demand.RequiredPerMinute:g}/min (-{demand.RemainingPerMinute:g}/min)"));
            }
        }

        foreach (var edge in validEdges)
        {
            var key = ProductionInputKey.For(edge.TargetNodeId, edge.TargetItemId);
            if (result.ShortInputs.Contains(key))
            {
                result.ShortEdges.Add(edge.Id);
            }
        }

        return result;
    }

    private static Dictionary<string, ProductionNodeRates> BuildNodeRates(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator)
    {
        var rates = new Dictionary<string, ProductionNodeRates>();
        foreach (var node in scheme.Nodes)
        {
            if (node.NodeType == SchemeNodeType.BlueprintSource)
            {
                rates[node.Id] = new ProductionNodeRates(
                    0,
                    [],
                    node.BlueprintOutputs
                        .GroupBy(output => output.ItemId, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Sum(output => output.RatePerMinute)));
                continue;
            }

            var recipe = PlannerEdgeService.RecipeForNode(catalog, node);
            if (recipe is null)
            {
                rates[node.Id] = new ProductionNodeRates(0, []);
                continue;
            }

            var machineCount = EffectiveMachineCount(node);
            rates[node.Id] = new ProductionNodeRates(
                calculator.OutputPerMinute(recipe, machineCount),
                recipe.Inputs.ToDictionary(
                    input => input.ItemId,
                    input => calculator.RequiredInputPerMinute(recipe, input, machineCount)));
        }

        return rates;
    }

    private static List<InputDemand> BuildDemands(
        SchemeDocument scheme,
        PlannerCatalog catalog,
        IPlannerCalculator calculator)
    {
        var demands = new List<InputDemand>();
        foreach (var node in scheme.Nodes)
        {
            var recipe = PlannerEdgeService.RecipeForNode(catalog, node);
            if (recipe is null || node.OnlyOutput)
            {
                continue;
            }

            var machineCount = EffectiveMachineCount(node);
            foreach (var input in recipe.Inputs)
            {
                var required = calculator.RequiredInputPerMinute(recipe, input, machineCount);
                if (required <= Epsilon)
                {
                    continue;
                }

                demands.Add(new InputDemand(
                    ProductionInputKey.For(node.Id, input.ItemId),
                    node.Id,
                    input.ItemId,
                    input.Name,
                    recipe.BuildingName,
                    required,
                    node.Priority));
            }
        }

        return demands;
    }

    private static void AllocateSource(
        IReadOnlyList<SchemeEdge> sourceEdges,
        double availablePerMinute,
        IReadOnlyDictionary<ProductionInputKey, InputDemand> demands,
        ProductionAnalysisResult result)
    {
        if (availablePerMinute <= Epsilon)
        {
            return;
        }

        var remainingSupply = availablePerMinute;
        foreach (var priority in new[] { ProductionPriority.High, ProductionPriority.Mid, ProductionPriority.Low })
        {
            var candidates = sourceEdges
                .Select(edge => (Edge: edge, DemandKey: ProductionInputKey.For(edge.TargetNodeId, edge.TargetItemId)))
                .Where(item => demands.TryGetValue(item.DemandKey, out var demand)
                    && demand.Priority == priority
                    && demand.RemainingPerMinute > Epsilon)
                .ToList();

            while (remainingSupply > Epsilon && candidates.Count > 0)
            {
                var share = remainingSupply / candidates.Count;
                var deliveredThisPass = 0d;
                foreach (var candidate in candidates.ToList())
                {
                    var demand = demands[candidate.DemandKey];
                    var delivered = Math.Min(share, demand.RemainingPerMinute);
                    if (delivered <= Epsilon)
                    {
                        candidates.Remove(candidate);
                        continue;
                    }

                    demand.RemainingPerMinute -= delivered;
                    remainingSupply -= delivered;
                    deliveredThisPass += delivered;
                    result.EdgeDeliveries[candidate.Edge.Id] =
                        result.EdgeDeliveries.GetValueOrDefault(candidate.Edge.Id) + delivered;

                    if (demand.RemainingPerMinute <= Epsilon)
                    {
                        candidates.Remove(candidate);
                    }
                }

                if (deliveredThisPass <= Epsilon)
                {
                    break;
                }
            }
        }
    }

    public static int EffectiveMachineCount(SchemeNode node)
    {
        return Math.Max(1, node.MachineCount);
    }

    private sealed class InputDemand
    {
        public InputDemand(
            ProductionInputKey key,
            string nodeId,
            string itemId,
            string itemName,
            string buildingName,
            double requiredPerMinute,
            ProductionPriority priority)
        {
            Key = key;
            NodeId = nodeId;
            ItemId = itemId;
            ItemName = itemName;
            BuildingName = buildingName;
            RequiredPerMinute = requiredPerMinute;
            RemainingPerMinute = requiredPerMinute;
            Priority = priority;
        }

        public ProductionInputKey Key { get; }
        public string NodeId { get; }
        public string ItemId { get; }
        public string ItemName { get; }
        public string BuildingName { get; }
        public double RequiredPerMinute { get; }
        public double RemainingPerMinute { get; set; }
        public ProductionPriority Priority { get; }
    }
}

public sealed class ProductionAnalysisResult
{
    public Dictionary<string, ProductionNodeRates> NodeRates { get; } = [];
    public Dictionary<ProductionInputKey, ProductionInputAnalysis> Inputs { get; } = [];
    public Dictionary<string, double> EdgeDeliveries { get; } = [];
    public HashSet<ProductionInputKey> ShortInputs { get; } = [];
    public HashSet<string> ShortEdges { get; } = [];
    public List<ProductionAlert> Alerts { get; } = [];

    public static ProductionAnalysisResult Empty { get; } = new();
}

public sealed record ProductionNodeRates(
    double OutputPerMinute,
    Dictionary<string, double> InputsPerMinute,
    Dictionary<string, double> OutputsPerMinute)
{
    public ProductionNodeRates(double outputPerMinute, Dictionary<string, double> inputsPerMinute)
        : this(outputPerMinute, inputsPerMinute, [])
    {
    }
}

public sealed record ProductionInputAnalysis(
    string NodeId,
    string ItemId,
    string ItemName,
    string BuildingName,
    double RequiredPerMinute,
    double DeliveredPerMinute,
    ProductionPriority Priority);

public sealed record ProductionAlert(string NodeId, string ItemId, string Message);

public readonly record struct ProductionInputKey(string NodeId, string ItemId)
{
    public static ProductionInputKey For(string nodeId, string itemId) => new(nodeId, itemId);
}
