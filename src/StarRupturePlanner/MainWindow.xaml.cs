using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;
using Registry = Microsoft.Win32.Registry;

namespace StarRupturePlanner;

public partial class MainWindow : Window
{
    private static readonly Color InputPortColor = Color.FromRgb(10, 132, 255);
    private static readonly Color OutputPortColor = Color.FromRgb(24, 160, 255);
    private static readonly Color SignalGreenColor = Color.FromRgb(99, 214, 77);
    private static readonly Color ShortageColor = Color.FromRgb(255, 72, 72);
    private static readonly Color LockedPortColor = Color.FromRgb(255, 72, 72);
    private static readonly Color PanelGlassColor = Color.FromRgb(16, 24, 32);
    private static readonly Color GraphiteLineColor = Color.FromRgb(38, 52, 61);

    private readonly IPlannerApiClient _apiClient;
    private readonly IApiProcessManager _apiProcessManager;
    private readonly ISchemeStore _schemeStore;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IPlannerCalculator _calculator;
    private readonly ICanvasLayoutService _layoutService;
    private readonly MainWindowViewModel _viewModel;
    private readonly PlannerCanvasViewModel _canvasViewModel;
    private readonly InspectorViewModel _inspectorViewModel;
    private readonly Dictionary<string, FrameworkElement> _nodeViews = [];
    private readonly Dictionary<string, EdgeVisual> _edgeViews = [];
    private readonly Dictionary<string, FrameworkElement> _commentViews = [];

    private PlannerCatalog _catalog = new();
    private SchemeDocument _scheme = new();
    private AppSettings _settings = new();
    private ProductionAnalysisResult _productionAnalysis = ProductionAnalysisResult.Empty;
    private string _lastToolboxUnlockSignature = "";
    private IReadOnlyList<RecipeInfo> _inspectorRecipes = [];
    private string _activeInspectorTab = "Details";
    private SchemeNode? _selectedNode;
    private SchemeEdge? _selectedEdge;
    private SchemeComment? _selectedComment;
    private RoutePointReference? _selectedRoutePoint;
    private readonly HashSet<string> _selectedNodeIds = [];
    private readonly HashSet<string> _selectedCommentIds = [];
    private readonly HashSet<RoutePointReference> _selectedRoutePoints = [];
    private bool _updatingInspector;
    private string _lastStatus = "";

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
    private Point? _resourceDragStart;
    private ResourceToolboxItem? _resourceDragItem;
    private Point? _machineDragStart;
    private MachineToolboxItem? _machineDragItem;
    private readonly Dictionary<string, Point> _groupDragNodeStarts = [];
    private readonly Dictionary<string, Point> _groupDragCommentStarts = [];
    private readonly Dictionary<RoutePointReference, Point> _groupDragRoutePointStarts = [];
    private Rectangle? _selectionRectangle;
    private bool _isSelecting;
    private bool _isCreatingComment;
    private Point _selectionStart;
    private Point _commentStart;

    public MainWindow()
        : this(
            new PlannerApiClient(),
            new SchemeStore(),
            new AppSettingsStore(),
            new PlannerCalculator(),
            new CanvasLayoutService())
    {
    }

    public MainWindow(
        IPlannerApiClient apiClient,
        ISchemeStore schemeStore,
        IAppSettingsStore settingsStore,
        IPlannerCalculator calculator,
        ICanvasLayoutService layoutService,
        IApiProcessManager? apiProcessManager = null)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _apiProcessManager = apiProcessManager ?? new LocalApiProcessManager(_apiClient);
        _schemeStore = schemeStore;
        _settingsStore = settingsStore;
        _calculator = calculator;
        _layoutService = layoutService;
        var uiDispatcher = new WpfUiDispatcher(Dispatcher);
        var backgroundTaskRunner = new BackgroundTaskRunner();
        _viewModel = new MainWindowViewModel(
            _apiClient,
            _apiProcessManager,
            _schemeStore,
            _settingsStore,
            uiDispatcher,
            backgroundTaskRunner);
        _canvasViewModel = new PlannerCanvasViewModel(_calculator, _layoutService);
        _inspectorViewModel = new InspectorViewModel(_calculator);
        DataContext = _viewModel;
        PlannerCanvas.GridSize = _layoutService.GridSize;
        _settings = _viewModel.Settings;
        _scheme = _viewModel.Scheme;
        ApplySettings();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _suggestionCancellation?.Cancel();
            _suggestionCancellation?.Dispose();
            _viewModel.Dispose();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshSchemeListAsync();
        NewScheme();
        await InitializeApiAsync();
    }

    private async Task InitializeApiAsync()
    {
        await _viewModel.InitializeAsync();
        SyncFromViewModel();
        RenderCanvas();
        UpdateInspector();
    }

    private async Task RefreshSchemeListAsync()
    {
        await _viewModel.RefreshSchemeListAsync();
    }

    private void SyncFromViewModel()
    {
        _catalog = _viewModel.Catalog;
        _scheme = _viewModel.Scheme;
        _settings = _viewModel.Settings;
        _canvasViewModel.Catalog = _catalog;
        _canvasViewModel.Scheme = _scheme;
        _canvasViewModel.Settings = _settings;
    }

    private void NewScheme_Click(object sender, RoutedEventArgs e) => NewScheme();

    private void TopSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TopSearchPlaceholder is not null)
        {
            TopSearchPlaceholder.Visibility = string.IsNullOrEmpty(TopSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var maximized = WindowState == WindowState.Maximized;
        MaxRestoreButton.Content = maximized ? "\uE923" : "\uE922";
        MaxRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";

        // A borderless (WindowStyle=None) window overhangs the work area by the
        // resize-border + padded-border thickness on every side when maximized;
        // inset the content uniformly (horizontal metric) to compensate.
        if (maximized)
        {
            var pad = SystemParameters.WindowResizeBorderThickness.Left
                + SystemParameters.WindowNonClientFrameThickness.Left;
            RootGrid.Margin = new Thickness(pad);
        }
        else
        {
            RootGrid.Margin = new Thickness(0);
        }
    }

    private void NewScheme()
    {
        _viewModel.NewScheme();
        SyncFromViewModel();
        ClearSelection();
        CanvasScale.ScaleX = 1;
        CanvasScale.ScaleY = 1;
        CanvasTranslate.X = 0;
        CanvasTranslate.Y = 0;
        UpdateZoomText();
        RenderCanvas();
        UpdateInspector();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose StarRupture scheme folder",
            InitialDirectory = _schemeStore.FolderPath,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetSchemeFolder(dialog.FolderName);
            _ = RefreshSchemeListAsync();
            SetStatus("Scheme folder changed.");
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings)
        {
            Owner = this,
        };
        if (window.ShowDialog() == true)
        {
            _settings = window.Settings;
            _viewModel.SaveSettings(_settings);
            SyncFromViewModel();
            ApplySettings();
            RenderCanvas();
        }
    }

    private void ApplySettings()
    {
        // Per-user font overrides shadow the theme defaults for the window scope.
        Resources["LeftListFontFamily"] = new FontFamily(SafeFontFamily(_settings.LeftBarListFont.Family));
        Resources["LeftListFontSize"] = _settings.LeftBarListFont.Size;
        Resources["LeftListForegroundBrush"] = BrushFromString(_settings.LeftBarListFont.Color, "#F4F0E8");

        ApplyTheme(ResolveTheme(_settings.Theme));
    }

    private static void ApplyTheme(AppTheme theme)
    {
        var dark = theme != AppTheme.Light;
        var themeUri = new Uri($"Themes/{(dark ? "DarkTheme" : "LightTheme")}.xaml", UriKind.Relative);
        var merged = Application.Current.Resources.MergedDictionaries;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)
                || source.Contains("LightTheme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        // Insert the theme ahead of ControlStyles so styles can layer over the tokens.
        merged.Insert(0, new ResourceDictionary { Source = themeUri });
    }

    private async void SchemesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SchemesList.SelectedItem is not SchemeListItem item)
        {
            return;
        }

        await _viewModel.OpenSchemeAsync(item);
        SyncFromViewModel();
        ClearSelection();
        CanvasScale.ScaleX = Math.Clamp(_scheme.Canvas.Zoom, 0.25, 2.5);
        CanvasScale.ScaleY = CanvasScale.ScaleX;
        CanvasTranslate.X = _scheme.Canvas.OffsetX;
        CanvasTranslate.Y = _scheme.Canvas.OffsetY;
        UpdateZoomText();
        RenderCanvas();
        UpdateInspector();
    }

    private async void SaveScheme_Click(object sender, RoutedEventArgs e) => await SaveCurrentSchemeAsync();

    private async Task SaveCurrentSchemeAsync()
    {
        if (_scheme.FilePath is null && _scheme.Name == "Untitled")
        {
            var name = PromptForName("Scheme name", "Untitled");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _scheme.Name = name.Trim();
        }

        _scheme.Canvas.Zoom = CanvasScale.ScaleX;
        _scheme.Canvas.OffsetX = CanvasTranslate.X;
        _scheme.Canvas.OffsetY = CanvasTranslate.Y;
        MigrateAndAnalyzeScheme();
        await _viewModel.SaveSchemeAsync();
        await RefreshSchemeListAsync();
    }

    private void ResourcesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetListItem(ResourcesList, e.OriginalSource as DependencyObject, out ResourceToolboxItem? item))
        {
            return;
        }

        var position = _layoutService.Snap(new Point(260, 180));
        AddRecipeNode(item!.Recipe, position);
    }

    private void ResourcesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginToolboxDrag(ResourcesList, e, out _resourceDragStart, out _resourceDragItem);
    }

    private void ResourcesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ShouldStartToolboxDrag(ResourcesList, e, _resourceDragStart, _resourceDragItem))
        {
            var item = _resourceDragItem;
            ClearResourceToolboxDrag();
            DragDrop.DoDragDrop(ResourcesList, new DataObject(typeof(ResourceToolboxItem), item!), DragDropEffects.Copy);
        }
    }

    private void ResourcesList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ClearResourceToolboxDrag();
    }

    private void MachinesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetListItem(MachinesList, e.OriginalSource as DependencyObject, out MachineToolboxItem? item))
        {
            return;
        }

        var position = _layoutService.Snap(new Point(260, 180));
        AddMachineNode(item!.Building, position);
    }

    private void MachinesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginToolboxDrag(MachinesList, e, out _machineDragStart, out _machineDragItem);
    }

    private void MachinesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ShouldStartToolboxDrag(MachinesList, e, _machineDragStart, _machineDragItem))
        {
            var item = _machineDragItem;
            ClearMachineToolboxDrag();
            DragDrop.DoDragDrop(MachinesList, new DataObject(typeof(MachineToolboxItem), item!), DragDropEffects.Copy);
        }
    }

    private void MachinesList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ClearMachineToolboxDrag();
    }

    private void PlannerCanvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ResourceToolboxItem)) || e.Data.GetDataPresent(typeof(MachineToolboxItem))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlannerCanvas_Drop(object sender, DragEventArgs e)
    {
        var dropPoint = _layoutService.Snap(e.GetPosition(PlannerCanvas));
        if (e.Data.GetData(typeof(ResourceToolboxItem)) is ResourceToolboxItem resourceItem)
        {
            AddRecipeNode(resourceItem.Recipe, dropPoint);
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(MachineToolboxItem)) is MachineToolboxItem machineItem)
        {
            AddMachineNode(machineItem.Building, dropPoint);
            e.Handled = true;
        }
    }

    private void AddRecipeNode(RecipeInfo recipe, Point position)
    {
        var node = CreateNode(recipe, position.X, position.Y);
        _scheme.Nodes.Add(node);
        SelectSingleNode(node);
        RenderCanvas();
        UpdateInspector();
    }

    private void AddMachineNode(BuildingInfo building, Point position)
    {
        var node = _canvasViewModel.CreateMachineNode(building, position);
        _scheme.Nodes.Add(node);
        SelectSingleNode(node);
        RenderCanvas();
        UpdateInspector();
    }

    private SchemeNode CreateNode(RecipeInfo recipe, double x, double y)
    {
        return _canvasViewModel.CreateRecipeNode(recipe, new Point(x, y));
    }

    private void RenderCanvas()
    {
        MigrateAndAnalyzeScheme();
        PlannerCanvas.Children.Clear();
        _nodeViews.Clear();
        _edgeViews.Clear();
        _commentViews.Clear();
        _selectionRectangle = null;

        foreach (var comment in _scheme.Comments)
        {
            AddCommentView(comment);
        }

        foreach (var edge in _scheme.Edges)
        {
            AddEdgeView(edge);
        }

        foreach (var node in _scheme.Nodes)
        {
            AddNodeView(node);
        }

        RefreshEdges();
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                PlannerCanvas.UpdateLayout();
                RefreshEdges();
            }),
            DispatcherPriority.Loaded);
    }

    private void AddCommentView(SchemeComment comment)
    {
        var root = new Border
        {
            Width = Math.Max(140, comment.Width),
            Height = Math.Max(82, comment.Height),
            Background = new SolidColorBrush(Color.FromArgb(72, 10, 30, 42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(190, 34, 83, 113)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(7),
            Tag = comment,
            ClipToBounds = true,
        };
        ApplyCommentSelectionVisual(root, _selectedCommentIds.Contains(comment.Id));

        root.MouseLeftButtonDown += Comment_MouseLeftButtonDown;
        root.MouseMove += Comment_MouseMove;
        root.MouseLeftButtonUp += Comment_MouseLeftButtonUp;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Child = grid;

        var title = new TextBox
        {
            Text = comment.Text,
            Background = new SolidColorBrush(Color.FromArgb(190, 14, 34, 48)),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(230, 246, 255)),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 4, 12, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = comment,
        };
        title.TextChanged += CommentTitle_TextChanged;
        grid.Children.Add(title);

        var body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(48, 10, 18, 24)),
        };
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        var grip = CreateCommentResizeGrip(comment);
        Grid.SetRow(grip, 1);
        grid.Children.Add(grip);

        Canvas.SetLeft(root, comment.X);
        Canvas.SetTop(root, comment.Y);
        Panel.SetZIndex(root, -10);
        PlannerCanvas.Children.Add(root);
        _commentViews[comment.Id] = root;
    }

    private FrameworkElement CreateCommentResizeGrip(SchemeComment comment)
    {
        var grip = new Canvas
        {
            Width = 18,
            Height = 18,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Margin = new Thickness(0, 0, 3, 3),
            Tag = comment,
        };

        for (var index = 0; index < 3; index++)
        {
            var offset = index * 5d;
            var line = new Line
            {
                X1 = 18 - offset,
                Y1 = 18,
                X2 = 18,
                Y2 = 18 - offset,
                Stroke = new SolidColorBrush(Color.FromArgb(210, 225, 225, 225)),
                StrokeThickness = 1.4,
                IsHitTestVisible = false,
            };
            grip.Children.Add(line);
        }

        grip.MouseLeftButtonDown += CommentResizeGrip_MouseLeftButtonDown;
        grip.MouseMove += CommentResizeGrip_MouseMove;
        grip.MouseLeftButtonUp += CommentResizeGrip_MouseLeftButtonUp;
        return grip;
    }

    private void AddNodeView(SchemeNode node)
    {
        var recipe = RecipeForNode(node);
        var building = BuildingForNode(node);
        var root = new Border
        {
            Width = 470,
            MinHeight = 112,
            Background = new LinearGradientBrush(
                Color.FromArgb(242, 17, 27, 35),
                Color.FromArgb(232, 7, 15, 20),
                new Point(0, 0),
                new Point(1, 1)),
            BorderBrush = new SolidColorBrush(GraphiteLineColor),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Tag = node,
        };
        ApplyNodeSelectionVisual(root, _selectedNodeIds.Contains(node.Id));
        if (IsNodeLocked(node) && !_selectedNodeIds.Contains(node.Id))
        {
            root.BorderBrush = new SolidColorBrush(ShortageColor);
            root.BorderThickness = new Thickness(2);
        }

        root.MouseLeftButtonDown += Node_MouseLeftButtonDown;
        root.MouseMove += Node_MouseMove;
        root.MouseLeftButtonUp += Node_MouseLeftButtonUp;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // category accent
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // issues
        root.Child = grid;
        root.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            {
                return;
            }

            grid.Clip = new RectangleGeometry(new Rect(e.NewSize), 8, 8);
        };

        var accentColor = AccentColorForCategory(building?.Category ?? recipe?.BuildingCategory);
        var accent = new Border { Height = 5, Background = new SolidColorBrush(accentColor) };
        Grid.SetRow(accent, 0);
        grid.Children.Add(accent);

        var header = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0),
            Background = new LinearGradientBrush(
                Color.FromArgb(118, accentColor.R, accentColor.G, accentColor.B),
                Color.FromArgb(20, 16, 24, 32),
                new Point(0, 0),
                new Point(1, 0)),
        };
        header.SetValue(Grid.RowProperty, 1);
        var imageFrame = new Border
        {
            Width = 58,
            Height = 58,
            Margin = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromRgb(13, 24, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
        };
        var image = new Image { Width = 50, Height = 50, Stretch = Stretch.Uniform };
        SetImage(image, recipe?.BuildingImageUrl ?? building?.ImageUrl);
        imageFrame.Child = image;
        DockPanel.SetDock(imageFrame, Dock.Left);
        header.Children.Add(imageFrame);
        var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = recipe?.BuildingName ?? building?.Name ?? "Unselected machine",
            Foreground = CardTextBrush(),
            FontSize = CardFontSize(4),
            FontFamily = CardFontFamily(),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = recipe is null
                ? "Recipe not selected"
                : $"{recipe.Output.Name}  {NodeOutputRate(node, recipe):g}/min",
            Foreground = CardTextBrush(0.72),
            FontSize = CardFontSize(),
            FontFamily = CardFontFamily(),
            TextWrapping = TextWrapping.Wrap,
        });
        if (recipe is not null)
        {
            var status = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            status.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(SignalGreenColor),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            status.Children.Add(new TextBlock
            {
                Text = IsNodeLocked(node)
                    ? "Locked by corporations"
                    : $"Count {ProductionAnalysisService.EffectiveMachineCount(node)}  {node.Priority}",
                Foreground = IsNodeLocked(node) ? new SolidColorBrush(ShortageColor) : CardTextBrush(0.75),
                FontSize = CardFontSize(-1),
                FontFamily = CardFontFamily(),
            });
            titlePanel.Children.Add(status);
        }
        header.Children.Add(titlePanel);
        grid.Children.Add(header);

        if (recipe is null)
        {
            var hint = new TextBlock
            {
                Text = "Select a recipe in the inspector to activate ports.",
                Foreground = CardTextBrush(0.72),
                FontFamily = CardFontFamily(),
                FontSize = CardFontSize(),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 18, 16, 18),
            };
            Grid.SetRow(hint, 2);
            grid.Children.Add(hint);
        }
        else
        {
            var body = new Grid { Margin = new Thickness(0), MinHeight = 70 };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });

            var inputs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 10, 8, 12) };
            inputs.Children.Add(CreateCardSectionLabel("INPUTS"));
            foreach (var input in recipe.Inputs)
            {
                inputs.Children.Add(CreatePortVisual(node, input, "input"));
            }

            var divider = new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromArgb(120, 38, 52, 61)),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var output = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 10, 14, 12) };
            output.Children.Add(CreateCardSectionLabel("OUTPUTS"));
            output.Children.Add(CreatePortVisual(node, recipe.Output, "output"));

            Grid.SetColumn(inputs, 0);
            Grid.SetColumn(divider, 1);
            Grid.SetColumn(output, 2);
            body.Children.Add(inputs);
            body.Children.Add(divider);
            body.Children.Add(output);
            Grid.SetRow(body, 2);
            grid.Children.Add(body);

            var footer = CreateCardFooter(node);
            Grid.SetRow(footer, 3);
            grid.Children.Add(footer);

            var issues = CreateCardIssues(node);
            if (issues is not null)
            {
                Grid.SetRow(issues, 4);
                grid.Children.Add(issues);
            }
        }

        Canvas.SetLeft(root, node.X);
        Canvas.SetTop(root, node.Y);
        PlannerCanvas.Children.Add(root);
        _nodeViews[node.Id] = root;
    }

    private static void ApplyNodeSelectionVisual(Border border, bool selected)
    {
        border.BorderBrush = selected
            ? new SolidColorBrush(OutputPortColor)
            : new SolidColorBrush(GraphiteLineColor);
        border.BorderThickness = selected ? new Thickness(2.5) : new Thickness(1.5);
        border.Effect = selected
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = OutputPortColor,
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.58,
            }
            : null;
    }

    private static void ApplyCommentSelectionVisual(Border border, bool selected)
    {
        border.BorderBrush = selected
            ? new SolidColorBrush(OutputPortColor)
            : new SolidColorBrush(Color.FromArgb(170, 95, 105, 112));
        border.BorderThickness = selected ? new Thickness(2.5) : new Thickness(1.5);
        border.Effect = selected
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = OutputPortColor,
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.45,
            }
            : null;
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var (commentId, view) in _commentViews)
        {
            if (view is Border border)
            {
                ApplyCommentSelectionVisual(border, _selectedCommentIds.Contains(commentId));
            }
        }

        foreach (var (nodeId, view) in _nodeViews)
        {
            if (view is Border border)
            {
                ApplyNodeSelectionVisual(border, _selectedNodeIds.Contains(nodeId));
            }
        }

        foreach (var edgeVisual in _edgeViews.Values)
        {
            foreach (var handle in edgeVisual.RoutePointHandles)
            {
                if (handle.Tag is not RoutePointReference reference)
                {
                    continue;
                }

                var selected = _selectedRoutePoints.Contains(reference);
                handle.Width = selected ? 16 : 12;
                handle.Height = selected ? 16 : 12;
                handle.Stroke = selected ? new SolidColorBrush(OutputPortColor) : new SolidColorBrush(Color.FromRgb(5, 12, 17));
                handle.StrokeThickness = selected ? 3 : 1.5;
            }
        }
    }

    private TextBlock CreateCardSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = CardTextBrush(0.58),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-2),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    private static Color AccentColorForCategory(string? category)
    {
        return (category ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "extraction" => Color.FromRgb(0xC8, 0x89, 0x3B),
            "processing" => Color.FromRgb(0x3F, 0xB6, 0xA8),
            "temperature" => Color.FromRgb(0xFF, 0x7A, 0x3C),
            "crafting" => Color.FromRgb(0x9B, 0x6B, 0xE0),
            _ => Color.FromRgb(0x5A, 0x7B, 0x8C),
        };
    }

    // Stub: power has no backing data yet, so the footer shows "—" per design decision.
    private FrameworkElement CreateCardFooter(SchemeNode node)
    {
        var footer = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 38, 52, 61)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 7, 14, 8),
        };

        var dock = new DockPanel { LastChildFill = false };
        var building = BuildingForNode(node);
        var machines = ProductionAnalysisService.EffectiveMachineCount(node);

        var power = new TextBlock
        {
            Text = "⚡ Power  —",
            Foreground = CardTextBrush(0.6),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(power, Dock.Left);
        power.Text = $"Power  {PlannerMetricService.FormatNodePower(building, machines)}";
        dock.Children.Add(power);

        var temperature = new TextBlock
        {
            Text = $"Temp  {PlannerMetricService.FormatNodeTemperature(building, machines)}",
            Foreground = CardTextBrush(0.6),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        DockPanel.SetDock(temperature, Dock.Left);
        dock.Children.Add(temperature);

        var (ratio, isShort) = NodeFeedRatio(node);
        var util = new TextBlock
        {
            Text = $"{ratio * 100:0}%",
            Foreground = isShort ? new SolidColorBrush(ShortageColor) : new SolidColorBrush(SignalGreenColor),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-1),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(util, Dock.Right);
        dock.Children.Add(util);

        footer.Child = dock;
        return footer;
    }

    // Per-machine issue list shown under the card; null when the node has no shortages.
    private FrameworkElement? CreateCardIssues(SchemeNode node)
    {
        var shortInputs = _productionAnalysis.Inputs.Values
            .Where(input => string.Equals(input.NodeId, node.Id, StringComparison.Ordinal)
                && _productionAnalysis.ShortInputs.Contains(ProductionInputKey.For(node.Id, input.ItemId)))
            .OrderByDescending(input => input.RequiredPerMinute - input.DeliveredPerMinute)
            .ToList();
        if (shortInputs.Count == 0)
        {
            return null;
        }

        var container = new StackPanel { Margin = new Thickness(14, 8, 14, 10) };
        container.Children.Add(new TextBlock
        {
            Text = $"ISSUES ({shortInputs.Count})",
            Foreground = new SolidColorBrush(ShortageColor),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-2),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5),
        });

        foreach (var input in shortInputs)
        {
            var deficit = Math.Max(0, input.RequiredPerMinute - input.DeliveredPerMinute);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = "⚠",
                Foreground = new SolidColorBrush(ShortageColor),
                FontFamily = CardFontFamily(),
                FontSize = CardFontSize(-1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = $"{input.ItemName}: {input.DeliveredPerMinute:g}/{input.RequiredPerMinute:g}/min ({deficit:g} short)",
                Foreground = CardTextBrush(0.85),
                FontFamily = CardFontFamily(),
                FontSize = CardFontSize(-1),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            container.Children.Add(row);
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, ShortageColor.R, ShortageColor.G, ShortageColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, ShortageColor.R, ShortageColor.G, ShortageColor.B)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = container,
        };
    }

    // Derived "fed" ratio: how much of the node's input demand is delivered (1.0 if no inputs).
    private (double Ratio, bool IsShort) NodeFeedRatio(SchemeNode node)
    {
        var inputs = _productionAnalysis.Inputs.Values
            .Where(i => string.Equals(i.NodeId, node.Id, StringComparison.Ordinal) && i.RequiredPerMinute > 0)
            .ToList();
        if (inputs.Count == 0)
        {
            return (1.0, false);
        }

        var ratio = inputs.Min(i => Math.Min(1.0, i.DeliveredPerMinute / i.RequiredPerMinute));
        var isShort = inputs.Any(i => _productionAnalysis.ShortInputs.Contains(
            ProductionInputKey.For(node.Id, i.ItemId)));
        return (ratio, isShort);
    }

    private double NodeOutputRate(SchemeNode node, RecipeInfo recipe)
    {
        return _calculator.OutputPerMinute(recipe, ProductionAnalysisService.EffectiveMachineCount(node));
    }

    private double PortRate(SchemeNode node, RecipePortInfo port, string direction)
    {
        var recipe = RecipeForNode(node);
        if (recipe is null)
        {
            return 0;
        }

        var machineCount = ProductionAnalysisService.EffectiveMachineCount(node);
        return direction == "output"
            ? _calculator.OutputPerMinute(recipe, machineCount)
            : _calculator.RequiredInputPerMinute(recipe, port, machineCount);
    }

    private bool IsPortAvailableForConnection(SchemeNode node, RecipePortInfo port, string direction)
    {
        if (IsNodeLocked(node))
        {
            return false;
        }

        return direction == "input"
            ? _catalog.Recipes.Any(recipe =>
                recipe.Output.ItemId == port.ItemId
                && PlannerUnlockService.IsBuildingUnlocked(_catalog, _scheme, recipe.BuildingId))
            : _catalog.Recipes.Any(recipe =>
                recipe.Inputs.Any(input => input.ItemId == port.ItemId)
                && PlannerUnlockService.IsBuildingUnlocked(_catalog, _scheme, recipe.BuildingId));
    }

    private bool IsPortReferenceAvailable(PortReference reference)
    {
        var node = _scheme.Nodes.FirstOrDefault(item => item.Id == reference.NodeId);
        var recipe = RecipeForNode(node);
        if (node is null || recipe is null)
        {
            return false;
        }

        var port = reference.Direction == "input"
            ? recipe.Inputs.FirstOrDefault(input => input.ItemId == reference.ItemId)
            : recipe.Output.ItemId == reference.ItemId ? recipe.Output : null;
        return port is not null && IsPortAvailableForConnection(node, port, reference.Direction);
    }

    private FrameworkElement CreatePortVisual(SchemeNode node, RecipePortInfo port, string direction)
    {
        var rate = PortRate(node, port, direction);
        var available = IsPortAvailableForConnection(node, port, direction);
        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            ToolTip = available
                ? $"{port.Name} {rate:g}/min"
                : $"{port.Name} is not available for connection with current corporation levels.",
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 15,
            Height = 15,
            Fill = available ? PortBrush(direction) : new SolidColorBrush(LockedPortColor),
            Stroke = new SolidColorBrush(Color.FromRgb(5, 12, 17)),
            StrokeThickness = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = available ? Cursors.Hand : Cursors.No,
            Tag = new PortReference(node.Id, direction, port.ItemId),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = available
                    ? direction == "input" ? InputPortColor : OutputPortColor
                    : LockedPortColor,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = available ? 0.65 : 0.75,
            },
        };
        if (available)
        {
            dot.PreviewMouseLeftButtonDown += Port_MouseLeftButtonDown;
        }

        var info = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = direction == "input" ? HorizontalAlignment.Left : HorizontalAlignment.Right,
        };
        var imageFrame = new Border
        {
            Width = 22,
            Height = 22,
            Background = new SolidColorBrush(Color.FromRgb(9, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 45, 55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        var image = new Image { Width = 18, Height = 18, Stretch = Stretch.Uniform };
        SetImage(image, port.ImageUrl);
        imageFrame.Child = image;
        var label = new TextBlock
        {
            Text = $"{port.Name} {rate:g}/min",
            Foreground = available ? CardTextBrush() : new SolidColorBrush(LockedPortColor),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-1),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 152,
        };

        if (direction == "input")
        {
            imageFrame.Margin = new Thickness(7, 0, 5, 0);
            info.Children.Add(imageFrame);
            info.Children.Add(label);
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(info, 1);
        }
        else
        {
            imageFrame.Margin = new Thickness(5, 0, 7, 0);
            info.Children.Add(label);
            info.Children.Add(imageFrame);
            Grid.SetColumn(info, 1);
            Grid.SetColumn(dot, 2);
        }

        row.Children.Add(dot);
        row.Children.Add(info);
        return row;
    }

    private void AddEdgeView(SchemeEdge edge)
    {
        var isValid = IsEdgeValid(edge);
        var isShort = IsEdgeShort(edge);
        var path = new Path
        {
            Stroke = EdgeVisualBrush(edge, isValid),
            StrokeThickness = isValid ? 3.5 : 2.5,
            Tag = edge,
            Effect = isValid && !isShort
                ? new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = OutputPortColor,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                }
                : null,
        };
        path.MouseLeftButtonDown += Edge_MouseLeftButtonDown;
        var hitPath = new Path
        {
            Stroke = Brushes.Transparent,
            StrokeThickness = 16,
            Tag = edge,
            Cursor = Cursors.Hand,
        };
        hitPath.MouseLeftButtonDown += Edge_MouseLeftButtonDown;

        var label = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromArgb(232, 8, 15, 20)),
            Foreground = isValid && !isShort ? CardTextBrush() : new SolidColorBrush(ShortageColor),
            Padding = new Thickness(8, 4, 8, 4),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Tag = edge,
        };
        label.MouseLeftButtonDown += Edge_MouseLeftButtonDown;

        PlannerCanvas.Children.Add(path);
        PlannerCanvas.Children.Add(hitPath);
        PlannerCanvas.Children.Add(label);
        var visual = new EdgeVisual(path, hitPath, label);
        _edgeViews[edge.Id] = visual;

        for (var index = 0; index < edge.RoutePoints.Count; index++)
        {
            var handle = CreateRoutePointHandle(edge, index);
            visual.RoutePointHandles.Add(handle);
            PlannerCanvas.Children.Add(handle);
        }
    }

    private void RefreshEdges()
    {
        MigrateAndAnalyzeScheme();
        foreach (var edge in _scheme.Edges)
        {
            if (!_edgeViews.TryGetValue(edge.Id, out var visual))
            {
                continue;
            }

            var sourcePoint = GetPortPoint(edge.SourceNodeId, "output", edge.SourceItemId);
            var targetPoint = GetPortPoint(edge.TargetNodeId, "input", edge.TargetItemId);
            if (sourcePoint is null || targetPoint is null)
            {
                visual.Path.Data = Geometry.Empty;
                visual.HitPath.Data = Geometry.Empty;
                visual.Label.Text = "Invalid connection";
                continue;
            }

            var routePoints = CanvasGeometryService.EdgePoints(edge, sourcePoint.Value, targetPoint.Value);
            var geometry = CanvasGeometryService.CreateRoutedGeometry(routePoints);
            visual.Path.Data = geometry;
            visual.HitPath.Data = geometry;
            var isValid = IsEdgeValid(edge);
            var isShort = IsEdgeShort(edge);
            visual.Path.Stroke = EdgeVisualBrush(edge, isValid);
            visual.Path.StrokeThickness = isValid ? 3.5 : 2.5;
            visual.Label.Foreground = isValid && !isShort ? CardTextBrush() : new SolidColorBrush(ShortageColor);
            var labelPlacement = CanvasGeometryService.LabelPlacementAboveLine(routePoints);
            Canvas.SetLeft(visual.Label, labelPlacement.Position.X);
            Canvas.SetTop(visual.Label, labelPlacement.Position.Y);
            visual.Label.RenderTransform = new RotateTransform(labelPlacement.AngleDegrees);
            visual.Label.Text = EdgeLabel(edge);

            for (var index = 0; index < visual.RoutePointHandles.Count && index < edge.RoutePoints.Count; index++)
            {
                var point = edge.RoutePoints[index];
                Canvas.SetLeft(visual.RoutePointHandles[index], point.X - visual.RoutePointHandles[index].Width / 2);
                Canvas.SetTop(visual.RoutePointHandles[index], point.Y - visual.RoutePointHandles[index].Height / 2);
            }
        }

        UpdateSelectionVisuals();
    }

    private void MigrateAndAnalyzeScheme()
    {
        if (_catalog.Recipes.Count > 0)
        {
            SchemeMigrationService.Migrate(_scheme, _catalog, _calculator);
        }

        _productionAnalysis = ProductionAnalysisService.Analyze(_scheme, _catalog, _calculator);
        UpdateProductionAlerts();
        RefreshToolboxUnlocksIfNeeded();
    }

    private void RefreshToolboxUnlocksIfNeeded()
    {
        var signature = string.Join(
            "|",
            _scheme.CorporationLevels
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}:{entry.Value}"));
        signature = $"{_catalog.Corporations.Count}:{signature}";
        if (string.Equals(signature, _lastToolboxUnlockSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastToolboxUnlockSignature = signature;
        _ = _viewModel.Toolbox.SetSchemeAsync(_scheme);
    }

    private void UpdateProductionAlerts()
    {
        if (AlertsChips is null)
        {
            return;
        }

        if (MetricMachines is not null)
        {
            var total = _scheme.Nodes.Count(node => RecipeForNode(node) is not null);
            var starved = _scheme.Nodes.Count(node => RecipeForNode(node) is not null && NodeFeedRatio(node).IsShort);
            MetricMachines.Text = total == 0 ? "0" : $"{total - starved}/{total}";
        }

        var totals = PlannerMetricService.CalculateTotals(_scheme, _catalog);
        if (MetricPower is not null)
        {
            MetricPower.Text = totals.PowerGeneration > 0
                ? $"{totals.PowerConsumption:g} kW / +{totals.PowerGeneration:g} kW"
                : $"{totals.PowerConsumption:g} kW";
        }

        if (MetricTemperature is not null)
        {
            MetricTemperature.Text = totals.Temperature > 0
                ? $"+{totals.Temperature:g} temp"
                : $"{totals.Temperature:g} temp";
        }

        AlertsChips.Children.Clear();
        var lockedAlerts = PlannerUnlockService.LockedNodeAlerts(_catalog, _scheme);
        if (_productionAnalysis.Alerts.Count == 0 && lockedAlerts.Count == 0)
        {
            AlertsChips.Children.Add(BuildAlertChip("No production shortages", SignalGreenColor, "✓"));
            return;
        }

        // Show every alert; the row scrolls horizontally to reveal the rest.
        foreach (var alert in _productionAnalysis.Alerts)
        {
            AlertsChips.Children.Add(BuildAlertChip(alert.Message, ShortageColor, "⚠"));
        }

        foreach (var alert in lockedAlerts)
        {
            AlertsChips.Children.Add(BuildAlertChip(alert, ShortageColor, "вљ "));
        }

        AlertsScroller?.ScrollToHorizontalOffset(0);
    }

    private void AlertsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private FrameworkElement BuildAlertChip(string message, Color accent, string glyph)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = message,
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
            MaxWidth = 240,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        border.Child = panel;
        return border;
    }

    private string EdgeLabel(SchemeEdge edge)
    {
        return PlannerEdgeService.EdgeLabel(_scheme, _catalog, _settings, _calculator, edge, _productionAnalysis);
    }

    private string EdgeDetail(SchemeEdge edge)
    {
        return PlannerEdgeService.EdgeDetail(_scheme, _catalog, _settings, _calculator, edge, _productionAnalysis);
    }

    private bool IsEdgeShort(SchemeEdge edge)
    {
        return _productionAnalysis.ShortEdges.Contains(edge.Id);
    }

    private Brush EdgeVisualBrush(SchemeEdge edge, bool isValid)
    {
        if (!isValid || IsEdgeShort(edge))
        {
            return new SolidColorBrush(ShortageColor);
        }

        return EdgeStrokeBrush();
    }

    private bool IsNodeLocked(SchemeNode node)
    {
        return !string.IsNullOrWhiteSpace(node.BuildingId)
            && !PlannerUnlockService.IsBuildingUnlocked(_catalog, _scheme, node.BuildingId);
    }

    private bool IsEdgeValid(SchemeEdge edge)
    {
        return PlannerEdgeService.IsEdgeValid(_scheme, _catalog, _calculator, edge);
    }

    private static Brush PortBrush(string direction)
    {
        return new SolidColorBrush(direction == "input" ? InputPortColor : OutputPortColor);
    }

    private static Brush EdgeStrokeBrush()
    {
        return new LinearGradientBrush(
            OutputPortColor,
            InputPortColor,
            new Point(0, 0),
            new Point(1, 0));
    }

    private Point? GetPortPoint(string nodeId, string direction, string itemId)
    {
        if (!_nodeViews.TryGetValue(nodeId, out var nodeView))
        {
            return null;
        }

        var handle = FindVisualChildren<FrameworkElement>(nodeView)
            .FirstOrDefault(port => port is Ellipse
                && port.Tag is PortReference reference
                && reference.Direction == direction
                && reference.ItemId == itemId);
        if (handle is null || !handle.IsVisible)
        {
            return null;
        }

        var point = handle.TransformToAncestor(PlannerCanvas)
            .Transform(new Point(handle.ActualWidth / 2, handle.ActualHeight / 2));
        return point;
    }

    private void ClearSelection()
    {
        _selectedNode = null;
        _selectedEdge = null;
        _selectedComment = null;
        _selectedRoutePoint = null;
        _selectedNodeIds.Clear();
        _selectedCommentIds.Clear();
        _selectedRoutePoints.Clear();
    }

    private void SelectSingleNode(SchemeNode node)
    {
        ClearSelection();
        _selectedNode = node;
        _selectedNodeIds.Add(node.Id);
    }

    private void SelectSingleEdge(SchemeEdge edge)
    {
        ClearSelection();
        _selectedEdge = edge;
    }

    private void SelectSingleComment(SchemeComment comment)
    {
        ClearSelection();
        _selectedComment = comment;
        _selectedCommentIds.Add(comment.Id);
    }

    private void SelectSingleRoutePoint(SchemeEdge edge, RoutePointReference reference)
    {
        ClearSelection();
        _selectedEdge = edge;
        _selectedRoutePoint = reference;
        _selectedRoutePoints.Add(reference);
    }

    private void SyncPrimarySelectionFromSets()
    {
        _selectedNode = null;
        _selectedEdge = null;
        _selectedComment = null;
        _selectedRoutePoint = null;

        if (_selectedNodeIds.Count == 1 && _selectedRoutePoints.Count == 0 && _selectedCommentIds.Count == 0)
        {
            var nodeId = _selectedNodeIds.First();
            _selectedNode = _scheme.Nodes.FirstOrDefault(node => node.Id == nodeId);
            return;
        }

        if (_selectedCommentIds.Count == 1 && _selectedNodeIds.Count == 0 && _selectedRoutePoints.Count == 0)
        {
            var commentId = _selectedCommentIds.First();
            _selectedComment = _scheme.Comments.FirstOrDefault(comment => comment.Id == commentId);
            return;
        }

        if (_selectedRoutePoints.Count == 1 && _selectedNodeIds.Count == 0 && _selectedCommentIds.Count == 0)
        {
            var reference = _selectedRoutePoints.First();
            var edge = _scheme.Edges.FirstOrDefault(item => item.Id == reference.EdgeId);
            if (edge is not null && reference.Index >= 0 && reference.Index < edge.RoutePoints.Count)
            {
                _selectedEdge = edge;
                _selectedRoutePoint = reference;
            }
        }
    }

    private void ShowSelectionRectangle(Point start, Point current)
    {
        if (_selectionRectangle is null)
        {
            _selectionRectangle = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(36, 0, 171, 224)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 171, 224)),
                StrokeThickness = 1.5,
                StrokeDashArray = [5, 3],
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(_selectionRectangle, int.MaxValue);
            PlannerCanvas.Children.Add(_selectionRectangle);
        }

        var rect = new Rect(start, current);
        Canvas.SetLeft(_selectionRectangle, rect.Left);
        Canvas.SetTop(_selectionRectangle, rect.Top);
        _selectionRectangle.Width = rect.Width;
        _selectionRectangle.Height = rect.Height;
    }

    private void HideSelectionRectangle()
    {
        if (_selectionRectangle is null)
        {
            return;
        }

        PlannerCanvas.Children.Remove(_selectionRectangle);
        _selectionRectangle = null;
    }

    private void SelectInsideRectangle(Rect selection)
    {
        ClearSelection();

        foreach (var comment in _scheme.Comments)
        {
            var commentRect = new Rect(comment.X, comment.Y, Math.Max(140, comment.Width), Math.Max(82, comment.Height));
            if (selection.IntersectsWith(commentRect))
            {
                _selectedCommentIds.Add(comment.Id);
            }
        }

        foreach (var node in _scheme.Nodes)
        {
            if (!_nodeViews.TryGetValue(node.Id, out var view))
            {
                continue;
            }

            var width = view.ActualWidth > 0 ? view.ActualWidth : view.Width;
            var height = view.ActualHeight > 0 ? view.ActualHeight : view.Height;
            var nodeRect = new Rect(node.X, node.Y, width, height);
            if (selection.IntersectsWith(nodeRect))
            {
                _selectedNodeIds.Add(node.Id);
            }
        }

        foreach (var edge in _scheme.Edges)
        {
            for (var index = 0; index < edge.RoutePoints.Count; index++)
            {
                var point = edge.RoutePoints[index];
                if (selection.Contains(new Point(point.X, point.Y)))
                {
                    _selectedRoutePoints.Add(new RoutePointReference(edge.Id, index));
                }
            }
        }

        SyncPrimarySelectionFromSets();
    }

    private void BeginGroupDrag(Point _)
    {
        _groupDragNodeStarts.Clear();
        _groupDragCommentStarts.Clear();
        _groupDragRoutePointStarts.Clear();

        foreach (var commentId in _selectedCommentIds)
        {
            var comment = _scheme.Comments.FirstOrDefault(item => item.Id == commentId);
            if (comment is not null)
            {
                _groupDragCommentStarts[commentId] = new Point(comment.X, comment.Y);
            }
        }

        foreach (var nodeId in _selectedNodeIds)
        {
            var node = _scheme.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is not null)
            {
                _groupDragNodeStarts[nodeId] = new Point(node.X, node.Y);
            }
        }

        foreach (var entry in SelectedRoutePointEntries())
        {
            _groupDragRoutePointStarts[entry.Reference] = new Point(entry.Point.X, entry.Point.Y);
        }
    }

    private void ApplyGroupDrag(Point current, Point start)
    {
        var deltaX = current.X - start.X;
        var deltaY = current.Y - start.Y;

        foreach (var (commentId, origin) in _groupDragCommentStarts)
        {
            var comment = _scheme.Comments.FirstOrDefault(item => item.Id == commentId);
            if (comment is null)
            {
                continue;
            }

            comment.X = origin.X + deltaX;
            comment.Y = origin.Y + deltaY;
            UpdateCommentViewPosition(comment);
        }

        foreach (var (nodeId, origin) in _groupDragNodeStarts)
        {
            var node = _scheme.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is null)
            {
                continue;
            }

            node.X = origin.X + deltaX;
            node.Y = origin.Y + deltaY;
            UpdateNodeViewPosition(node);
        }

        foreach (var (reference, origin) in _groupDragRoutePointStarts)
        {
            var routePoint = RoutePointForReference(reference);
            if (routePoint is null)
            {
                continue;
            }

            routePoint.X = origin.X + deltaX;
            routePoint.Y = origin.Y + deltaY;
        }
    }

    private void SnapGroupDrag()
    {
        foreach (var commentId in _groupDragCommentStarts.Keys)
        {
            var comment = _scheme.Comments.FirstOrDefault(item => item.Id == commentId);
            if (comment is null)
            {
                continue;
            }

            var snapped = _layoutService.Snap(new Point(comment.X, comment.Y));
            comment.X = snapped.X;
            comment.Y = snapped.Y;
            UpdateCommentViewPosition(comment);
        }

        foreach (var nodeId in _groupDragNodeStarts.Keys)
        {
            var node = _scheme.Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is null)
            {
                continue;
            }

            var snapped = _layoutService.Snap(new Point(node.X, node.Y));
            node.X = snapped.X;
            node.Y = snapped.Y;
            UpdateNodeViewPosition(node);
        }

        foreach (var reference in _groupDragRoutePointStarts.Keys)
        {
            var routePoint = RoutePointForReference(reference);
            if (routePoint is null)
            {
                continue;
            }

            var snapped = _layoutService.Snap(new Point(routePoint.X, routePoint.Y));
            routePoint.X = snapped.X;
            routePoint.Y = snapped.Y;
        }
    }

    private void ClearGroupDrag()
    {
        _groupDragNodeStarts.Clear();
        _groupDragCommentStarts.Clear();
        _groupDragRoutePointStarts.Clear();
    }

    private void UpdateNodeViewPosition(SchemeNode node)
    {
        if (!_nodeViews.TryGetValue(node.Id, out var view))
        {
            return;
        }

        Canvas.SetLeft(view, node.X);
        Canvas.SetTop(view, node.Y);
        view.UpdateLayout();
    }

    private void UpdateCommentViewPosition(SchemeComment comment)
    {
        if (!_commentViews.TryGetValue(comment.Id, out var view))
        {
            return;
        }

        Canvas.SetLeft(view, comment.X);
        Canvas.SetTop(view, comment.Y);
        view.UpdateLayout();
    }

    private void UpdateCommentViewBounds(SchemeComment comment)
    {
        if (!_commentViews.TryGetValue(comment.Id, out var view))
        {
            return;
        }

        view.Width = Math.Max(140, comment.Width);
        view.Height = Math.Max(82, comment.Height);
        Canvas.SetLeft(view, comment.X);
        Canvas.SetTop(view, comment.Y);
        view.UpdateLayout();
    }

    private void FocusCommentTitle(string commentId)
    {
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                if (!_commentViews.TryGetValue(commentId, out var view))
                {
                    return;
                }

                var textBox = FindVisualChildren<TextBox>(view).FirstOrDefault();
                if (textBox is null)
                {
                    return;
                }

                textBox.Focus();
                textBox.SelectAll();
            }),
            DispatcherPriority.Input);
    }

    private RoutePoint? RoutePointForReference(RoutePointReference reference)
    {
        var edge = _scheme.Edges.FirstOrDefault(item => item.Id == reference.EdgeId);
        if (edge is null || reference.Index < 0 || reference.Index >= edge.RoutePoints.Count)
        {
            return null;
        }

        return edge.RoutePoints[reference.Index];
    }

    private IEnumerable<(SchemeEdge Edge, int Index, RoutePoint Point, RoutePointReference Reference)> SelectedRoutePointEntries()
    {
        foreach (var reference in _selectedRoutePoints)
        {
            var edge = _scheme.Edges.FirstOrDefault(item => item.Id == reference.EdgeId);
            if (edge is null || reference.Index < 0 || reference.Index >= edge.RoutePoints.Count)
            {
                continue;
            }

            yield return (edge, reference.Index, edge.RoutePoints[reference.Index], reference);
        }
    }

    private Ellipse CreateRoutePointHandle(SchemeEdge edge, int routePointIndex)
    {
        var handle = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(OutputPortColor),
            Stroke = new SolidColorBrush(Color.FromRgb(5, 12, 17)),
            StrokeThickness = 1.5,
            Tag = new RoutePointReference(edge.Id, routePointIndex),
            Cursor = Cursors.SizeAll,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = OutputPortColor,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.7,
            },
        };
        handle.MouseLeftButtonDown += RoutePoint_MouseLeftButtonDown;
        handle.MouseMove += RoutePoint_MouseMove;
        handle.MouseLeftButtonUp += RoutePoint_MouseLeftButtonUp;
        return handle;
    }

    private void Comment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not SchemeComment comment)
        {
            return;
        }

        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            if (!_selectedCommentIds.Contains(comment.Id))
            {
                SelectSingleComment(comment);
                UpdateInspector();
                UpdateSelectionVisuals();
            }
            return;
        }

        if (!_selectedCommentIds.Contains(comment.Id))
        {
            SelectSingleComment(comment);
        }

        _dragComment = comment;
        _dragStartMouse = e.GetPosition(PlannerCanvas);
        BeginGroupDrag(_dragStartMouse);
        element.CaptureMouse();
        UpdateInspector();
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void Comment_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragComment is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ApplyGroupDrag(e.GetPosition(PlannerCanvas), _dragStartMouse);
        PlannerCanvas.UpdateLayout();
        RefreshEdges();
    }

    private void Comment_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        if (_dragComment is not null)
        {
            SnapGroupDrag();
            PlannerCanvas.UpdateLayout();
            RefreshEdges();
        }

        _dragComment = null;
        ClearGroupDrag();
    }

    private void CommentTitle_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Tag is SchemeComment comment)
        {
            comment.Text = textBox.Text;
        }
    }

    private void CommentResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not SchemeComment comment)
        {
            return;
        }

        SelectSingleComment(comment);
        _commentResizeDrag = new CommentResizeDrag(
            comment,
            e.GetPosition(PlannerCanvas),
            Math.Max(140, comment.Width),
            Math.Max(82, comment.Height));
        element.CaptureMouse();
        UpdateInspector();
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void CommentResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_commentResizeDrag is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(PlannerCanvas);
        _commentResizeDrag.Comment.Width = Math.Max(
            140,
            _commentResizeDrag.StartWidth + current.X - _commentResizeDrag.StartMouse.X);
        _commentResizeDrag.Comment.Height = Math.Max(
            82,
            _commentResizeDrag.StartHeight + current.Y - _commentResizeDrag.StartMouse.Y);
        UpdateCommentViewBounds(_commentResizeDrag.Comment);
        e.Handled = true;
    }

    private void CommentResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        if (_commentResizeDrag is not null)
        {
            var snapped = _layoutService.Snap(new Point(
                _commentResizeDrag.Comment.Width,
                _commentResizeDrag.Comment.Height));
            _commentResizeDrag.Comment.Width = Math.Max(140, snapped.X);
            _commentResizeDrag.Comment.Height = Math.Max(82, snapped.Y);
            UpdateCommentViewBounds(_commentResizeDrag.Comment);
        }

        _commentResizeDrag = null;
        e.Handled = true;
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not SchemeNode node)
        {
            return;
        }

        if (!_selectedNodeIds.Contains(node.Id))
        {
            SelectSingleNode(node);
        }

        _dragNode = node;
        _dragStartMouse = e.GetPosition(PlannerCanvas);
        _dragStartNode = new Point(node.X, node.Y);
        BeginGroupDrag(_dragStartMouse);
        element.CaptureMouse();
        UpdateInspector();
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(PlannerCanvas);
        ApplyGroupDrag(current, _dragStartMouse);
        PlannerCanvas.UpdateLayout();
        RefreshEdges();
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }
        if (_dragNode is not null)
        {
            SnapGroupDrag();
            PlannerCanvas.UpdateLayout();
            RefreshEdges();
        }
        _dragNode = null;
        ClearGroupDrag();
    }

    private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is SchemeEdge edge)
        {
            if (e.ClickCount == 2)
            {
                AddRoutePoint(edge, e.GetPosition(PlannerCanvas));
                e.Handled = true;
                return;
            }

            SelectSingleEdge(edge);
            UpdateInspector();
            UpdateSelectionVisuals();
            e.Handled = true;
        }
    }

    private void Edge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
    }

    private void AddRoutePoint(SchemeEdge edge, Point point)
    {
        var insertIndex = RoutePointInsertIndex(edge, point);
        edge.RoutePoints.Insert(insertIndex, new RoutePoint { X = point.X, Y = point.Y });
        SelectSingleRoutePoint(edge, new RoutePointReference(edge.Id, insertIndex));
        RenderCanvas();
        UpdateInspector();
    }

    private int RoutePointInsertIndex(SchemeEdge edge, Point point)
    {
        var sourcePoint = GetPortPoint(edge.SourceNodeId, "output", edge.SourceItemId);
        var targetPoint = GetPortPoint(edge.TargetNodeId, "input", edge.TargetItemId);
        if (sourcePoint is null || targetPoint is null)
        {
            return edge.RoutePoints.Count;
        }

        var points = CanvasGeometryService.EdgePoints(edge, sourcePoint.Value, targetPoint.Value);
        var bestSegment = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var distance = CanvasGeometryService.DistanceToSegment(point, points[index], points[index + 1]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSegment = index;
            }
        }

        return bestSegment;
    }

    private void RoutePoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not RoutePointReference reference)
        {
            return;
        }

        var edge = _scheme.Edges.FirstOrDefault(item => item.Id == reference.EdgeId);
        if (edge is null || reference.Index < 0 || reference.Index >= edge.RoutePoints.Count)
        {
            return;
        }

        if (!_selectedRoutePoints.Contains(reference))
        {
            SelectSingleRoutePoint(edge, reference);
        }

        var mouse = e.GetPosition(PlannerCanvas);
        BeginGroupDrag(mouse);
        _routePointDrag = new RoutePointDrag(reference, mouse, new Point(edge.RoutePoints[reference.Index].X, edge.RoutePoints[reference.Index].Y));
        element.CaptureMouse();
        UpdateInspector();
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void RoutePoint_MouseMove(object sender, MouseEventArgs e)
    {
        if (_routePointDrag is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(PlannerCanvas);
        ApplyGroupDrag(current, _routePointDrag.StartMouse);
        RefreshEdges();
    }

    private void RoutePoint_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        if (_routePointDrag is not null)
        {
            SnapGroupDrag();
            RefreshEdges();
        }

        _routePointDrag = null;
        ClearGroupDrag();
    }

    private void Port_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle || handle.Tag is not PortReference port)
        {
            return;
        }

        if (!IsPortReferenceAvailable(port))
        {
            SetStatus("This resource is not available for connection with current corporation levels.");
            e.Handled = true;
            return;
        }

        var start = GetPortPoint(port.NodeId, port.Direction, port.ItemId) ?? e.GetPosition(PlannerCanvas);
        var path = new Path
        {
            Stroke = PortBrush(port.Direction),
            StrokeThickness = 2.5,
            StrokeDashArray = [4, 3],
            Data = CanvasGeometryService.CreateBezier(start, start, port.Direction),
        };
        PlannerCanvas.Children.Insert(0, path);
        _connectionDrag = new ConnectionDrag(port, path, start);
        Mouse.Capture(PlannerCanvas);
        e.Handled = true;
    }

    private void PlannerCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == PlannerCanvas)
        {
            ClearSelection();
            _isSelecting = true;
            _selectionStart = e.GetPosition(PlannerCanvas);
            ShowSelectionRectangle(_selectionStart, _selectionStart);
            PlannerCanvas.CaptureMouse();
            UpdateInspector();
            UpdateSelectionVisuals();
            e.Handled = true;
        }
    }

    private void PlannerCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != PlannerCanvas || _connectionDrag is not null || _isSelecting || _isPanning)
        {
            return;
        }

        ClearSelection();
        _isCreatingComment = true;
        _commentStart = e.GetPosition(PlannerCanvas);
        ShowSelectionRectangle(_commentStart, _commentStart);
        PlannerCanvas.CaptureMouse();
        UpdateInspector();
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void PlannerCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _connectionDrag is not null || _isSelecting || _isCreatingComment)
        {
            return;
        }

        _isPanning = true;
        _panStartMouse = e.GetPosition(this);
        _panStartOffset = new Point(CanvasTranslate.X, CanvasTranslate.Y);
        PlannerCanvas.Cursor = Cursors.SizeAll;
        PlannerCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void PlannerCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_connectionDrag is not null)
        {
            var current = e.GetPosition(PlannerCanvas);
            _connectionDrag.Path.Data = CanvasGeometryService.CreateBezier(
                _connectionDrag.StartPoint,
                current,
                _connectionDrag.Port.Direction);
            return;
        }

        if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            ShowSelectionRectangle(_selectionStart, e.GetPosition(PlannerCanvas));
            return;
        }

        if (_isCreatingComment && e.RightButton == MouseButtonState.Pressed)
        {
            ShowSelectionRectangle(_commentStart, e.GetPosition(PlannerCanvas));
            return;
        }

        if (_isPanning && e.MiddleButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(this);
            CanvasTranslate.X = _panStartOffset.X + current.X - _panStartMouse.X;
            CanvasTranslate.Y = _panStartOffset.Y + current.Y - _panStartMouse.Y;
            return;
        }

        if (_isPanning && e.MiddleButton == MouseButtonState.Released)
        {
            EndViewportPan();
        }
    }

    private async void PlannerCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_connectionDrag is not null)
        {
            var drag = _connectionDrag;
            _connectionDrag = null;
            Mouse.Capture(null);
            PlannerCanvas.Children.Remove(drag.Path);

            var targetPort = FindAncestor<FrameworkElement>(e.OriginalSource as DependencyObject)?.Tag as PortReference;
            if (targetPort is not null && TryCreateEdge(drag.Port, targetPort))
            {
                RenderCanvas();
                return;
            }

            _suggestionCanvasPoint = e.GetPosition(PlannerCanvas);
            await ShowSuggestionsAsync(drag.Port, _suggestionCanvasPoint);
            return;
        }

        if (_isSelecting)
        {
            SelectInsideRectangle(new Rect(_selectionStart, e.GetPosition(PlannerCanvas)));
            HideSelectionRectangle();
            _isSelecting = false;
            PlannerCanvas.ReleaseMouseCapture();
            UpdateInspector();
            UpdateSelectionVisuals();
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            EndViewportPan();
        }
    }

    private void PlannerCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCreatingComment)
        {
            return;
        }

        var rect = new Rect(_commentStart, e.GetPosition(PlannerCanvas));
        HideSelectionRectangle();
        _isCreatingComment = false;
        PlannerCanvas.ReleaseMouseCapture();

        if (rect.Width >= 80 && rect.Height >= 50)
        {
            var snappedTopLeft = _layoutService.Snap(rect.TopLeft);
            var comment = new SchemeComment
            {
                Text = "Comment",
                X = snappedTopLeft.X,
                Y = snappedTopLeft.Y,
                Width = Math.Max(140, rect.Width),
                Height = Math.Max(82, rect.Height),
            };
            _scheme.Comments.Add(comment);
            SelectSingleComment(comment);
            RenderCanvas();
            FocusCommentTitle(comment.Id);
        }

        UpdateInspector();
        UpdateSelectionVisuals();
        e.Handled = true;
    }

    private void PlannerCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_isPanning)
        {
            return;
        }

        EndViewportPan();
        e.Handled = true;
    }

    private void EndViewportPan()
    {
        _isPanning = false;
        PlannerCanvas.Cursor = Cursors.Arrow;
        if (PlannerCanvas.IsMouseCaptured)
        {
            PlannerCanvas.ReleaseMouseCapture();
        }
    }

    private void PlannerCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta > 0 ? 1.1 : 0.9;
        var zoom = Math.Clamp(CanvasScale.ScaleX * delta, 0.35, 2.4);
        CanvasScale.ScaleX = zoom;
        CanvasScale.ScaleY = zoom;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        if (ZoomText is not null)
        {
            ZoomText.Text = $"{CanvasScale.ScaleX * 100:0}%";
        }
    }

    private bool TryCreateEdge(PortReference first, PortReference second)
    {
        if (first.NodeId == second.NodeId || first.Direction == second.Direction || first.ItemId != second.ItemId)
        {
            return false;
        }

        var source = first.Direction == "output" ? first : second;
        var target = first.Direction == "input" ? first : second;
        if (!IsPortReferenceAvailable(source) || !IsPortReferenceAvailable(target))
        {
            SetStatus("This resource is not available for connection with current corporation levels.");
            return false;
        }

        var sourceRecipe = RecipeForNode(_scheme.Nodes.FirstOrDefault(node => node.Id == source.NodeId));
        var targetRecipe = RecipeForNode(_scheme.Nodes.FirstOrDefault(node => node.Id == target.NodeId));
        if (!_calculator.CanConnectOutputToInput(sourceRecipe, targetRecipe, source.ItemId))
        {
            SetStatus("Ports are not compatible.");
            return false;
        }

        if (_scheme.Edges.Any(edge =>
                edge.SourceNodeId == source.NodeId
                && edge.TargetNodeId == target.NodeId
                && edge.SourceItemId == source.ItemId))
        {
            SetStatus("Connection already exists.");
            return false;
        }

        _scheme.Edges.Add(new SchemeEdge
        {
            SourceNodeId = source.NodeId,
            SourceItemId = source.ItemId,
            TargetNodeId = target.NodeId,
            TargetItemId = target.ItemId,
        });
        SetStatus("Connected machines.");
        return true;
    }

    private async Task ShowSuggestionsAsync(PortReference sourcePort, Point canvasPoint)
    {
        _suggestionCancellation?.Cancel();
        _suggestionCancellation?.Dispose();
        _suggestionCancellation = new CancellationTokenSource();
        var token = _suggestionCancellation.Token;

        try
        {
            var response = await _apiClient.GetSuggestionsAsync(sourcePort.Direction, sourcePort.ItemId, token);
            token.ThrowIfCancellationRequested();
            NormalizeSuggestionAssets(response.Suggestions);
            response.Suggestions = response.Suggestions
                .Where(recipe => PlannerUnlockService.IsBuildingUnlocked(_catalog, _scheme, recipe.BuildingId))
                .ToList();
            SuggestionList.ItemsSource = response.Suggestions;
            SuggestionList.Tag = sourcePort;
            SuggestionPopup.IsOpen = response.Suggestions.Count > 0;
            if (SuggestionPopup.IsOpen)
            {
                CenterSuggestionPopupAtCanvasPoint(canvasPoint);
                _ = Dispatcher.BeginInvoke(
                    new Action(() => CenterSuggestionPopupAtCanvasPoint(canvasPoint)),
                    DispatcherPriority.Loaded);
            }

            SetStatus(response.Suggestions.Count == 0
                ? "No compatible machines found."
                : $"Found {response.Suggestions.Count} compatible machines.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load suggestions: {ex.Message}");
        }
    }

    private void NormalizeSuggestionAssets(IEnumerable<RecipeInfo> suggestions)
    {
        foreach (var recipe in suggestions)
        {
            recipe.BuildingImageUrl = _apiClient.ToAbsoluteAssetUrl(recipe.BuildingImageUrl);
            recipe.Output.ImageUrl = _apiClient.ToAbsoluteAssetUrl(recipe.Output.ImageUrl);

            foreach (var input in recipe.Inputs)
            {
                input.ImageUrl = _apiClient.ToAbsoluteAssetUrl(input.ImageUrl);
            }
        }
    }

    private void CenterSuggestionPopupAtCanvasPoint(Point canvasPoint)
    {
        var framePoint = PlannerCanvas.TransformToAncestor(CanvasFrame).Transform(canvasPoint);
        var popupSize = MeasureSuggestionPopup();
        SuggestionPopup.HorizontalOffset = framePoint.X - popupSize.Width / 2;
        SuggestionPopup.VerticalOffset = framePoint.Y - popupSize.Height / 2;
    }

    private Size MeasureSuggestionPopup()
    {
        SuggestionPopupChrome.Measure(new Size(330, 360));
        var desired = SuggestionPopupChrome.DesiredSize;
        var width = desired.Width > 0 ? desired.Width : SuggestionPopupChrome.ActualWidth;
        var height = desired.Height > 0 ? desired.Height : SuggestionPopupChrome.ActualHeight;

        if (width <= 0)
        {
            width = 330;
        }

        if (height <= 0)
        {
            height = 160;
        }

        return new Size(width, Math.Min(height, 360));
    }

    private void SuggestionPopupChrome_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        element.Clip = new RectangleGeometry(new Rect(e.NewSize), 8, 8);
    }

    private void SuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!SuggestionPopup.IsOpen
            || SuggestionList.SelectedItem is not RecipeInfo recipe
            || SuggestionList.Tag is not PortReference sourcePort)
        {
            return;
        }

        var position = _layoutService.Snap(_suggestionCanvasPoint);
        var newNode = CreateNode(recipe, position.X, position.Y);
        _scheme.Nodes.Add(newNode);

        var newPort = sourcePort.Direction == "input"
            ? new PortReference(newNode.Id, "output", sourcePort.ItemId)
            : new PortReference(newNode.Id, "input", sourcePort.ItemId);

        TryCreateEdge(sourcePort, newPort);
        SelectSingleNode(newNode);
        SuggestionPopup.IsOpen = false;
        SuggestionList.SelectedItem = null;
        RenderCanvas();
        UpdateInspector();
    }

    private void InspectorRecipeSearchBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        OpenInspectorRecipePopup();
        e.Handled = true;
    }

    private void InspectorRecipeSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        OpenInspectorRecipePopup();
    }

    private void InspectorRecipePicker_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CloseInspectorRecipePopupIfFocusLeft();
    }

    private void InspectorRecipeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingInspector || !InspectorRecipePopup.IsOpen)
        {
            return;
        }

        ApplyInspectorRecipeFilter(InspectorRecipeSearchBox.Text, preserveSearchText: true);
    }

    private void InspectorRecipePopup_Closed(object sender, EventArgs e)
    {
        if (_selectedNode is null)
        {
            return;
        }

        _updatingInspector = true;
        try
        {
            InspectorRecipeSearchBox.Text = RecipeForNode(_selectedNode)?.InspectorDisplayName ?? "";
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void InspectorRecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingInspector || _selectedNode is null || InspectorRecipeList.SelectedItem is not RecipeInfo recipe)
        {
            return;
        }

        _selectedNode.BuildingId = recipe.BuildingId;
        _selectedNode.SelectedRecipeKey = recipe.RecipeKey;
        _selectedNode.MachineCount = Math.Max(1, _selectedNode.MachineCount);
        _selectedNode.Priority = ProductionPriority.Mid;
        _selectedNode.TargetOutputPerMinute = 0;
        InspectorRecipePopup.IsOpen = false;
        RemoveInvalidEdgesForNode(_selectedNode.Id);
        RenderCanvas();
        UpdateInspector();
    }

    private void OpenInspectorRecipePopup()
    {
        if (_selectedNode is null)
        {
            return;
        }

        ApplyInspectorRecipeFilter("", preserveSearchText: false);
        InspectorRecipePopup.IsOpen = true;
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                InspectorRecipeSearchBox.Focus();
                InspectorRecipeSearchBox.Text = "";
                InspectorRecipeSearchBox.CaretIndex = 0;
            }),
            DispatcherPriority.Input);
    }

    private void CloseInspectorRecipePopupIfFocusLeft()
    {
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                if (!InspectorRecipePopup.IsOpen)
                {
                    return;
                }

                var focused = Keyboard.FocusedElement as DependencyObject;
                var insideSearch = IsDescendantOf(focused, InspectorRecipeSearchBox);
                var insideList = IsDescendantOf(focused, InspectorRecipeList);
                if (!insideSearch && !insideList)
                {
                    InspectorRecipePopup.IsOpen = false;
                }
            }),
            DispatcherPriority.Input);
    }

    private void ApplyInspectorRecipeFilter(string? query, bool preserveSearchText)
    {
        if (_selectedNode is null)
        {
            InspectorRecipeList.ItemsSource = null;
            return;
        }

        var selected = RecipeForNode(_selectedNode);
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _inspectorRecipes
            : _inspectorRecipes
                .Where(recipe => recipe.Output.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        _updatingInspector = true;
        try
        {
            InspectorRecipeList.ItemsSource = filtered;
            InspectorRecipeList.SelectedItem = selected is not null && filtered.Any(recipe => recipe.RecipeKey == selected.RecipeKey)
                ? selected
                : null;
            if (!preserveSearchText)
            {
                InspectorRecipeSearchBox.Text = "";
            }
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void TargetOutputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingInspector || _selectedNode is null)
        {
            return;
        }

        if (int.TryParse(TargetOutputBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var machineCount)
            || int.TryParse(TargetOutputBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out machineCount))
        {
            _selectedNode.MachineCount = Math.Max(1, machineCount);
            _selectedNode.TargetOutputPerMinute = 0;
            RenderCanvas();
            UpdateInspector(readTargetBox: false);
        }
    }

    private void PriorityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingInspector || _selectedNode is null || PriorityBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse<ProductionPriority>(item.Tag?.ToString(), out var priority))
        {
            _selectedNode.Priority = priority;
            RefreshEdges();
            RenderCanvas();
            UpdateInspector();
        }
    }

    private void RemoveInvalidEdgesForNode(string nodeId)
    {
        foreach (var edge in _scheme.Edges.Where(edge => edge.SourceNodeId == nodeId || edge.TargetNodeId == nodeId).ToList())
        {
            if (!IsEdgeValid(edge))
            {
                _scheme.Edges.Remove(edge);
            }
        }
    }

    private void UpdateInspector(bool readTargetBox = true)
    {
        _updatingInspector = true;
        try
        {
            NodeInspectorPanel.Visibility = Visibility.Collapsed;
            ConnectionInspectorPanel.Visibility = Visibility.Collapsed;
            SchemeSettingsPanel.Visibility = Visibility.Collapsed;
            InspectorRecipeList.ItemsSource = null;
            InspectorInputs.ItemsSource = null;
            InspectorUnlocks.ItemsSource = null;
            InspectorMetricsStack.Children.Clear();
            CorporationSettingsStack.Children.Clear();
            RailTierSettingsStack.Children.Clear();
            ConnectionReadOnly.Text = "";
            InspectorStatusPanel.Visibility = Visibility.Collapsed;
            PriorityBox.SelectedItem = null;

            if (_selectedNode is not null)
            {
                _inspectorViewModel.LoadNode(_catalog, _selectedNode);
                var recipe = RecipeForNode(_selectedNode);
                var building = BuildingForNode(_selectedNode);
                _inspectorRecipes = RecipesForNode(_selectedNode);
                NodeInspectorPanel.Visibility = Visibility.Visible;
                InspectorStatusPanel.Visibility = Visibility.Visible;
                ShowInspectorTab(_activeInspectorTab);
                InspectorTitle.Text = recipe?.BuildingName ?? building?.Name ?? "Unselected machine";
                InspectorRecipeList.ItemsSource = _inspectorRecipes;
                InspectorRecipeList.SelectedItem = recipe;
                InspectorRecipeSearchBox.Text = recipe?.InspectorDisplayName ?? "";
                SetImage(InspectorImage, recipe?.BuildingImageUrl ?? building?.ImageUrl);
                if (readTargetBox)
                {
                    TargetOutputBox.Text = recipe is null
                        ? ""
                        : ProductionAnalysisService.EffectiveMachineCount(_selectedNode).ToString(CultureInfo.CurrentCulture);
                }
                SelectPriorityBoxItem(_selectedNode.Priority);

                if (recipe is null)
                {
                    InspectorReadOnly.Text = "No recipe selected. Ports are disabled.";
                    return;
                }

                var machines = ProductionAnalysisService.EffectiveMachineCount(_selectedNode);
                var outputPerMinute = _calculator.OutputPerMinute(recipe, machines);
                BuildInspectorMetrics(_selectedNode, recipe, machines, outputPerMinute);
                InspectorReadOnly.Text = $"Recipe base: {recipe.Output.QuantityPerMinute:g}/min ({recipe.OriginalRateText}). Inputs scale with machine count.";
                InspectorInputs.ItemsSource = recipe.Inputs
                    .Select(input =>
                    {
                        var required = _calculator.RequiredInputPerMinute(recipe, input, machines);
                        var key = ProductionInputKey.For(_selectedNode.Id, input.ItemId);
                        var delivered = _productionAnalysis.Inputs.TryGetValue(key, out var analysis)
                            ? analysis.DeliveredPerMinute
                            : 0;
                        var prefix = delivered + 0.000001 < required ? "SHORT " : "";
                        return $"{prefix}{input.Name}: {required:g}/min required, {delivered:g}/min delivered";
                    })
                    .ToList();
                InspectorUnlocks.ItemsSource = recipe.UnlockRequirements.Count == 0
                    ? new List<string> { "None" }
                    : recipe.UnlockRequirements.Select(item => $"{item.Name}: {item.RequiredQuantity:g}").ToList();
                return;
            }

            InspectorTitle.Text = "";
            InspectorImage.Source = null;
            InspectorRecipeList.SelectedItem = null;
            InspectorRecipeSearchBox.Text = "";
            TargetOutputBox.Text = "";
            PriorityBox.SelectedItem = null;
            InspectorReadOnly.Text = "";

            if (_selectedEdge is not null)
            {
                InspectorTitle.Text = "Connection";
                ConnectionInspectorPanel.Visibility = Visibility.Visible;
                ConnectionReadOnly.Text = EdgeDetail(_selectedEdge);
                return;
            }

            if (HasNoCanvasSelection())
            {
                InspectorTitle.Text = "Scheme";
                InspectorStatusPanel.Visibility = Visibility.Collapsed;
                SchemeSettingsPanel.Visibility = Visibility.Visible;
                BuildSchemeSettingsInspector();
            }
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private bool HasNoCanvasSelection()
    {
        return _selectedNode is null
            && _selectedEdge is null
            && _selectedComment is null
            && _selectedRoutePoint is null
            && _selectedNodeIds.Count == 0
            && _selectedCommentIds.Count == 0
            && _selectedRoutePoints.Count == 0;
    }

    private void BuildSchemeSettingsInspector()
    {
        PlannerUnlockService.EnsureSchemeDefaults(_scheme, _catalog);
        BuildCorporationSettings();
        BuildRailTierSettings();
    }

    private void BuildCorporationSettings()
    {
        CorporationSettingsStack.Children.Clear();
        if (_catalog.Corporations.Count == 0)
        {
            CorporationSettingsStack.Children.Add(new TextBlock
            {
                Text = "Corporation data is not available yet.",
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var corporation in _catalog.Corporations.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = corporation.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = $"Max level {Math.Max(0, corporation.MaxLevel)}",
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });
            row.Children.Add(textPanel);

            var maxLevel = Math.Max(0, corporation.MaxLevel);
            var currentLevel = _scheme.CorporationLevels.TryGetValue(corporation.CorporationId, out var value)
                ? Math.Clamp(value, 0, maxLevel)
                : 0;
            var picker = new ComboBox
            {
                ItemsSource = Enumerable.Range(0, maxLevel + 1).ToList(),
                SelectedItem = currentLevel,
                Tag = corporation.CorporationId,
                MinWidth = 72,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            picker.SelectionChanged += CorporationLevelPicker_SelectionChanged;
            Grid.SetColumn(picker, 1);
            row.Children.Add(picker);

            CorporationSettingsStack.Children.Add(row);
        }
    }

    private void BuildRailTierSettings()
    {
        RailTierSettingsStack.Children.Clear();
        if (_catalog.TransportTiers.Tiers.Count == 0)
        {
            RailTierSettingsStack.Children.Add(new TextBlock
            {
                Text = "No rail tiers are configured.",
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var maxAvailable = PlannerUnlockService.MaxAvailableRailTier(_catalog, _scheme);
        RailTierSettingsStack.Children.Add(new TextBlock
        {
            Text = maxAvailable is null
                ? "No rail tiers are currently unlocked."
                : $"Max available: {maxAvailable.Name} ({maxAvailable.ItemsPerMinute:g}/min)",
            Foreground = maxAvailable is null
                ? new SolidColorBrush(ShortageColor)
                : (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var tier in _catalog.TransportTiers.Tiers.OrderBy(item => item.Level))
        {
            var unlocked = PlannerUnlockService.IsRailTierUnlocked(tier, _scheme);
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(8),
                ToolTip = unlocked ? "Available for any connection that needs this capacity." : PlannerUnlockService.RailUnlockText(_catalog, tier),
                Background = unlocked
                    ? (Brush)Application.Current.FindResource("StarBlueBrush")
                    : (Brush)Application.Current.FindResource("MutedPanelBrush"),
                BorderBrush = unlocked
                    ? (Brush)Application.Current.FindResource("StarBlueBrush")
                    : (Brush)Application.Current.FindResource("MutedBorderBrush"),
                BorderThickness = new Thickness(1),
            };
            border.Child = new TextBlock
            {
                Text = unlocked
                    ? $"{tier.Name}  {tier.ItemsPerMinute:g}/min  available"
                    : $"{tier.Name}  {tier.ItemsPerMinute:g}/min  locked",
                Foreground = (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
                TextWrapping = TextWrapping.Wrap,
            };
            RailTierSettingsStack.Children.Add(border);

            if (unlocked)
            {
                continue;
            }

            RailTierSettingsStack.Children.Add(new TextBlock
            {
                Text = $"Requires: {PlannerUnlockService.RailUnlockText(_catalog, tier)}",
                Foreground = new SolidColorBrush(ShortageColor),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, -4, 0, 8),
            });
        }
    }

    private void CorporationLevelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingInspector
            || sender is not ComboBox picker
            || picker.Tag is not string corporationId
            || picker.SelectedItem is not int level)
        {
            return;
        }

        _scheme.CorporationLevels[corporationId] = level;
        PlannerUnlockService.EnsureSchemeDefaults(_scheme, _catalog);
        MigrateAndAnalyzeScheme();
        RenderCanvas();
        UpdateInspector();
        SetStatus("Corporation levels updated.");
    }

    private void InspectorTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string tab)
        {
            _activeInspectorTab = tab;
            ShowInspectorTab(tab);
        }
    }

    private void ShowInspectorTab(string tab)
    {
        InspectorDetailsPanel.Visibility = tab == "Details" ? Visibility.Visible : Visibility.Collapsed;
        InspectorStatisticsPanel.Visibility = tab == "Statistics" ? Visibility.Visible : Visibility.Collapsed;
        InspectorModificationsPanel.Visibility = tab == "Modifications" ? Visibility.Visible : Visibility.Collapsed;

        var active = (Brush)Application.Current.FindResource("StarBlueBrush");
        var inactive = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush");
        var transparent = (Brush)Brushes.Transparent;

        InspectorTabDetails.BorderBrush = tab == "Details" ? active : transparent;
        InspectorTabStatistics.BorderBrush = tab == "Statistics" ? active : transparent;
        InspectorTabModifications.BorderBrush = tab == "Modifications" ? active : transparent;
        InspectorTabDetailsText.Foreground = tab == "Details" ? active : inactive;
        InspectorTabStatisticsText.Foreground = tab == "Statistics" ? active : inactive;
        InspectorTabModificationsText.Foreground = tab == "Modifications" ? active : inactive;
        InspectorTabDetailsText.FontWeight = tab == "Details" ? FontWeights.SemiBold : FontWeights.Normal;
        InspectorTabStatisticsText.FontWeight = tab == "Statistics" ? FontWeights.SemiBold : FontWeights.Normal;
        InspectorTabModificationsText.FontWeight = tab == "Modifications" ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void OutputStepUp_Click(object sender, RoutedEventArgs e) => StepMachineCount(1);

    private void OutputStepDown_Click(object sender, RoutedEventArgs e) => StepMachineCount(-1);

    private void StepMachineCount(int delta)
    {
        if (_selectedNode is null)
        {
            return;
        }

        var current = int.TryParse(TargetOutputBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : ProductionAnalysisService.EffectiveMachineCount(_selectedNode);
        TargetOutputBox.Text = Math.Max(1, current + delta).ToString(CultureInfo.CurrentCulture);
        TargetOutputBox.CaretIndex = TargetOutputBox.Text.Length;
    }

    private void BuildInspectorMetrics(SchemeNode node, RecipeInfo recipe, int machines, double outputPerMinute)
    {
        InspectorMetricsStack.Children.Clear();

        foreach (var input in recipe.Inputs)
        {
            var required = _calculator.RequiredInputPerMinute(recipe, input, machines);
            InspectorMetricsStack.Children.Add(BuildMetricRow(
                "Input",
                $"{input.Name}  {input.QuantityPerMinute:g}/min",
                sub: $"Total {required:g}/min"));
        }

        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Output",
            $"{recipe.Output.Name}  {recipe.Output.QuantityPerMinute:g}/min",
            sub: $"Total {outputPerMinute:g}/min"));

        var building = BuildingForNode(node);
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Power",
            building?.Power is null ? "-" : PlannerMetricService.FormatNodePower(building, 1),
            sub: building?.Power is null ? null : $"Total {PlannerMetricService.FormatNodePower(building, machines)}"));
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Temperature",
            building?.Temperature is null ? "-" : PlannerMetricService.FormatNodeTemperature(building, 1),
            sub: building?.Temperature is null ? null : $"Total {PlannerMetricService.FormatNodeTemperature(building, machines)}"));

        InspectorMetricsStack.Children.Add(BuildMetricRow("Efficiency", "—"));

        var (ratio, isShort) = NodeFeedRatio(node);
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Utilization",
            $"{ratio * 100:0}%",
            valueBrush: new SolidColorBrush(isShort ? ShortageColor : SignalGreenColor)));
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Status",
            isShort ? "Starved" : "Running",
            valueBrush: new SolidColorBrush(isShort ? ShortageColor : SignalGreenColor)));
    }

    private FrameworkElement BuildMetricRow(string label, string value, Brush? valueBrush = null, string? sub = null)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var labelText = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(labelText, Dock.Left);
        dock.Children.Add(labelText);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new TextBlock
        {
            Text = value,
            FontWeight = FontWeights.SemiBold,
            Foreground = valueBrush ?? (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        if (!string.IsNullOrEmpty(sub))
        {
            right.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }

        dock.Children.Add(right);
        return dock;
    }

    private void SelectPriorityBoxItem(ProductionPriority priority)
    {
        foreach (var item in PriorityBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), priority.ToString(), StringComparison.Ordinal))
            {
                PriorityBox.SelectedItem = item;
                return;
            }
        }

        PriorityBox.SelectedItem = null;
    }

    private RecipeInfo? RecipeForNode(SchemeNode? node)
    {
        return PlannerEdgeService.RecipeForNode(_catalog, node);
    }

    private BuildingInfo? BuildingForNode(SchemeNode? node)
    {
        return PlannerEdgeService.BuildingForNode(_catalog, node);
    }

    private IReadOnlyList<RecipeInfo> RecipesForNode(SchemeNode node)
    {
        return PlannerEdgeService.RecipesForNode(_catalog, node);
    }

    private void SetImage(Image image, string? assetUrl)
    {
        var absolute = _apiClient.ToAbsoluteAssetUrl(assetUrl);
        if (string.IsNullOrWhiteSpace(absolute))
        {
            image.Source = null;
            return;
        }

        try
        {
            image.Source = new BitmapImage(new Uri(absolute));
        }
        catch
        {
            image.Source = null;
        }
    }

    private FontFamily CardFontFamily()
    {
        return new FontFamily(SafeFontFamily(_settings.CanvasCardFont.Family));
    }

    private double CardFontSize(double delta = 0)
    {
        return Math.Clamp(_settings.CanvasCardFont.Size + delta, 8, 36);
    }

    private Brush CardTextBrush(double opacity = 1)
    {
        var brush = BrushFromString(_settings.CanvasCardFont.Color, "#F4F0E8");
        brush.Opacity = opacity;
        return brush;
    }

    private static string SafeFontFamily(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;
    }

    private static SolidColorBrush BrushFromString(string? value, string fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value ?? fallback)!);
        }
        catch
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)!);
        }
    }

    private static AppTheme ResolveTheme(AppTheme theme)
    {
        if (theme != AppTheme.System)
        {
            return theme;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
            return appsUseLightTheme is int value && value == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            _ = SaveCurrentSchemeAsync();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            TopSearchBox.Focus();
            TopSearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelection();
            e.Handled = true;
        }
    }

    private void DeleteSelection()
    {
        var nodeIdsToDelete = _selectedNodeIds.ToHashSet(StringComparer.Ordinal);
        if (nodeIdsToDelete.Count == 0 && _selectedNode is not null)
        {
            nodeIdsToDelete.Add(_selectedNode.Id);
        }

        var commentIdsToDelete = _selectedCommentIds.ToHashSet(StringComparer.Ordinal);
        if (commentIdsToDelete.Count == 0 && _selectedComment is not null)
        {
            commentIdsToDelete.Add(_selectedComment.Id);
        }

        var routePointsToDelete = _selectedRoutePoints.ToList();
        if (routePointsToDelete.Count == 0 && _selectedRoutePoint is not null)
        {
            routePointsToDelete.Add(_selectedRoutePoint);
        }

        var changed = false;
        foreach (var group in routePointsToDelete.GroupBy(reference => reference.EdgeId))
        {
            var edge = _scheme.Edges.FirstOrDefault(item => item.Id == group.Key);
            if (edge is null || nodeIdsToDelete.Contains(edge.SourceNodeId) || nodeIdsToDelete.Contains(edge.TargetNodeId))
            {
                continue;
            }

            foreach (var index in group.Select(reference => reference.Index).Distinct().OrderDescending())
            {
                if (index < 0 || index >= edge.RoutePoints.Count)
                {
                    continue;
                }

                edge.RoutePoints.RemoveAt(index);
                changed = true;
            }
        }

        if (nodeIdsToDelete.Count > 0)
        {
            _scheme.Nodes.RemoveAll(node => nodeIdsToDelete.Contains(node.Id));
            _scheme.Edges.RemoveAll(edge => nodeIdsToDelete.Contains(edge.SourceNodeId) || nodeIdsToDelete.Contains(edge.TargetNodeId));
            changed = true;
        }

        if (commentIdsToDelete.Count > 0)
        {
            _scheme.Comments.RemoveAll(comment => commentIdsToDelete.Contains(comment.Id));
            changed = true;
        }

        if (changed)
        {
            ClearSelection();
            RenderCanvas();
            UpdateInspector();
        }
    }

    private void SetStatus(string status)
    {
        _lastStatus = status;
        _viewModel.SetStatus(status);
    }

    private void BeginToolboxDrag<T>(
        ListBox listBox,
        MouseButtonEventArgs e,
        out Point? dragStart,
        out T? dragItem)
        where T : class
    {
        dragStart = null;
        dragItem = null;

        if (!TryGetListItem(listBox, e.OriginalSource as DependencyObject, out T? item))
        {
            return;
        }

        dragStart = e.GetPosition(listBox);
        dragItem = item;
    }

    private static bool ShouldStartToolboxDrag<T>(ListBox listBox, MouseEventArgs e, Point? dragStart, T? dragItem)
        where T : class
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragStart is not Point start || dragItem is null)
        {
            return false;
        }

        var current = e.GetPosition(listBox);
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void ClearResourceToolboxDrag()
    {
        _resourceDragStart = null;
        _resourceDragItem = null;
    }

    private void ClearMachineToolboxDrag()
    {
        _machineDragStart = null;
        _machineDragItem = null;
    }

    private static bool TryGetListItem<T>(ListBox listBox, DependencyObject? source, out T? item)
        where T : class
    {
        item = null;

        if (source is null || FindAncestor<ScrollBar>(source) is not null)
        {
            return false;
        }

        var container = FindAncestor<ListBoxItem>(source);
        if (container is null || !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(container), listBox))
        {
            return false;
        }

        item = container.DataContext as T;
        return item is not null;
    }

    private static string? PromptForName(string title, string defaultValue)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(30, 34, 37)),
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        var textBox = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(new TextBlock { Text = "Name", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(textBox);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject ancestor)
    {
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject current)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
        {
            var child = VisualTreeHelper.GetChild(current, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record PortReference(string NodeId, string Direction, string ItemId);

    private sealed record ConnectionDrag(PortReference Port, Path Path, Point StartPoint);

    private sealed record RoutePointReference(string EdgeId, int Index);

    private sealed record RoutePointDrag(RoutePointReference Reference, Point StartMouse, Point StartPoint);

    private sealed record CommentResizeDrag(SchemeComment Comment, Point StartMouse, double StartWidth, double StartHeight);

    private sealed class EdgeVisual
    {
        public EdgeVisual(Path path, Path hitPath, TextBlock label)
        {
            Path = path;
            HitPath = hitPath;
            Label = label;
        }

        public Path Path { get; }
        public Path HitPath { get; }
        public TextBlock Label { get; }
        public List<Ellipse> RoutePointHandles { get; } = [];
    }
}
