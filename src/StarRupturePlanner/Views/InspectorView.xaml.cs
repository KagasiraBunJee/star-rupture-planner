using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;

namespace StarRupturePlanner.Views;

/// <summary>
/// Right inspector panel. Reads the current selection / scheme from the shared
/// <see cref="ISchemeSession"/> (refreshing on <see cref="ISchemeSession.SelectionChanged"/>),
/// and routes edits back through the session (<c>RequestCanvasRender</c> / <c>RequestStatus</c>).
/// Shell-coupled blueprint actions and full-selection checks are provided as callbacks.
/// </summary>
public partial class InspectorView : UserControl
{
    private ISchemeSession _session = null!;
    private IPlannerApiClient _apiClient = null!;
    private IPlannerCalculator _calculator = null!;
    private InspectorViewModel _inspectorViewModel = null!;
    private Func<bool> _hasNoCanvasSelection = () => true;
    private Func<SchemeNode, bool> _blueprintSourceExists = _ => false;
    private Action<SchemeNode> _openBlueprintSource = _ => { };

    private IReadOnlyList<RecipeInfo> _inspectorRecipes = [];
    private string _activeInspectorTab = "Details";
    private bool _updatingInspector;

    public InspectorView()
    {
        InitializeComponent();
    }

    public void Initialize(
        ISchemeSession session,
        IPlannerApiClient apiClient,
        IPlannerCalculator calculator,
        Func<bool> hasNoCanvasSelection,
        Func<SchemeNode, bool> blueprintSourceExists,
        Action<SchemeNode> openBlueprintSource)
    {
        _session = session;
        _apiClient = apiClient;
        _calculator = calculator;
        _inspectorViewModel = new InspectorViewModel(calculator);
        _hasNoCanvasSelection = hasNoCanvasSelection;
        _blueprintSourceExists = blueprintSourceExists;
        _openBlueprintSource = openBlueprintSource;

        // The shell populates the inspector during startup (NewScheme -> UpdateInspector -> SelectionChanged);
        // here we only subscribe so later selection/edit signals refresh it.
        _session.SelectionChanged += (_, _) => UpdateInspector();
    }

    private SchemeNode? SelectedNode => _session.SelectedNode;
    private SchemeEdge? SelectedEdge => _session.SelectedEdge;
    private PlannerCatalog Catalog => _session.CurrentCatalog;
    private SchemeDocument Scheme => _session.CurrentScheme;

    private RecipeInfo? RecipeForNode(SchemeNode? node) => PlannerEdgeService.RecipeForNode(Catalog, node);

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
        if (SelectedNode is null)
        {
            return;
        }

        _updatingInspector = true;
        try
        {
            InspectorRecipeSearchBox.Text = RecipeForNode(SelectedNode)?.InspectorDisplayName ?? "";
        }
        finally
        {
            _updatingInspector = false;
        }
    }

    private void InspectorRecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingInspector || SelectedNode is not { } node || InspectorRecipeList.SelectedItem is not RecipeInfo recipe)
        {
            return;
        }

        node.BuildingId = recipe.BuildingId;
        node.SelectedRecipeKey = recipe.RecipeKey;
        node.MachineCount = Math.Max(1, node.MachineCount);
        node.Priority = ProductionPriority.Mid;
        node.TargetOutputPerMinute = 0;
        InspectorRecipePopup.IsOpen = false;
        RemoveInvalidEdgesForNode(node.Id);
        _session.RequestCanvasRender();
        UpdateInspector();
    }

    private void OpenInspectorRecipePopup()
    {
        if (SelectedNode is null)
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
                var insideSearch = WpfVisualTreeHelpers.IsDescendantOf(focused, InspectorRecipeSearchBox);
                var insideList = WpfVisualTreeHelpers.IsDescendantOf(focused, InspectorRecipeList);
                if (!insideSearch && !insideList)
                {
                    InspectorRecipePopup.IsOpen = false;
                }
            }),
            DispatcherPriority.Input);
    }

    private void ApplyInspectorRecipeFilter(string? query, bool preserveSearchText)
    {
        if (SelectedNode is not { } node)
        {
            InspectorRecipeList.ItemsSource = null;
            return;
        }

        var selected = RecipeForNode(node);
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
        if (_updatingInspector || SelectedNode is not { } node)
        {
            return;
        }

        if (int.TryParse(TargetOutputBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var machineCount)
            || int.TryParse(TargetOutputBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out machineCount))
        {
            node.MachineCount = Math.Max(1, machineCount);
            node.TargetOutputPerMinute = 0;
            _session.RequestCanvasRender();
            UpdateInspector(readTargetBox: false);
        }
    }

    private void PriorityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingInspector || SelectedNode is not { } node || PriorityBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (Enum.TryParse<ProductionPriority>(item.Tag?.ToString(), out var priority))
        {
            node.Priority = priority;
            _session.RequestCanvasRender();
            UpdateInspector();
        }
    }

    private void OnlyOutputCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingInspector || SelectedNode is not { } node)
        {
            return;
        }

        node.OnlyOutput = OnlyOutputCheckBox.IsChecked == true && RecipeForNode(node) is not null;
        _session.RequestCanvasRender();
        UpdateInspector();
        _session.RequestStatus(node.OnlyOutput ? UiText.T("Status.NodeMarkedOutputOnly") : UiText.T("Status.NodeInputsRestored"));
    }

    private void SchemeOutputCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingInspector || SelectedNode is not { } node)
        {
            return;
        }

        node.IsSchemeOutput = SchemeOutputCheckBox.IsChecked == true && RecipeForNode(node) is not null;
        _session.RequestCanvasRender();
        UpdateInspector();
        _session.RequestStatus(node.IsSchemeOutput ? UiText.T("Status.NodeMarkedSchemeOutput") : UiText.T("Status.NodeRemovedSchemeOutputs"));
    }

    private void RemoveInvalidEdgesForNode(string nodeId)
    {
        foreach (var edge in Scheme.Edges.Where(edge => edge.SourceNodeId == nodeId || edge.TargetNodeId == nodeId).ToList())
        {
            if (!PlannerEdgeService.IsEdgeValid(Scheme, Catalog, _calculator, edge))
            {
                Scheme.Edges.Remove(edge);
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
            PriorityBox.IsEnabled = true;
            OpenBlueprintSourceButton.Visibility = Visibility.Collapsed;
            OpenBlueprintSourceButton.IsEnabled = false;
            OnlyOutputCheckBox.IsChecked = false;
            OnlyOutputCheckBox.IsEnabled = false;
            SchemeOutputCheckBox.IsChecked = false;
            SchemeOutputCheckBox.IsEnabled = false;

            var selectedNode = SelectedNode;
            if (selectedNode is not null)
            {
                if (selectedNode.NodeType == SchemeNodeType.BlueprintSource)
                {
                    NodeInspectorPanel.Visibility = Visibility.Visible;
                    InspectorTitle.Text = string.IsNullOrWhiteSpace(selectedNode.SourceSchemeName) ? UiText.T("Text.BlueprintSource") : selectedNode.SourceSchemeName;
                    OpenBlueprintSourceButton.Visibility = Visibility.Visible;
                    OpenBlueprintSourceButton.IsEnabled = _blueprintSourceExists(selectedNode);
                    InspectorRecipeSearchBox.Text = "";
                    TargetOutputBox.Text = "";
                    PriorityBox.IsEnabled = false;
                    OnlyOutputCheckBox.IsEnabled = false;
                    SchemeOutputCheckBox.IsEnabled = false;
                    InspectorStatusPanel.Visibility = Visibility.Visible;
                    UiImageLoader.SetImage(_apiClient, InspectorImage, null);
                    ShowInspectorTab(_activeInspectorTab);
                    InspectorMetricsStack.Children.Clear();
                    foreach (var output in selectedNode.BlueprintOutputs)
                    {
                        InspectorMetricsStack.Children.Add(BuildMetricRow(UiText.T("Text.Output"), output.Name, sub: $"{output.RatePerMinute:g}/min"));
                    }

                    InspectorReadOnly.Text = UiText.T("Text.BlueprintInspectorDescription");
                    InspectorInputs.ItemsSource = selectedNode.BlueprintOutputs.Select(output => UiText.Format("Text.OutputAvailableRate", output.Name, output.RatePerMinute)).ToList();
                    InspectorUnlocks.ItemsSource = new List<string> { UiText.T("Text.None") };
                    return;
                }

                _inspectorViewModel.LoadNode(Catalog, selectedNode);
                var recipe = RecipeForNode(selectedNode);
                var building = PlannerEdgeService.BuildingForNode(Catalog, selectedNode);
                _inspectorRecipes = PlannerEdgeService.RecipesForNode(Catalog, selectedNode);
                NodeInspectorPanel.Visibility = Visibility.Visible;
                InspectorStatusPanel.Visibility = Visibility.Visible;
                ShowInspectorTab(_activeInspectorTab);
                InspectorTitle.Text = recipe?.BuildingName ?? building?.Name ?? UiText.T("Text.UnselectedMachine");
                InspectorRecipeList.ItemsSource = _inspectorRecipes;
                InspectorRecipeList.SelectedItem = recipe;
                InspectorRecipeSearchBox.Text = recipe?.InspectorDisplayName ?? "";
                UiImageLoader.SetImage(_apiClient, InspectorImage, recipe?.BuildingImageUrl ?? building?.ImageUrl);
                OnlyOutputCheckBox.IsEnabled = recipe is not null;
                OnlyOutputCheckBox.IsChecked = selectedNode.OnlyOutput;
                SchemeOutputCheckBox.IsEnabled = recipe is not null;
                SchemeOutputCheckBox.IsChecked = selectedNode.IsSchemeOutput;
                if (readTargetBox)
                {
                    TargetOutputBox.Text = recipe is null
                        ? ""
                        : ProductionAnalysisService.EffectiveMachineCount(selectedNode).ToString(CultureInfo.CurrentCulture);
                }
                SelectPriorityBoxItem(selectedNode.Priority);
                PriorityBox.IsEnabled = !selectedNode.OnlyOutput;

                if (recipe is null)
                {
                    InspectorReadOnly.Text = UiText.T("Text.NoRecipeSelected");
                    return;
                }

                var machines = ProductionAnalysisService.EffectiveMachineCount(selectedNode);
                var outputPerMinute = _calculator.OutputPerMinute(recipe, machines);
                BuildInspectorMetrics(selectedNode, recipe, machines, outputPerMinute);
                InspectorReadOnly.Text = selectedNode.OnlyOutput
                    ? UiText.Format("Text.RecipeBaseInputsBypassed", recipe.Output.QuantityPerMinute, recipe.OriginalRateText)
                    : UiText.Format("Text.RecipeBaseInputsScale", recipe.Output.QuantityPerMinute, recipe.OriginalRateText);
                InspectorInputs.ItemsSource = selectedNode.OnlyOutput
                    ? new List<string> { UiText.T("Text.InputsBypassedOnlyOutput") }
                    : recipe.Inputs
                        .Select(input =>
                        {
                            var required = _calculator.RequiredInputPerMinute(recipe, input, machines);
                            var key = ProductionInputKey.For(selectedNode.Id, input.ItemId);
                            var delivered = _session.ProductionAnalysis.Inputs.TryGetValue(key, out var analysis)
                                ? analysis.DeliveredPerMinute
                                : 0;
                            return delivered + 0.000001 < required
                                ? UiText.Format("Text.InputRequirementShort", input.Name, required, delivered)
                                : UiText.Format("Text.InputRequirement", input.Name, required, delivered);
                        })
                        .ToList();
                InspectorUnlocks.ItemsSource = recipe.UnlockRequirements.Count == 0
                    ? new List<string> { UiText.T("Text.None") }
                    : recipe.UnlockRequirements.Select(item => $"{item.Name}: {item.RequiredQuantity:g}").ToList();
                return;
            }

            InspectorTitle.Text = "";
            UiImageLoader.SetImage(_apiClient, InspectorImage, null);
            InspectorRecipeList.SelectedItem = null;
            InspectorRecipeSearchBox.Text = "";
            TargetOutputBox.Text = "";
            PriorityBox.SelectedItem = null;
            PriorityBox.IsEnabled = true;
            OpenBlueprintSourceButton.Visibility = Visibility.Collapsed;
            OpenBlueprintSourceButton.IsEnabled = false;
            InspectorReadOnly.Text = "";

            if (SelectedEdge is { } edge)
            {
                InspectorTitle.Text = UiText.T("Text.Connection");
                ConnectionInspectorPanel.Visibility = Visibility.Visible;
                ConnectionReadOnly.Text = PlannerEdgeService.EdgeDetail(Scheme, Catalog, _session.CurrentSettings, _calculator, edge, _session.ProductionAnalysis);
                return;
            }

            if (_hasNoCanvasSelection())
            {
                InspectorTitle.Text = UiText.T("Text.Scheme");
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

    private void BuildSchemeSettingsInspector()
    {
        PlannerUnlockService.EnsureSchemeDefaults(Scheme, Catalog);
        BuildCorporationSettings();
        BuildRailTierSettings();
    }

    private void BuildCorporationSettings()
    {
        CorporationSettingsStack.Children.Clear();
        if (Catalog.Corporations.Count == 0)
        {
            CorporationSettingsStack.Children.Add(new TextBlock
            {
                Text = UiText.T("Text.CorporationDataUnavailable"),
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var corporation in Catalog.Corporations.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
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
                Text = UiText.Format("Text.MaxLevel", Math.Max(0, corporation.MaxLevel)),
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });
            row.Children.Add(textPanel);

            var maxLevel = Math.Max(0, corporation.MaxLevel);
            var currentLevel = Scheme.CorporationLevels.TryGetValue(corporation.CorporationId, out var value)
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
        if (Catalog.TransportTiers.Tiers.Count == 0)
        {
            RailTierSettingsStack.Children.Add(new TextBlock
            {
                Text = UiText.T("Text.NoRailTiersConfigured"),
                Foreground = (Brush)Application.Current.FindResource("ThemeSecondaryForegroundBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var maxAvailable = PlannerUnlockService.MaxAvailableRailTier(Catalog, Scheme);
        RailTierSettingsStack.Children.Add(new TextBlock
        {
            Text = maxAvailable is null
                ? UiText.T("Text.NoRailTiersUnlocked")
                : UiText.Format("Text.MaxAvailableRail", maxAvailable.Name, maxAvailable.ItemsPerMinute),
            Foreground = maxAvailable is null
                ? new SolidColorBrush(UiPalette.Shortage)
                : (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var tier in Catalog.TransportTiers.Tiers.OrderBy(item => item.Level))
        {
            var unlocked = PlannerUnlockService.IsRailTierUnlocked(tier, Scheme);
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(8),
                ToolTip = unlocked ? UiText.T("Text.RailAvailableTooltip") : PlannerUnlockService.RailUnlockText(Catalog, tier),
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
                    ? UiText.Format("Text.RailTierAvailable", tier.Name, tier.ItemsPerMinute)
                    : UiText.Format("Text.RailTierLocked", tier.Name, tier.ItemsPerMinute),
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
                Text = UiText.Format("Text.Requires", PlannerUnlockService.RailUnlockText(Catalog, tier)),
                Foreground = new SolidColorBrush(UiPalette.Shortage),
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

        Scheme.CorporationLevels[corporationId] = level;
        PlannerUnlockService.EnsureSchemeDefaults(Scheme, Catalog);
        _session.RequestCanvasRender();
        UpdateInspector();
        _session.RequestStatus(UiText.T("Status.CorporationLevelsUpdated"));
    }

    private void OpenBlueprintSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNode is { } node)
        {
            _openBlueprintSource(node);
        }
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
        if (SelectedNode is not { } node)
        {
            return;
        }

        var current = int.TryParse(TargetOutputBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : ProductionAnalysisService.EffectiveMachineCount(node);
        TargetOutputBox.Text = Math.Max(1, current + delta).ToString(CultureInfo.CurrentCulture);
        TargetOutputBox.CaretIndex = TargetOutputBox.Text.Length;
    }

    private void BuildInspectorMetrics(SchemeNode node, RecipeInfo recipe, int machines, double outputPerMinute)
    {
        InspectorMetricsStack.Children.Clear();

        if (node.OnlyOutput)
        {
            InspectorMetricsStack.Children.Add(BuildMetricRow("Input", "Bypassed"));
        }
        else
        {
            foreach (var input in recipe.Inputs)
            {
                var required = _calculator.RequiredInputPerMinute(recipe, input, machines);
                InspectorMetricsStack.Children.Add(BuildMetricRow(
                    "Input",
                    $"{input.Name}  {input.QuantityPerMinute:g}/min",
                    sub: $"Total {required:g}/min"));
            }
        }

        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Output",
            $"{recipe.Output.Name}  {recipe.Output.QuantityPerMinute:g}/min",
            sub: $"Total {outputPerMinute:g}/min"));

        var building = PlannerEdgeService.BuildingForNode(Catalog, node);
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Power",
            building?.Power is null ? "-" : PlannerMetricService.FormatNodePower(building, 1),
            sub: building?.Power is null ? null : $"Total {PlannerMetricService.FormatNodePower(building, machines)}"));
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Temperature",
            building?.Temperature is null ? "-" : PlannerMetricService.FormatNodeTemperature(building, 1),
            sub: building?.Temperature is null ? null : $"Total {PlannerMetricService.FormatNodeTemperature(building, machines)}"));

        InspectorMetricsStack.Children.Add(BuildMetricRow("Efficiency", "—"));

        var (ratio, isShort) = PlannerNodeMetrics.FeedRatio(_session.ProductionAnalysis, node);
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Utilization",
            $"{ratio * 100:0}%",
            valueBrush: new SolidColorBrush(isShort ? UiPalette.Shortage : UiPalette.SignalGreen)));
        InspectorMetricsStack.Children.Add(BuildMetricRow(
            "Status",
            node.OnlyOutput ? "External source" : isShort ? "Starved" : "Running",
            valueBrush: new SolidColorBrush(isShort ? UiPalette.Shortage : UiPalette.SignalGreen)));
    }

    private static FrameworkElement BuildMetricRow(string label, string value, Brush? valueBrush = null, string? sub = null)
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
}
