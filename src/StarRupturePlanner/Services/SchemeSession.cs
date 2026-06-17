using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

/// <inheritdoc />
public sealed class SchemeSession : ISchemeSession
{
    private SchemeDocument _scheme = new();
    private PlannerCatalog _catalog = new();
    private AppSettings _settings = new();
    private ProductionAnalysisResult _analysis = ProductionAnalysisResult.Empty;
    private string _status = "";
    private double _zoom = 1;
    private SchemeNode? _selectedNode;
    private SchemeEdge? _selectedEdge;
    private SchemeComment? _selectedComment;

    public SchemeDocument CurrentScheme
    {
        get => _scheme;
        set => SetRef(ref _scheme, value, SchemeChanged);
    }

    public PlannerCatalog CurrentCatalog
    {
        get => _catalog;
        set => SetRef(ref _catalog, value, CatalogChanged);
    }

    public AppSettings CurrentSettings
    {
        get => _settings;
        set => SetRef(ref _settings, value, SettingsChanged);
    }

    public ProductionAnalysisResult ProductionAnalysis
    {
        get => _analysis;
        set => SetRef(ref _analysis, value, AnalysisChanged);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal))
            {
                return;
            }

            _status = value;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (_zoom.Equals(value))
            {
                return;
            }

            _zoom = value;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SchemeNode? SelectedNode
    {
        get => _selectedNode;
        set => _selectedNode = value;
    }

    public SchemeEdge? SelectedEdge
    {
        get => _selectedEdge;
        set => _selectedEdge = value;
    }

    public SchemeComment? SelectedComment
    {
        get => _selectedComment;
        set => _selectedComment = value;
    }

    public HashSet<string> SelectedNodeIds { get; } = [];

    public HashSet<string> SelectedCommentIds { get; } = [];

    public event EventHandler? SchemeChanged;
    public event EventHandler? CatalogChanged;
    public event EventHandler? SettingsChanged;
    public event EventHandler? AnalysisChanged;
    public event EventHandler? StatusChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<FocusNodeRequestedEventArgs>? FocusNodeRequested;
    public event EventHandler? ResetZoomRequested;

    public void NotifySelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    public void RequestFocusNode(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
        {
            FocusNodeRequested?.Invoke(this, new FocusNodeRequestedEventArgs(nodeId));
        }
    }

    public void RequestResetZoom() => ResetZoomRequested?.Invoke(this, EventArgs.Empty);

    private void SetRef<T>(ref T field, T value, EventHandler? handler)
        where T : class
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }

        field = value;
        handler?.Invoke(this, EventArgs.Empty);
    }
}
