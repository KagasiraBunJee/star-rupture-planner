using System.Text.Json.Serialization;

namespace StarRupturePlanner.Models;

public sealed class SchemeDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 4;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("canvas")]
    public CanvasState Canvas { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<SchemeNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<SchemeEdge> Edges { get; set; } = [];

    [JsonPropertyName("comments")]
    public List<SchemeComment> Comments { get; set; } = [];

    [JsonPropertyName("corporation_levels")]
    public Dictionary<string, int> CorporationLevels { get; set; } = [];

    [JsonPropertyName("selected_rail_tier_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedRailTierId { get; set; }

    [JsonIgnore]
    public string? FilePath { get; set; }
}

public sealed class CanvasState
{
    [JsonPropertyName("offset_x")]
    public double OffsetX { get; set; }

    [JsonPropertyName("offset_y")]
    public double OffsetY { get; set; }

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1;
}

public abstract class SchemeElement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
}

public sealed class SchemeNode : SchemeElement
{
    [JsonPropertyName("node_type")]
    public SchemeNodeType NodeType { get; set; } = SchemeNodeType.Machine;

    [JsonPropertyName("building_id")]
    public string BuildingId { get; set; } = "";

    [JsonPropertyName("selected_recipe_key")]
    public string? SelectedRecipeKey { get; set; }

    [JsonPropertyName("target_output_per_minute")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double TargetOutputPerMinute { get; set; }

    [JsonPropertyName("machine_count")]
    public int MachineCount { get; set; } = 1;

    [JsonPropertyName("priority")]
    public ProductionPriority Priority { get; set; } = ProductionPriority.Mid;

    [JsonPropertyName("only_output")]
    public bool OnlyOutput { get; set; }

    [JsonPropertyName("is_scheme_output")]
    public bool IsSchemeOutput { get; set; }

    [JsonPropertyName("source_scheme_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string SourceSchemeName { get; set; } = "";

    [JsonPropertyName("source_scheme_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string SourceSchemePath { get; set; } = "";

    [JsonPropertyName("blueprint_outputs")]
    public List<BlueprintOutputPort> BlueprintOutputs { get; set; } = [];

    [JsonPropertyName("input_order")]
    public List<string> InputOrder { get; set; } = [];

    [JsonPropertyName("output_order")]
    public List<string> OutputOrder { get; set; } = [];

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public enum SchemeNodeType
{
    Machine = 0,
    BlueprintSource = 1,
}

public sealed class BlueprintOutputPort
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("image_url")]
    public string ImageUrl { get; set; } = "";

    [JsonPropertyName("rate_per_minute")]
    public double RatePerMinute { get; set; }
}

public enum ProductionPriority
{
    Low = 0,
    Mid = 1,
    High = 2,
}

public sealed class SchemeEdge : SchemeElement
{
    [JsonPropertyName("source_node_id")]
    public string SourceNodeId { get; set; } = "";

    [JsonPropertyName("source_item_id")]
    public string SourceItemId { get; set; } = "";

    [JsonPropertyName("target_node_id")]
    public string TargetNodeId { get; set; } = "";

    [JsonPropertyName("target_item_id")]
    public string TargetItemId { get; set; } = "";

    [JsonPropertyName("route_points")]
    public List<RoutePoint> RoutePoints { get; set; } = [];
}

public sealed class SchemeComment : SchemeElement
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "Comment";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 360;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 220;
}

public sealed class RoutePoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class SchemeListItem
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public List<SchemeListOutputItem> Outputs { get; set; } = [];
    public string OutputStatusText => Outputs.Count == 0
        ? "No outputs marked"
        : Outputs.Count == 1 ? "1 scheme output" : $"{Outputs.Count} scheme outputs";
    public override string ToString() => Name;
}

public sealed class SchemeListOutputItem
{
    public string RecipeKey { get; set; } = "";
    public int MachineCount { get; set; } = 1;
    public string ItemName { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public double RatePerMinute { get; set; }
    public string RateText => RatePerMinute > 0 ? $"{RatePerMinute:g}/min" : "";
    public string DisplayName => string.IsNullOrWhiteSpace(RateText) ? ItemName : $"{ItemName} {RateText}";
}
