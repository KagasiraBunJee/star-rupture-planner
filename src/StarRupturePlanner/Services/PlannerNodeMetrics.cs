using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

/// <summary>
/// Pure, analysis-derived node metrics shared by the canvas (card issues/feed
/// visuals) and the alerts bar (starved-machine count). Extracted from MainWindow
/// so modules compute these from <see cref="ProductionAnalysisResult"/> directly.
/// </summary>
public static class PlannerNodeMetrics
{
    /// <summary>
    /// How much of the node's input demand is delivered (1.0 if it has no inputs),
    /// and whether any required input is short.
    /// </summary>
    public static (double Ratio, bool IsShort) FeedRatio(ProductionAnalysisResult analysis, SchemeNode node)
    {
        var inputs = analysis.Inputs.Values
            .Where(i => string.Equals(i.NodeId, node.Id, StringComparison.Ordinal) && i.RequiredPerMinute > 0)
            .ToList();
        if (inputs.Count == 0)
        {
            return (1.0, false);
        }

        var ratio = inputs.Min(i => Math.Min(1.0, i.DeliveredPerMinute / i.RequiredPerMinute));
        var isShort = inputs.Any(i => analysis.ShortInputs.Contains(
            ProductionInputKey.For(node.Id, i.ItemId)));
        return (ratio, isShort);
    }
}
