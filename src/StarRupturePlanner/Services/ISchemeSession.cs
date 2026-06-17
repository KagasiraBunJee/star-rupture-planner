using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

/// <summary>
/// Shared, observable state that the UI modules (command bar, toolbox, canvas,
/// inspector, alerts bar) read from and write to instead of referencing each
/// other directly. This is the communication backbone that replaces MainWindow's
/// private fields and its manual <c>SyncFromViewModel</c> push model.
/// </summary>
public interface ISchemeSession
{
    SchemeDocument CurrentScheme { get; set; }
    PlannerCatalog CurrentCatalog { get; set; }
    AppSettings CurrentSettings { get; set; }
    ProductionAnalysisResult ProductionAnalysis { get; set; }
    string Status { get; set; }
    double Zoom { get; set; }

    SchemeNode? SelectedNode { get; set; }
    SchemeEdge? SelectedEdge { get; set; }
    SchemeComment? SelectedComment { get; set; }

    /// <summary>Mutable multi-selection sets. Mutate in place, then call <see cref="NotifySelectionChanged"/>.</summary>
    HashSet<string> SelectedNodeIds { get; }
    HashSet<string> SelectedCommentIds { get; }

    event EventHandler? SchemeChanged;
    event EventHandler? CatalogChanged;
    event EventHandler? SettingsChanged;
    event EventHandler? AnalysisChanged;
    event EventHandler? StatusChanged;
    event EventHandler? ZoomChanged;
    event EventHandler? SelectionChanged;
    event EventHandler<FocusNodeRequestedEventArgs>? FocusNodeRequested;

    /// <summary>Raise <see cref="SelectionChanged"/> after the selection sets / single selections change.</summary>
    void NotifySelectionChanged();

    /// <summary>Ask the canvas to bring a node into view (used by the alerts/surplus bar).</summary>
    void RequestFocusNode(string nodeId);
}

public sealed class FocusNodeRequestedEventArgs(string nodeId) : EventArgs
{
    public string NodeId { get; } = nodeId;
}
