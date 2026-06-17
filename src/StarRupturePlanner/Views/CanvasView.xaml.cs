using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using StarRupturePlanner.Controls;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;

namespace StarRupturePlanner.Views;

/// <summary>
/// Center canvas: node-graph rendering, drag, pan/zoom, selection, connection drag and
/// suggestions. Reads scheme/catalog/settings/analysis/selection from the shared
/// <see cref="ISchemeSession"/> (via bridge properties so the rendering code is unchanged),
/// and publishes edits/refreshes back through the session. Split across partial files;
/// this file holds state, wiring and the shell-facing surface.
/// </summary>
public partial class CanvasView : UserControl
{
    private static readonly Color InputPortColor = UiPalette.InputPort;
    private static readonly Color OutputPortColor = UiPalette.OutputPort;
    private static readonly Color SignalGreenColor = UiPalette.SignalGreen;
    private static readonly Color ShortageColor = UiPalette.Shortage;
    private static readonly Color ReactorOrangeColor = UiPalette.ReactorOrange;
    private static readonly Color LockedPortColor = UiPalette.LockedPort;
    private static readonly Color PanelGlassColor = UiPalette.PanelGlass;
    private static readonly Color GraphiteLineColor = UiPalette.GraphiteLine;

    private ISchemeSession _session = null!;
    private IPlannerApiClient _apiClient = null!;
    private IPlannerCalculator _calculator = null!;
    private ICanvasLayoutService _layoutService = null!;
    private PlannerCanvasViewModel _canvasViewModel = null!;

    private readonly Dictionary<string, FrameworkElement> _nodeViews = [];
    private readonly Dictionary<string, EdgeVisual> _edgeViews = [];
    private readonly Dictionary<string, FrameworkElement> _commentViews = [];
    private readonly Dictionary<PortReference, FrameworkElement> _portViews = [];
    private readonly Dictionary<string, EdgeRenderItem> _edgeRenderItems = [];
    private EdgeLayer? _edgeLayer;

    // Shared state is read from / written to the session so all modules stay in sync.
    private SchemeDocument _scheme => _session.CurrentScheme;
    private PlannerCatalog _catalog => _session.CurrentCatalog;
    private AppSettings _settings => _session.CurrentSettings;
    private ProductionAnalysisResult _productionAnalysis
    {
        get => _session.ProductionAnalysis;
        set => _session.ProductionAnalysis = value;
    }

    private SchemeNode? _selectedNode { get => _session.SelectedNode; set => _session.SelectedNode = value; }
    private SchemeEdge? _selectedEdge { get => _session.SelectedEdge; set => _session.SelectedEdge = value; }
    private SchemeComment? _selectedComment { get => _session.SelectedComment; set => _session.SelectedComment = value; }
    private RoutePointReference? _selectedRoutePoint;
    private readonly HashSet<string> _selectedNodeIds = [];
    private readonly HashSet<string> _selectedCommentIds = [];
    private readonly HashSet<RoutePointReference> _selectedRoutePoints = [];

    private SchemeNode? _dragNode;
    private SchemeComment? _dragComment;
    private Point _dragStartMouse;
    private Point _dragStartNode;
    private bool _isPanning;
    private Point _panStartMouse;
    private Point _panStartOffset;
    private RoutePointDrag? _routePointDrag;
    private CommentResizeDrag? _commentResizeDrag;
    private ConnectionDrag? _connectionDrag;
    private CancellationTokenSource? _suggestionCancellation;
    private Point _suggestionCanvasPoint;
    private readonly Dictionary<string, Point> _groupDragNodeStarts = [];
    private readonly Dictionary<string, Point> _groupDragCommentStarts = [];
    private readonly Dictionary<RoutePointReference, Point> _groupDragRoutePointStarts = [];
    private readonly HashSet<string> _groupDragEdgeIds = [];
    private readonly HashSet<string> _pendingDragEdgeIds = [];
    private bool _dragEdgeRefreshScheduled;
    private Rectangle? _selectionRectangle;
    private bool _isSelecting;
    private bool _isCreatingComment;
    private Point _selectionStart;
    private Point _commentStart;

    public CanvasView()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _suggestionCancellation?.Cancel();
            _suggestionCancellation?.Dispose();
            _suggestionCancellation = null;
        };
    }

    public void Initialize(
        ISchemeSession session,
        IPlannerApiClient apiClient,
        IPlannerCalculator calculator,
        ICanvasLayoutService layoutService,
        PlannerCanvasViewModel canvasViewModel)
    {
        _session = session;
        _apiClient = apiClient;
        _calculator = calculator;
        _layoutService = layoutService;
        _canvasViewModel = canvasViewModel;

        GridInputLayer.GridSize = _layoutService.GridSize;
        _session.CanvasRenderRequested += (_, _) => RenderCanvas();
        _session.ResetZoomRequested += (_, _) => ResetZoom();
        _session.FocusNodeRequested += (_, e) => FocusNodeById(e.NodeId);
    }

    // Select and center a node by id (used when an alert/bottleneck chip is clicked).
    private void FocusNodeById(string nodeId)
    {
        var node = _scheme.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null)
        {
            return;
        }

        SelectSingleNode(node);
        UpdateInspector();
        UpdateSelectionVisuals();
        FocusNode(node);
    }

    // ----- Shell-facing surface -----

    public void Render() => RenderCanvas();

    public void ResetView()
    {
        ClearSelection();
        CanvasScale.ScaleX = 1;
        CanvasScale.ScaleY = 1;
        CanvasTranslate.X = 0;
        CanvasTranslate.Y = 0;
        UpdateZoomText();
        RenderCanvas();
    }

    public void LoadView()
    {
        ClearSelection();
        CanvasScale.ScaleX = Math.Clamp(_scheme.Canvas.Zoom, 0.25, 2.5);
        CanvasScale.ScaleY = CanvasScale.ScaleX;
        CanvasTranslate.X = _scheme.Canvas.OffsetX;
        CanvasTranslate.Y = _scheme.Canvas.OffsetY;
        UpdateZoomText();
        RenderCanvas();
    }

    /// <summary>Persist the current viewport transform back into the scheme (called before save).</summary>
    public void CaptureViewState()
    {
        _scheme.Canvas.Zoom = CanvasScale.ScaleX;
        _scheme.Canvas.OffsetX = CanvasTranslate.X;
        _scheme.Canvas.OffsetY = CanvasTranslate.Y;
    }

    // ----- Forwarders so the moved rendering code compiles unchanged -----

    private void UpdateInspector() => _session.NotifySelectionChanged();

    private void SetStatus(string status) => _session.RequestStatus(status);

    private void SetImage(Image image, string? assetUrl) => UiImageLoader.SetImage(_apiClient, image, assetUrl);

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
        => WpfVisualTreeHelpers.FindAncestor<T>(current);

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject ancestor)
        => WpfVisualTreeHelpers.IsDescendantOf(current, ancestor);

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject current)
        where T : DependencyObject
        => WpfVisualTreeHelpers.FindVisualChildren<T>(current);

    private static string SafeFontFamily(string value) => UiBrushHelpers.SafeFontFamily(value);

    private static SolidColorBrush BrushFromString(string? value, string fallback)
        => UiBrushHelpers.BrushFromString(value, fallback);

    private void RunUiAsync(Func<Task> operation, string context)
    {
        _ = RunUiAsyncCore(operation, context);
    }

    private async Task RunUiAsyncCore(Func<Task> operation, string context)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[{context}] Operation canceled.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{context}] {ex}");
            SetStatus(ex.Message);
        }
    }

    // ----- Interaction-state records (were nested in MainWindow) -----

    private sealed record PortReference(string NodeId, string Direction, string ItemId);

    private sealed record ConnectionDrag(PortReference Port, Path Path, Point StartPoint);

    private sealed record RoutePointReference(string EdgeId, int Index);

    private sealed record RoutePointDrag(RoutePointReference Reference, Point StartMouse, Point StartPoint);

    private sealed record CommentResizeDrag(SchemeComment Comment, Point StartMouse, double StartWidth, double StartHeight);

    private sealed class EdgeVisual
    {
        public List<Ellipse> RoutePointHandles { get; } = [];
    }
}
