namespace StarRupturePlanner.Models;

public sealed class ResourceToolboxItem
{
    public RecipeInfo Recipe { get; init; } = new();
    public string ResourceName => Recipe.Output.Name;
    public string ResourceImageUrl { get; init; } = "";
    public string MachineName => Recipe.BuildingName;
    public string MachineImageUrl { get; init; } = "";
    public double SpeedPerMinute => Recipe.Output.QuantityPerMinute;
}

public sealed class MachineToolboxItem
{
    public BuildingInfo Building { get; init; } = new();
    public string Name => Building.Name;
    public string ImageUrl { get; init; } = "";
}
