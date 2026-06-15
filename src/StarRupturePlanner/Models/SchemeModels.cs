using System.Text.Json.Serialization;

namespace StarRupturePlanner.Models;

public sealed class SchemeDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

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
    [JsonPropertyName("building_id")]
    public string BuildingId { get; set; } = "";

    [JsonPropertyName("selected_recipe_key")]
    public string? SelectedRecipeKey { get; set; }

    [JsonPropertyName("target_output_per_minute")]
    public double TargetOutputPerMinute { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
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
    public override string ToString() => Name;
}
