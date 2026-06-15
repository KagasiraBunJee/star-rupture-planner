using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public static class PlannerUnlockService
{
    private const string TrainingCorporationId = "starting";

    public static void EnsureSchemeDefaults(SchemeDocument scheme, PlannerCatalog catalog)
    {
        foreach (var corporation in catalog.Corporations)
        {
            if (!scheme.CorporationLevels.ContainsKey(corporation.CorporationId))
            {
                scheme.CorporationLevels[corporation.CorporationId] =
                    corporation.CorporationId == TrainingCorporationId
                        ? Math.Min(5, Math.Max(0, corporation.MaxLevel))
                        : 0;
            }
        }

        if (!scheme.CorporationLevels.ContainsKey(TrainingCorporationId))
        {
            scheme.CorporationLevels[TrainingCorporationId] = 5;
        }

        scheme.SelectedRailTierId = null;
    }

    public static bool IsBuildingUnlocked(PlannerCatalog catalog, SchemeDocument scheme, string buildingId)
    {
        if (!catalog.BuildingUnlocks.TryGetValue(buildingId, out var requirements) || requirements.Count == 0)
        {
            return true;
        }

        return requirements.Any(requirement => CorporationLevel(scheme, requirement.CorporationId) >= requirement.Level);
    }

    public static string BuildingUnlockText(PlannerCatalog catalog, string buildingId)
    {
        if (!catalog.BuildingUnlocks.TryGetValue(buildingId, out var requirements) || requirements.Count == 0)
        {
            return "";
        }

        return string.Join(" or ", requirements.Select(requirement =>
            $"{requirement.CorporationName} {UiText.T("Text.Level")} {requirement.Level}"));
    }

    public static IEnumerable<TransportTierInfo> AvailableRailTiers(PlannerCatalog catalog, SchemeDocument scheme)
    {
        return catalog.TransportTiers.Tiers.Where(tier => IsRailTierUnlocked(tier, scheme));
    }

    public static bool IsRailTierUnlocked(TransportTierInfo tier, SchemeDocument scheme)
    {
        if (tier.UnlockRequirements.Count == 0)
        {
            return true;
        }

        return tier.UnlockRequirements.Any(requirement =>
            CorporationLevel(scheme, requirement.CorporationId) >= requirement.Level);
    }

    public static string RailUnlockText(PlannerCatalog catalog, TransportTierInfo tier)
    {
        if (tier.UnlockRequirements.Count == 0)
        {
            return UiText.T("Text.Available");
        }

        return string.Join(" or ", tier.UnlockRequirements.Select(requirement =>
        {
            var corporation = catalog.Corporations.FirstOrDefault(item => item.CorporationId == requirement.CorporationId);
            return $"{corporation?.Name ?? requirement.CorporationId} {UiText.T("Text.Level")} {requirement.Level}";
        }));
    }

    public static TransportTierInfo? MaxAvailableRailTier(PlannerCatalog catalog, SchemeDocument scheme)
    {
        return AvailableRailTiers(catalog, scheme)
            .OrderByDescending(tier => tier.Level)
            .FirstOrDefault();
    }

    public static IReadOnlyList<string> LockedNodeAlerts(PlannerCatalog catalog, SchemeDocument scheme)
    {
        return scheme.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.BuildingId)
                && !IsBuildingUnlocked(catalog, scheme, node.BuildingId))
            .Select(node =>
            {
                var building = PlannerEdgeService.BuildingForNode(catalog, node);
                var recipe = PlannerEdgeService.RecipeForNode(catalog, node);
                var name = recipe?.BuildingName ?? building?.Name ?? node.BuildingId;
                var unlock = BuildingUnlockText(catalog, node.BuildingId);
                return string.IsNullOrWhiteSpace(unlock)
                    ? $"{name} {UiText.T("Text.LockedByCorporationLevels")}"
                    : $"{name} {UiText.T("Text.LockedRequires")} {unlock}";
            })
            .ToList();
    }

    private static int CorporationLevel(SchemeDocument scheme, string corporationId)
    {
        return scheme.CorporationLevels.TryGetValue(corporationId, out var level) ? level : 0;
    }
}
