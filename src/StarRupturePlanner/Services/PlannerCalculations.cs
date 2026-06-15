using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class PlannerCalculations
{
    public static double DefaultTargetOutput(RecipeInfo? recipe)
    {
        return recipe?.Output.QuantityPerMinute ?? 0;
    }

    public static double MachineCount(RecipeInfo? recipe, double targetOutputPerMinute)
    {
        if (recipe is null || recipe.Output.QuantityPerMinute <= 0 || targetOutputPerMinute <= 0)
        {
            return 0;
        }

        return targetOutputPerMinute / recipe.Output.QuantityPerMinute;
    }

    public static double OutputPerMinute(RecipeInfo? recipe, int machineCount)
    {
        if (recipe is null || machineCount <= 0)
        {
            return 0;
        }

        return recipe.Output.QuantityPerMinute * machineCount;
    }

    public static double RequiredInputPerMinute(RecipeInfo recipe, RecipePortInfo input, double targetOutputPerMinute)
    {
        var machineCount = MachineCount(recipe, targetOutputPerMinute);
        return input.QuantityPerMinute * machineCount;
    }

    public static double RequiredInputPerMinute(RecipeInfo recipe, RecipePortInfo input, int machineCount)
    {
        return machineCount <= 0 ? 0 : input.QuantityPerMinute * machineCount;
    }

    public static bool CanConnectOutputToInput(RecipeInfo? sourceRecipe, RecipeInfo? targetRecipe, string itemId)
    {
        if (sourceRecipe is null || targetRecipe is null || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        return sourceRecipe.Output.ItemId == itemId
            && targetRecipe.Inputs.Any(input => input.ItemId == itemId);
    }

    public static TransportTierInfo? RecommendTransportTier(IEnumerable<TransportTierInfo> tiers, double requiredPerMinute)
    {
        return tiers
            .Where(tier => tier.ItemsPerMinute >= requiredPerMinute)
            .OrderBy(tier => tier.ItemsPerMinute)
            .ThenBy(tier => tier.Level)
            .FirstOrDefault();
    }
}
