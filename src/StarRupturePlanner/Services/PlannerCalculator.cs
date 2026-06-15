using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public sealed class PlannerCalculator : IPlannerCalculator
{
    public double DefaultTargetOutput(RecipeInfo? recipe) => PlannerCalculations.DefaultTargetOutput(recipe);

    public double MachineCount(RecipeInfo? recipe, double targetOutputPerMinute)
    {
        return PlannerCalculations.MachineCount(recipe, targetOutputPerMinute);
    }

    public double OutputPerMinute(RecipeInfo? recipe, int machineCount)
    {
        return PlannerCalculations.OutputPerMinute(recipe, machineCount);
    }

    public double RequiredInputPerMinute(RecipeInfo recipe, RecipePortInfo input, double targetOutputPerMinute)
    {
        return PlannerCalculations.RequiredInputPerMinute(recipe, input, targetOutputPerMinute);
    }

    public double RequiredInputPerMinute(RecipeInfo recipe, RecipePortInfo input, int machineCount)
    {
        return PlannerCalculations.RequiredInputPerMinute(recipe, input, machineCount);
    }

    public bool CanConnectOutputToInput(RecipeInfo? sourceRecipe, RecipeInfo? targetRecipe, string itemId)
    {
        return PlannerCalculations.CanConnectOutputToInput(sourceRecipe, targetRecipe, itemId);
    }

    public TransportTierInfo? RecommendTransportTier(IEnumerable<TransportTierInfo> tiers, double requiredPerMinute)
    {
        return PlannerCalculations.RecommendTransportTier(tiers, requiredPerMinute);
    }
}
