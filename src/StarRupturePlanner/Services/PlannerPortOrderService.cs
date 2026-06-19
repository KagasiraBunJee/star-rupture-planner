using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class PlannerPortOrderService
{
    public static IReadOnlyList<RecipePortInfo> OrderedInputs(SchemeNode node, RecipeInfo recipe)
    {
        return OrderedByItemId(recipe.Inputs, node.InputOrder, input => input.ItemId);
    }

    public static IReadOnlyList<RecipePortInfo> OrderedOutputs(SchemeNode node, RecipeInfo recipe)
    {
        return OrderedByItemId([recipe.Output], node.OutputOrder, output => output.ItemId);
    }

    public static IReadOnlyList<BlueprintOutputPort> OrderedBlueprintOutputs(SchemeNode node)
    {
        return OrderedByItemId(node.BlueprintOutputs, node.OutputOrder, output => output.ItemId);
    }

    public static void NormalizeNodeOrders(SchemeNode node, RecipeInfo? recipe)
    {
        var inputIds = recipe?.Inputs.Select(input => input.ItemId) ?? [];
        var outputIds = node.NodeType == SchemeNodeType.BlueprintSource
            ? node.BlueprintOutputs.Select(output => output.ItemId)
            : recipe is null ? [] : [recipe.Output.ItemId];

        node.InputOrder = NormalizeOrder(node.InputOrder, inputIds);
        node.OutputOrder = NormalizeOrder(node.OutputOrder, outputIds);
    }

    public static void MovePort(SchemeNode node, string direction, string itemId, int targetIndex, IReadOnlyList<string> visibleItemIds)
    {
        var normalized = NormalizeOrder(direction == "input" ? node.InputOrder : node.OutputOrder, visibleItemIds);
        var currentIndex = normalized.FindIndex(id => string.Equals(id, itemId, StringComparison.Ordinal));
        if (currentIndex < 0)
        {
            return;
        }

        var clampedTarget = Math.Clamp(targetIndex, 0, normalized.Count - 1);
        if (currentIndex == clampedTarget)
        {
            SetOrder(node, direction, normalized);
            return;
        }

        var value = normalized[currentIndex];
        normalized.RemoveAt(currentIndex);
        normalized.Insert(clampedTarget, value);
        SetOrder(node, direction, normalized);
    }

    public static List<string> NormalizeOrder(IEnumerable<string> savedOrder, IEnumerable<string> visibleItemIds)
    {
        var visible = visibleItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var visibleSet = visible.ToHashSet(StringComparer.Ordinal);
        var normalized = savedOrder
            .Where(id => visibleSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var id in visible)
        {
            if (!normalized.Contains(id, StringComparer.Ordinal))
            {
                normalized.Add(id);
            }
        }

        return normalized;
    }

    private static IReadOnlyList<T> OrderedByItemId<T>(
        IReadOnlyList<T> ports,
        IReadOnlyList<string> savedOrder,
        Func<T, string> itemId)
    {
        if (ports.Count <= 1)
        {
            return ports.ToList();
        }

        var byId = ports
            .GroupBy(itemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return NormalizeOrder(savedOrder, ports.Select(itemId))
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
    }

    private static void SetOrder(SchemeNode node, string direction, List<string> order)
    {
        if (direction == "input")
        {
            node.InputOrder = order;
        }
        else
        {
            node.OutputOrder = order;
        }
    }
}
