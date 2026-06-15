using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class PlannerMetricService
{
    public static SchemeMetricTotals CalculateTotals(SchemeDocument scheme, PlannerCatalog catalog)
    {
        double powerConsumption = 0;
        double powerGeneration = 0;
        double temperature = 0;

        foreach (var node in scheme.Nodes)
        {
            var building = PlannerEdgeService.BuildingForNode(catalog, node);
            var machines = ProductionAnalysisService.EffectiveMachineCount(node);
            temperature += NodeTemperature(building, machines);

            if (PlannerEdgeService.RecipeForNode(catalog, node) is null)
            {
                continue;
            }

            var power = NodePower(building, machines);
            if (power < 0)
            {
                powerConsumption += Math.Abs(power);
            }
            else if (power > 0)
            {
                powerGeneration += power;
            }
        }

        return new SchemeMetricTotals(powerConsumption, powerGeneration, temperature);
    }

    public static double NodePower(BuildingInfo? building, int machineCount)
    {
        return building?.Power is double power ? power * EffectivePlacedMachineCount(machineCount) : 0;
    }

    public static double NodeTemperature(BuildingInfo? building, int machineCount)
    {
        return building?.Temperature is double temperature ? temperature * EffectivePlacedMachineCount(machineCount) : 0;
    }

    public static string FormatNodePower(BuildingInfo? building, int machineCount)
    {
        if (building?.Power is not double power)
        {
            return "-";
        }

        var total = power * EffectivePlacedMachineCount(machineCount);
        return total < 0 ? $"{Math.Abs(total):g} kW" : $"+{total:g} kW";
    }

    public static string FormatNodeTemperature(BuildingInfo? building, int machineCount)
    {
        if (building?.Temperature is not double temperature)
        {
            return "-";
        }

        var total = temperature * EffectivePlacedMachineCount(machineCount);
        return total > 0 ? $"+{total:g} temp" : $"{total:g} temp";
    }

    private static int EffectivePlacedMachineCount(int machineCount)
    {
        return Math.Max(1, machineCount);
    }
}

public readonly record struct SchemeMetricTotals(
    double PowerConsumption,
    double PowerGeneration,
    double Temperature);
