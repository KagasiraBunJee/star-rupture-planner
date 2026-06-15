using System.Text.Json.Serialization;

namespace StarRupturePlanner.Models;

public sealed class PlannerCatalog
{
    [JsonPropertyName("buildings")]
    public List<BuildingInfo> Buildings { get; set; } = [];

    [JsonPropertyName("recipes")]
    public List<RecipeInfo> Recipes { get; set; } = [];

    [JsonPropertyName("transport_tiers")]
    public TransportTierPayload TransportTiers { get; set; } = new();

    [JsonPropertyName("corporations")]
    public List<CorporationInfo> Corporations { get; set; } = [];

    [JsonPropertyName("building_unlocks")]
    public Dictionary<string, List<BuildingUnlockInfo>> BuildingUnlocks { get; set; } = [];
}

public sealed class BuildingInfo
{
    [JsonPropertyName("building_id")]
    public string BuildingId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("tier")]
    public int? Tier { get; set; }

    [JsonPropertyName("power")]
    public double? Power { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("recipe_count")]
    public int RecipeCount { get; set; }
}

public sealed class RecipeInfo
{
    [JsonPropertyName("recipe_key")]
    public string RecipeKey { get; set; } = "";

    [JsonPropertyName("recipe_id")]
    public string RecipeId { get; set; } = "";

    [JsonPropertyName("recipe_level")]
    public int? RecipeLevel { get; set; }

    [JsonPropertyName("building_id")]
    public string BuildingId { get; set; } = "";

    [JsonPropertyName("building_name")]
    public string BuildingName { get; set; } = "";

    [JsonPropertyName("building_category")]
    public string BuildingCategory { get; set; } = "";

    [JsonPropertyName("building_image_url")]
    public string? BuildingImageUrl { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("output")]
    public RecipePortInfo Output { get; set; } = new();

    [JsonPropertyName("inputs")]
    public List<RecipePortInfo> Inputs { get; set; } = [];

    [JsonPropertyName("unlock_requirements")]
    public List<UnlockRequirementInfo> UnlockRequirements { get; set; } = [];

    [JsonPropertyName("original_rate_text")]
    public string OriginalRateText { get; set; } = "";

    public string DisplayName => $"{BuildingName} - {Output.Name} ({Output.QuantityPerMinute:g}/min)";

    public string InspectorDisplayName => $"{Output.Name} ({Output.QuantityPerMinute:g}/min)";
}

public sealed class RecipePortInfo
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("quantity_per_cycle")]
    public double QuantityPerCycle { get; set; }

    [JsonPropertyName("quantity_per_minute")]
    public double QuantityPerMinute { get; set; }
}

public sealed class UnlockRequirementInfo
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("required_quantity")]
    public double RequiredQuantity { get; set; }
}

public sealed class TransportTierPayload
{
    [JsonPropertyName("tiers")]
    public List<TransportTierInfo> Tiers { get; set; } = [];

    [JsonPropertyName("missing")]
    public bool Missing { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class TransportTierInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("items_per_minute")]
    public double ItemsPerMinute { get; set; }

    [JsonPropertyName("unlock_requirements")]
    public List<CorporationUnlockRequirementInfo> UnlockRequirements { get; set; } = [];

    [JsonPropertyName("unlock_building_ids")]
    public List<string> UnlockBuildingIds { get; set; } = [];
}

public sealed class CorporationInfo
{
    [JsonPropertyName("corporation_id")]
    public string CorporationId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("colour")]
    public string? Colour { get; set; }

    [JsonPropertyName("max_level")]
    public int MaxLevel { get; set; }

    [JsonPropertyName("levels")]
    public List<CorporationLevelInfo> Levels { get; set; } = [];
}

public sealed class CorporationLevelInfo
{
    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("reputation")]
    public int? Reputation { get; set; }

    [JsonPropertyName("building_rewards")]
    public List<CorporationBuildingRewardInfo> BuildingRewards { get; set; } = [];

    [JsonPropertyName("item_rewards")]
    public List<CorporationItemRewardInfo> ItemRewards { get; set; } = [];
}

public sealed class CorporationBuildingRewardInfo
{
    [JsonPropertyName("building_id")]
    public string BuildingId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }
}

public sealed class CorporationItemRewardInfo
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }
}

public sealed class BuildingUnlockInfo
{
    [JsonPropertyName("corporation_id")]
    public string CorporationId { get; set; } = "";

    [JsonPropertyName("corporation_name")]
    public string CorporationName { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }
}

public sealed class CorporationUnlockRequirementInfo
{
    [JsonPropertyName("corporation_id")]
    public string CorporationId { get; set; } = "";

    [JsonPropertyName("level")]
    public int Level { get; set; }
}

public sealed class SuggestionResponse
{
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("suggestions")]
    public List<RecipeInfo> Suggestions { get; set; } = [];
}
