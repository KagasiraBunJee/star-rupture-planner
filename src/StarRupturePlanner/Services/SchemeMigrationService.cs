using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class SchemeMigrationService
{
    public const int CurrentVersion = 5;

    public static void Migrate(SchemeDocument scheme, PlannerCatalog catalog, IPlannerCalculator calculator)
    {
        PlannerUnlockService.EnsureSchemeDefaults(scheme, catalog);
        foreach (var node in scheme.Nodes)
        {
            var recipe = PlannerEdgeService.RecipeForNode(catalog, node);
            PlannerPortOrderService.NormalizeNodeOrders(node, recipe);
            if (node.SelectedRecipeKey is null)
            {
                node.MachineCount = 0;
                continue;
            }

            if (scheme.Version < CurrentVersion && node.TargetOutputPerMinute > 0)
            {
                node.MachineCount = MachineCountFromLegacyTarget(recipe, calculator, node.TargetOutputPerMinute);
            }
            else if (node.MachineCount <= 0)
            {
                node.MachineCount = MachineCountFromLegacyTarget(recipe, calculator, node.TargetOutputPerMinute);
            }

            node.MachineCount = Math.Max(1, node.MachineCount);
            node.TargetOutputPerMinute = 0;
        }

        scheme.Version = CurrentVersion;
    }

    private static int MachineCountFromLegacyTarget(
        RecipeInfo? recipe,
        IPlannerCalculator calculator,
        double targetOutputPerMinute)
    {
        if (recipe is null || targetOutputPerMinute <= 0)
        {
            return 1;
        }

        var machines = calculator.MachineCount(recipe, targetOutputPerMinute);
        return Math.Max(1, (int)Math.Ceiling(machines));
    }
}
