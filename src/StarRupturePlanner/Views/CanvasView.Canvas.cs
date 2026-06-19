using System.Globalization;
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

public partial class CanvasView
{
    private void PlannerCanvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ResourceToolboxItem))
            || e.Data.GetDataPresent(typeof(MachineToolboxItem))
            || e.Data.GetDataPresent(typeof(SchemeListItem))
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
            return;
        }

        if (e.Data.GetData(typeof(SchemeListItem)) is SchemeListItem schemeItem)
        {
            AddSchemeOutputNodes(schemeItem, dropPoint);
            e.Handled = true;
        }
    }

    public void AddRecipeNode(RecipeInfo recipe, Point position)
    {
        var node = CreateNode(recipe, position.X, position.Y);
        _scheme.Nodes.Add(node);
        SelectSingleNode(node);
        RenderCanvas();
        UpdateInspector();
    }

    public void AddMachineNode(BuildingInfo building, Point position)
    {
        var node = _canvasViewModel.CreateMachineNode(building, position);
        _scheme.Nodes.Add(node);
        SelectSingleNode(node);
        RenderCanvas();
        UpdateInspector();
    }

    private void AddSchemeOutputNodes(SchemeListItem schemeItem, Point position)
    {
        var outputs = schemeItem.Outputs
            .Where(output => !string.IsNullOrWhiteSpace(output.RecipeKey))
            .ToList();
        if (outputs.Count == 0)
        {
            SetStatus(UiText.Format("Status.SchemeHasNoMarkedOutputs", schemeItem.Name));
            return;
        }

        var node = _canvasViewModel.CreateBlueprintSourceNode(schemeItem, position);
        if (node is null)
        {
            SetStatus(UiText.Format("Status.SchemeOutputsUnavailable", schemeItem.Name));
            return;
        }

        _scheme.Nodes.Add(node);
        SelectSingleNode(node);
        RenderCanvas();
        UpdateInspector();
        SetStatus(UiText.Format("Status.AddedBlueprintSource", schemeItem.Name, node.BlueprintOutputs.Count));
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
        _portViews.Clear();
        _edgeRenderItems.Clear();
        _edgeLayer = null;
        _selectionRectangle = null;

        foreach (var comment in _scheme.Comments)
        {
            AddCommentView(comment);
        }

        AddEdgeLayer();

        foreach (var edge in _scheme.Edges)
        {
            AddEdgeView(edge);
        }

        foreach (var node in _scheme.Nodes)
        {
            AddNodeView(node);
        }

        RefreshEdges(refreshAnalysis: false);
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                RefreshEdges(refreshAnalysis: false);
            }),
            DispatcherPriority.Loaded);
    }

    private void AddEdgeLayer()
    {
        _edgeLayer = new EdgeLayer
        {
            Width = PlannerCanvas.Width,
            Height = PlannerCanvas.Height,
        };
        _edgeLayer.EdgeMouseLeftButtonDown += EdgeLayer_MouseLeftButtonDown;
        PlannerCanvas.Children.Add(_edgeLayer);
        Canvas.SetLeft(_edgeLayer, 0);
        Canvas.SetTop(_edgeLayer, 0);
    }

    private void AddCommentView(SchemeComment comment)
    {
        var root = new Border
        {
            Width = Math.Max(140, comment.Width),
            Height = Math.Max(82, comment.Height),
            Background = ThemeBrush("CommentBackgroundBrush", Color.FromArgb(72, 10, 30, 42)),
            BorderBrush = ThemeBrush("CommentBorderBrush", Color.FromArgb(190, 34, 83, 113)),
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
            Background = ThemeBrush("CommentTitleBackgroundBrush", Color.FromArgb(190, 14, 34, 48)),
            BorderThickness = new Thickness(0),
            Foreground = ThemeBrush("CommentForegroundBrush", Color.FromRgb(230, 246, 255)),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 4, 12, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = comment,
        };
        title.TextChanged += CommentTitle_TextChanged;
        title.PreviewKeyDown += CommentTitle_PreviewKeyDown;
        grid.Children.Add(title);

        var body = new Border
        {
            Background = ThemeBrush("CommentBodyBackgroundBrush", Color.FromArgb(48, 10, 18, 24)),
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
                Stroke = ThemeBrush("CommentForegroundBrush", Color.FromArgb(210, 225, 225, 225)),
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
        if (node.NodeType == SchemeNodeType.BlueprintSource)
        {
            AddBlueprintSourceNodeView(node);
            return;
        }

        var recipe = RecipeForNode(node);
        var building = BuildingForNode(node);
        var root = new Border
        {
            Width = 470,
            MinHeight = 112,
            Background = new LinearGradientBrush(
                ThemeColor("NodeCardTopBrush", Color.FromArgb(242, 17, 27, 35)),
                ThemeColor("NodeCardBottomBrush", Color.FromArgb(232, 7, 15, 20)),
                new Point(0, 0),
                new Point(1, 1)),
            BorderBrush = ThemeBrush("NodeCardBorderBrush", GraphiteLineColor),
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
                Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B),
                new Point(0, 0),
                new Point(1, 0)),
        };
        header.SetValue(Grid.RowProperty, 1);
        var imageFrame = new Border
        {
            Width = 58,
            Height = 58,
            Margin = new Thickness(12, 10, 12, 10),
            Background = ThemeBrush("NodeCardImageBrush", Color.FromRgb(13, 24, 32)),
            BorderBrush = ThemeBrush("NodeCardImageBorderBrush", Color.FromRgb(42, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
        };
        var image = new Image { Width = 50, Height = 50, Stretch = Stretch.Uniform };
        SetImage(image, recipe?.BuildingImageUrl ?? building?.ImageUrl);
        var imageHost = new Grid();
        imageHost.Children.Add(image);
        var machineCount = ProductionAnalysisService.EffectiveMachineCount(node);
        if (machineCount > 1)
        {
            imageHost.Children.Add(CreateCountBadge(machineCount));
        }
        imageFrame.Child = imageHost;
        DockPanel.SetDock(imageFrame, Dock.Left);
        header.Children.Add(imageFrame);
        var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = recipe?.BuildingName ?? building?.Name ?? UiText.T("Text.UnselectedMachine"),
            Foreground = CardTextBrush(),
            FontSize = CardFontSize(4),
            FontFamily = CardFontFamily(),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = recipe is null
                ? UiText.T("Text.RecipeNotSelected")
                : $"{recipe.Output.Name}  {NodeOutputRate(node, recipe):g}/min",
            Foreground = CardTextBrush(0.72),
            FontSize = CardFontSize(),
            FontFamily = CardFontFamily(),
            TextWrapping = TextWrapping.Wrap,
        });
        var badges = CreateNodeBadges(node);
        if (badges is not null)
        {
            titlePanel.Children.Add(badges);
        }

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
                    ? UiText.T("Text.LockedByCorporations")
                    : node.OnlyOutput
                        ? UiText.Format("Text.CountSource", ProductionAnalysisService.EffectiveMachineCount(node))
                        : UiText.Format("Text.CountPriority", ProductionAnalysisService.EffectiveMachineCount(node), PriorityDisplay(node.Priority)),
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
                Text = UiText.T("Text.SelectRecipeToActivatePorts"),
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

            if (!node.OnlyOutput)
            {
                var inputs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 10, 8, 12) };
                inputs.Children.Add(CreateCardSectionLabel(UiText.T("Text.InputsUpper")));
                var inputPorts = PlannerPortOrderService.OrderedInputs(node, recipe);
                var inputIds = inputPorts.Select(input => input.ItemId).ToList();
                foreach (var input in inputPorts)
                {
                    inputs.Children.Add(CreatePortVisual(node, input, "input", inputIds.Count > 1, inputIds));
                }

                var divider = new Border
                {
                    Width = 1,
                    Background = ThemeBrush("NodeCardDividerBrush", Color.FromArgb(120, 38, 52, 61)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };

                Grid.SetColumn(inputs, 0);
                Grid.SetColumn(divider, 1);
                body.Children.Add(inputs);
                body.Children.Add(divider);
            }

            var output = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 10, 14, 12) };
            output.Children.Add(CreateCardSectionLabel(UiText.T("Text.OutputsUpper")));
            var outputPorts = PlannerPortOrderService.OrderedOutputs(node, recipe);
            var outputIds = outputPorts.Select(item => item.ItemId).ToList();
            foreach (var outputPort in outputPorts)
            {
                output.Children.Add(CreatePortVisual(node, outputPort, "output", outputIds.Count > 1, outputIds));
            }

            Grid.SetColumn(output, 2);
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

    private void AddBlueprintSourceNodeView(SchemeNode node)
    {
        var root = new Border
        {
            Width = 470,
            MinHeight = 112,
            Background = new LinearGradientBrush(
                ThemeColor("NodeCardTopBrush", Color.FromArgb(242, 12, 26, 36)),
                ThemeColor("NodeCardBottomBrush", Color.FromArgb(232, 5, 12, 18)),
                new Point(0, 0),
                new Point(1, 1)),
            BorderBrush = ThemeBrush("NodeCardBorderBrush", GraphiteLineColor),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Tag = node,
        };
        ApplyNodeSelectionVisual(root, _selectedNodeIds.Contains(node.Id));

        root.MouseLeftButtonDown += Node_MouseLeftButtonDown;
        root.MouseMove += Node_MouseMove;
        root.MouseLeftButtonUp += Node_MouseLeftButtonUp;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = grid;
        root.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                grid.Clip = new RectangleGeometry(new Rect(e.NewSize), 8, 8);
            }
        };

        var accent = new Border { Height = 5, Background = new SolidColorBrush(Color.FromRgb(10, 132, 255)) };
        Grid.SetRow(accent, 0);
        grid.Children.Add(accent);

        var header = new DockPanel
        {
            LastChildFill = true,
            Background = new LinearGradientBrush(
                Color.FromArgb(125, 10, 132, 255),
                Color.FromArgb(0, 10, 132, 255),
                new Point(0, 0),
                new Point(1, 0)),
        };
        Grid.SetRow(header, 1);

        var iconFrame = new Border
        {
            Width = 58,
            Height = 58,
            Margin = new Thickness(12, 10, 12, 10),
            Background = ThemeBrush("NodeCardImageBrush", Color.FromRgb(13, 24, 32)),
            BorderBrush = ThemeBrush("NodeCardImageBorderBrush", Color.FromRgb(42, 60, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new TextBlock
            {
                Text = "▧",
                Foreground = new SolidColorBrush(OutputPortColor),
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(iconFrame, Dock.Left);
        header.Children.Add(iconFrame);

        var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(node.SourceSchemeName) ? UiText.T("Text.BlueprintSource") : node.SourceSchemeName,
            Foreground = CardTextBrush(),
            FontSize = CardFontSize(4),
            FontFamily = CardFontFamily(),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = UiText.Format("Text.ExternalOutputsCount", node.BlueprintOutputs.Count),
            Foreground = CardTextBrush(0.72),
            FontSize = CardFontSize(),
            FontFamily = CardFontFamily(),
        });
        titlePanel.Children.Add(CreateNodeBadge(UiText.T("Text.BlueprintSource"), OutputPortColor));
        header.Children.Add(titlePanel);
        grid.Children.Add(header);

        var body = new Grid { MinHeight = 70 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
        var note = new TextBlock
        {
            Text = UiText.T("Text.BlueprintSourceDescription"),
            Foreground = CardTextBrush(0.62),
            FontFamily = CardFontFamily(),
            FontSize = CardFontSize(-1),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 12, 10, 12),
            VerticalAlignment = VerticalAlignment.Center,
        };
        body.Children.Add(note);

        var outputs = new StackPanel { Margin = new Thickness(8, 10, 14, 12), VerticalAlignment = VerticalAlignment.Center };
        outputs.Children.Add(CreateCardSectionLabel(UiText.T("Text.OutputsUpper")));
        var outputPorts = PlannerPortOrderService.OrderedBlueprintOutputs(node);
        var outputIds = outputPorts.Select(output => output.ItemId).ToList();
        foreach (var output in outputPorts)
        {
            outputs.Children.Add(CreatePortVisual(node, BlueprintPortToRecipePort(output), "output", outputIds.Count > 1, outputIds));
        }

        Grid.SetColumn(outputs, 1);
        body.Children.Add(outputs);
        Grid.SetRow(body, 2);
        grid.Children.Add(body);

        Canvas.SetLeft(root, node.X);
        Canvas.SetTop(root, node.Y);
        PlannerCanvas.Children.Add(root);
        _nodeViews[node.Id] = root;
    }

    private static RecipePortInfo BlueprintPortToRecipePort(BlueprintOutputPort output)
    {
        return new RecipePortInfo
        {
            ItemId = output.ItemId,
            Name = output.Name,
            ImageUrl = output.ImageUrl,
            QuantityPerMinute = output.RatePerMinute,
        };
    }

    private static void ApplyNodeSelectionVisual(Border border, bool selected)
    {
        border.BorderBrush = selected
            ? new SolidColorBrush(OutputPortColor)
            : new SolidColorBrush(GraphiteLineColor);
        border.BorderThickness = new Thickness(2.5);
        border.Effect = null;
    }

    private static void ApplyCommentSelectionVisual(Border border, bool selected)
    {
        border.BorderBrush = selected
            ? new SolidColorBrush(OutputPortColor)
            : ThemeBrush("CommentBorderBrush", Color.FromArgb(170, 95, 105, 112));
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
                handle.Stroke = selected ? new SolidColorBrush(OutputPortColor) : ThemeBrush("NodeCardTopBrush", Color.FromRgb(5, 12, 17));
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

    private FrameworkElement? CreateNodeBadges(SchemeNode node)
    {
        var surplus = NodeSurplus(node);
        var hasSurplus = surplus > 0.0001;
        if (!node.OnlyOutput && !node.IsSchemeOutput && !hasSurplus)
        {
            return null;
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
        };

        if (node.OnlyOutput)
        {
            panel.Children.Add(CreateNodeBadge(UiText.T("Text.OnlyOutput"), OutputPortColor));
        }

        if (node.IsSchemeOutput)
        {
            panel.Children.Add(CreateNodeBadge(UiText.T("Text.SchemeOutput"), SignalGreenColor));
        }

        if (hasSurplus)
        {
            var amber = ((SolidColorBrush)Application.Current.FindResource("ReactorOrangeBrush")).Color;
            panel.Children.Add(CreateNodeBadge("▲ " + UiText.Format("Text.Surplus", surplus), amber));
        }

        return panel;
    }

    // Output the node produces beyond what its downstream consumers actually pull
    // (0 = fully consumed). Uses the recipe's primary output item.
    private double NodeSurplus(SchemeNode node)
    {
        var recipe = RecipeForNode(node);
        if (recipe is null)
        {
            return 0;
        }

        var output = NodeOutputRate(node, recipe);
        if (output <= 0.0001)
        {
            return 0;
        }

        var outgoing = _scheme.Edges
            .Where(edge => string.Equals(edge.SourceNodeId, node.Id, StringComparison.Ordinal)
                && string.Equals(edge.SourceItemId, recipe.Output.ItemId, StringComparison.Ordinal))
            .ToList();

        // Unconnected output isn't surplus — it just feeds nothing.
        if (outgoing.Count == 0)
        {
            return 0;
        }

        var delivered = outgoing.Sum(edge => _productionAnalysis.EdgeDeliveries.GetValueOrDefault(edge.Id));
        return Math.Max(0, output - delivered);
    }

    private FrameworkElement CreateNodeBadge(string text, Color accent)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(accent),
                FontFamily = CardFontFamily(),
                FontSize = CardFontSize(-2),
                FontWeight = FontWeights.SemiBold,
            },
        };
    }

    // Glanceable "xN" multiplier shown over the machine image when count > 1.
    private FrameworkElement CreateCountBadge(int count)
    {
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -3, -3),
            Background = new SolidColorBrush(ReactorOrangeColor),
            BorderBrush = new SolidColorBrush(Color.FromRgb(13, 24, 32)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 0, 5, 1),
            MinWidth = 18,
            Child = new TextBlock
            {
                Text = $"×{count}",
                Foreground = Brushes.White,
                FontFamily = CardFontFamily(),
                FontSize = CardFontSize(-1),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
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
            BorderBrush = ThemeBrush("NodeCardDividerBrush", Color.FromArgb(120, 38, 52, 61)),
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
        power.Text = $"{UiText.T("Metric.TotalPower")}  {PlannerMetricService.FormatNodePower(building, machines)}";
        dock.Children.Add(power);

        var temperature = new TextBlock
        {
            Text = $"{UiText.T("Metric.Temperature")}  {PlannerMetricService.FormatNodeTemperature(building, machines)}",
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
            Text = $"{UiText.T("Text.Issues")} ({shortInputs.Count})",
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
                Text = $"{input.ItemName}: {input.DeliveredPerMinute:g}/{input.RequiredPerMinute:g}/min ({deficit:g} {UiText.T("Text.Short")})",
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
        => PlannerNodeMetrics.FeedRatio(_productionAnalysis, node);

    private double NodeOutputRate(SchemeNode node, RecipeInfo recipe)
    {
        return _calculator.OutputPerMinute(recipe, ProductionAnalysisService.EffectiveMachineCount(node));
    }

    private double PortRate(SchemeNode node, RecipePortInfo port, string direction)
    {
        if (node.NodeType == SchemeNodeType.BlueprintSource && direction == "output")
        {
            return node.BlueprintOutputs.FirstOrDefault(output => output.ItemId == port.ItemId)?.RatePerMinute ?? 0;
        }

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
        if (node.NodeType == SchemeNodeType.BlueprintSource)
        {
            return direction == "output"
                && _catalog.Recipes.Any(recipe =>
                    recipe.Inputs.Any(input => input.ItemId == port.ItemId)
                    && PlannerUnlockService.IsBuildingUnlocked(_catalog, _scheme, recipe.BuildingId));
        }

        if (node.OnlyOutput && direction == "input")
        {
            return false;
        }

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
        if (node?.NodeType == SchemeNodeType.BlueprintSource)
        {
            var blueprintPort = node.BlueprintOutputs.FirstOrDefault(output => output.ItemId == reference.ItemId);
            return blueprintPort is not null && IsPortAvailableForConnection(node, BlueprintPortToRecipePort(blueprintPort), reference.Direction);
        }

        var recipe = RecipeForNode(node);
        if (node is null || recipe is null)
        {
            return false;
        }

        if (node.OnlyOutput && reference.Direction == "input")
        {
            return false;
        }

        var port = reference.Direction == "input"
            ? recipe.Inputs.FirstOrDefault(input => input.ItemId == reference.ItemId)
            : recipe.Output.ItemId == reference.ItemId ? recipe.Output : null;
        return port is not null && IsPortAvailableForConnection(node, port, reference.Direction);
    }

    private FrameworkElement CreatePortVisual(
        SchemeNode node,
        RecipePortInfo port,
        string direction,
        bool canReorder = false,
        IReadOnlyList<string>? visibleItemIds = null)
    {
        var rate = PortRate(node, port, direction);
        var available = IsPortAvailableForConnection(node, port, direction);
        var portReference = new PortReference(node.Id, direction, port.ItemId);
        var orderReference = new PortOrderReference(node.Id, direction, port.ItemId);
        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            Tag = orderReference,
            ToolTip = available
                ? $"{port.Name} {rate:g}/min"
                : $"{port.Name} {UiText.T("Text.NotAvailableForConnection")}",
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 15,
            Height = 15,
            Fill = available ? PortBrush(direction) : new SolidColorBrush(LockedPortColor),
            Stroke = ThemeBrush("NodeCardTopBrush", Color.FromRgb(5, 12, 17)),
            StrokeThickness = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = available ? Cursors.Hand : Cursors.No,
            Tag = portReference,
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
        _portViews[portReference] = dot;

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
            Background = ThemeBrush("NodeCardImageBrush", Color.FromRgb(9, 18, 24)),
            BorderBrush = ThemeBrush("NodeCardImageBorderBrush", Color.FromRgb(30, 45, 55)),
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
            Grid.SetColumn(dot, 3);
        }

        if (canReorder && visibleItemIds is not null)
        {
            var grip = CreatePortOrderGrip(orderReference, visibleItemIds);
            Grid.SetColumn(grip, direction == "input" ? 2 : 2);
            row.Children.Add(grip);
        }

        row.Children.Add(dot);
        row.Children.Add(info);
        return row;
    }

    private FrameworkElement CreatePortOrderGrip(PortOrderReference reference, IReadOnlyList<string> visibleItemIds)
    {
        var iconBrush = CardTextBrush(0.62);
        var icon = new StackPanel
        {
            Width = 10,
            Height = 10,
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (var index = 0; index < 3; index++)
        {
            icon.Children.Add(new Rectangle
            {
                Width = 10,
                Height = 1.5,
                RadiusX = 0.75,
                RadiusY = 0.75,
                Fill = iconBrush,
                Margin = index == 0 ? new Thickness(0) : new Thickness(0, 2, 0, 0),
            });
        }

        var grip = new Border
        {
            Width = 16,
            Height = 22,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNS,
            Tag = (Reference: reference, VisibleItemIds: visibleItemIds.ToList()),
            ToolTip = UiText.T("Text.ReorderPort"),
            Child = icon,
        };
        grip.PreviewMouseLeftButtonDown += PortOrderGrip_MouseLeftButtonDown;
        return grip;
    }

    private void PortOrderGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.Tag is not ValueTuple<PortOrderReference, List<string>> tag)
        {
            return;
        }

        FocusCanvasViewport();
        _portOrderDrag = new PortOrderDrag(tag.Item1, tag.Item2);
        CanvasViewport.CaptureMouse();
        e.Handled = true;
    }

    private void UpdatePortOrderDrag(Point canvasPoint)
    {
        if (_portOrderDrag is null)
        {
            return;
        }

        var target = PortOrderReferenceAt(canvasPoint);
        if (target is null
            || !string.Equals(target.NodeId, _portOrderDrag.Reference.NodeId, StringComparison.Ordinal)
            || !string.Equals(target.Direction, _portOrderDrag.Reference.Direction, StringComparison.Ordinal)
            || string.Equals(target.ItemId, _portOrderDrag.Reference.ItemId, StringComparison.Ordinal))
        {
            return;
        }

        var node = _scheme.Nodes.FirstOrDefault(item => string.Equals(item.Id, target.NodeId, StringComparison.Ordinal));
        if (node is null)
        {
            return;
        }

        var visibleIds = CurrentVisiblePortIds(node, target.Direction);
        var targetIndex = visibleIds.FindIndex(id => string.Equals(id, target.ItemId, StringComparison.Ordinal));
        if (targetIndex < 0)
        {
            return;
        }

        PlannerPortOrderService.MovePort(
            node,
            target.Direction,
            _portOrderDrag.Reference.ItemId,
            targetIndex,
            visibleIds);
        _portOrderDrag = new PortOrderDrag(_portOrderDrag.Reference, CurrentVisiblePortIds(node, target.Direction));
        RenderCanvas();
        UpdateInspector();
    }

    private void EndPortOrderDrag()
    {
        _portOrderDrag = null;
        if (CanvasViewport.IsMouseCaptured)
        {
            CanvasViewport.ReleaseMouseCapture();
        }
    }

    private PortOrderReference? PortOrderReferenceAt(Point canvasPoint)
    {
        var hit = VisualTreeHelper.HitTest(PlannerCanvas, canvasPoint)?.VisualHit as DependencyObject;
        while (hit is not null)
        {
            if (hit is FrameworkElement { Tag: PortOrderReference reference })
            {
                return reference;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    private List<string> CurrentVisiblePortIds(SchemeNode node, string direction)
    {
        if (direction == "input")
        {
            var recipe = RecipeForNode(node);
            return recipe is null
                ? []
                : PlannerPortOrderService.OrderedInputs(node, recipe).Select(input => input.ItemId).ToList();
        }

        if (node.NodeType == SchemeNodeType.BlueprintSource)
        {
            return PlannerPortOrderService.OrderedBlueprintOutputs(node).Select(output => output.ItemId).ToList();
        }

        var outputRecipe = RecipeForNode(node);
        return outputRecipe is null
            ? []
            : PlannerPortOrderService.OrderedOutputs(node, outputRecipe).Select(output => output.ItemId).ToList();
    }

    private void AddEdgeView(SchemeEdge edge)
    {
        var visual = new EdgeVisual();
        _edgeViews[edge.Id] = visual;

        for (var index = 0; index < edge.RoutePoints.Count; index++)
        {
            var handle = CreateRoutePointHandle(edge, index);
            visual.RoutePointHandles.Add(handle);
            PlannerCanvas.Children.Add(handle);
        }
    }

    private void RefreshEdges(
        IEnumerable<string>? edgeIds = null,
        bool refreshAnalysis = true,
        bool updateSelectionVisuals = true)
    {
        if (refreshAnalysis)
        {
            MigrateAndAnalyzeScheme();
        }

        var edgeFilter = edgeIds is null ? null : new HashSet<string>(edgeIds, StringComparer.Ordinal);
        foreach (var edge in _scheme.Edges)
        {
            if (edgeFilter is not null && !edgeFilter.Contains(edge.Id))
            {
                continue;
            }

            if (!_edgeViews.TryGetValue(edge.Id, out var visual))
            {
                continue;
            }

            var sourcePoint = GetPortPoint(edge.SourceNodeId, "output", edge.SourceItemId);
            var targetPoint = GetPortPoint(edge.TargetNodeId, "input", edge.TargetItemId);
            if (sourcePoint is null || targetPoint is null)
            {
                _edgeRenderItems[edge.Id] = CreateInvalidEdgeRenderItem(edge);
                continue;
            }

            var routePoints = CanvasGeometryService.EdgePoints(edge, sourcePoint.Value, targetPoint.Value);
            var isValid = IsEdgeValid(edge);
            var isShort = IsEdgeShort(edge);
            var labelPlacement = CanvasGeometryService.LabelPlacementAboveLine(routePoints);
            _edgeRenderItems[edge.Id] = new EdgeRenderItem(
                edge.Id,
                routePoints,
                EdgeLabel(edge),
                labelPlacement.Position,
                labelPlacement.AngleDegrees,
                EdgeStrokeColor(edge, isValid),
                isValid && !isShort ? BrushColor(CardTextBrush(), Color.FromRgb(244, 240, 232)) : ShortageColor,
                EdgeLabelBackgroundColor(),
                CardFontFamily(),
                CardFontSize(-1),
                isValid);

            for (var index = 0; index < visual.RoutePointHandles.Count && index < edge.RoutePoints.Count; index++)
            {
                var point = edge.RoutePoints[index];
                Canvas.SetLeft(visual.RoutePointHandles[index], point.X - visual.RoutePointHandles[index].Width / 2);
                Canvas.SetTop(visual.RoutePointHandles[index], point.Y - visual.RoutePointHandles[index].Height / 2);
            }
        }

        _edgeLayer?.SetEdges(_edgeRenderItems.Values.ToList());

        if (updateSelectionVisuals)
        {
            UpdateSelectionVisuals();
        }
    }

    private EdgeRenderItem CreateInvalidEdgeRenderItem(SchemeEdge edge)
    {
        return new EdgeRenderItem(
            edge.Id,
            [],
            UiText.T("Text.InvalidConnection"),
            new Point(),
            0,
            ShortageColor,
            ShortageColor,
            EdgeLabelBackgroundColor(),
            CardFontFamily(),
            CardFontSize(-1),
            false);
    }

    private void MigrateAndAnalyzeScheme()
    {
        if (_catalog.Recipes.Count > 0)
        {
            SchemeMigrationService.Migrate(_scheme, _catalog, _calculator);
        }

        _productionAnalysis = ProductionAnalysisService.Analyze(_scheme, _catalog, _calculator);
        _session.ProductionAnalysis = _productionAnalysis;
        UpdateSurplusPills();
    }

    private void UpdateSurplusPills()
    {
        if (SurplusPills is null)
        {
            return;
        }

        SurplusPills.Children.Clear();
        var any = false;
        foreach (var node in _scheme.Nodes)
        {
            var surplus = NodeSurplus(node);
            if (surplus <= 0.0001)
            {
                continue;
            }

            SurplusPills.Children.Add(CreateSurplusPill(node, surplus));
            any = true;
        }

        SurplusPillsBar.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement CreateSurplusPill(SchemeNode node, double surplus)
    {
        var amber = ((SolidColorBrush)Application.Current.FindResource("ReactorOrangeBrush")).Color;
        var name = RecipeForNode(node)?.BuildingName ?? BuildingForNode(node)?.Name ?? UiText.T("Text.UnselectedMachine");

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = (Brush)Application.Current.FindResource("ThemeForegroundBrush"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = $"  +{surplus:g}/min",
            Foreground = new SolidColorBrush(amber),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(34, amber.R, amber.G, amber.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, amber.R, amber.G, amber.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Tag = node,
            Child = content,
            ToolTip = UiText.Format("Text.Surplus", surplus),
        };
        pill.MouseLeftButtonUp += SurplusPill_MouseLeftButtonUp;
        return pill;
    }

    private void SurplusPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is SchemeNode node)
        {
            SelectSingleNode(node);
            UpdateInspector();
            UpdateSelectionVisuals();
            FocusNode(node);
            e.Handled = true;
        }
    }

    // Centers the canvas viewport on the given node at the current zoom.
    private void FocusNode(SchemeNode node)
    {
        var viewportWidth = CanvasFrame.ActualWidth;
        var viewportHeight = CanvasFrame.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        double cardWidth = 470;
        double cardHeight = 130;
        if (_nodeViews.TryGetValue(node.Id, out var view))
        {
            if (view.ActualWidth > 0)
            {
                cardWidth = view.ActualWidth;
            }

            if (view.ActualHeight > 0)
            {
                cardHeight = view.ActualHeight;
            }
        }

        // Focus at 100% zoom with the node centered in the viewport (screen = world*1 + translate).
        var centerX = node.X + cardWidth / 2;
        var centerY = node.Y + cardHeight / 2;
        AnimateCanvasView(1.0, viewportWidth / 2 - centerX, viewportHeight / 2 - centerY);
    }

    // Animate back to 100% zoom, keeping whatever is currently at the viewport center centered.
    private void ResetZoom()
    {
        var viewportWidth = CanvasFrame.ActualWidth;
        var viewportHeight = CanvasFrame.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var scale = CanvasScale.ScaleX <= 0 ? 1 : CanvasScale.ScaleX;
        var worldCenterX = (viewportWidth / 2 - CanvasTranslate.X) / scale;
        var worldCenterY = (viewportHeight / 2 - CanvasTranslate.Y) / scale;
        AnimateCanvasView(1.0, viewportWidth / 2 - worldCenterX, viewportHeight / 2 - worldCenterY);
    }

    // Smoothly glides the canvas zoom and offset to the targets.
    private void AnimateCanvasView(double targetScale, double targetX, double targetY)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(320));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animateScaleX = new DoubleAnimation(targetScale, duration) { EasingFunction = ease };
        var animateScaleY = new DoubleAnimation(targetScale, duration) { EasingFunction = ease };
        var animateX = new DoubleAnimation(targetX, duration) { EasingFunction = ease };
        var animateY = new DoubleAnimation(targetY, duration) { EasingFunction = ease };

        // On completion, drop each animation and write the concrete value so manual
        // pan/zoom can take over again afterwards.
        animateScaleX.Completed += (_, _) =>
        {
            CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            CanvasScale.ScaleX = targetScale;
            UpdateZoomText();
        };
        animateScaleY.Completed += (_, _) =>
        {
            CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CanvasScale.ScaleY = targetScale;
        };
        animateX.Completed += (_, _) =>
        {
            CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            CanvasTranslate.X = targetX;
        };
        animateY.Completed += (_, _) =>
        {
            CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            CanvasTranslate.Y = targetY;
        };

        CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, animateScaleX);
        CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, animateScaleY);
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, animateX);
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, animateY);
    }

    // Freezes any in-flight focus/zoom animation at its current state so a manual
    // pan/zoom takes over without snapping.
    private void StopCanvasTranslateAnimation()
    {
        var scaleX = CanvasScale.ScaleX;
        var scaleY = CanvasScale.ScaleY;
        var currentX = CanvasTranslate.X;
        var currentY = CanvasTranslate.Y;
        CanvasScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CanvasScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CanvasTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CanvasTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        CanvasScale.ScaleX = scaleX;
        CanvasScale.ScaleY = scaleY;
        CanvasTranslate.X = currentX;
        CanvasTranslate.Y = currentY;
        UpdateZoomText();
    }

    private void SurplusPills_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
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

    private Color EdgeStrokeColor(SchemeEdge edge, bool isValid)
    {
        return !isValid || IsEdgeShort(edge) ? ShortageColor : OutputPortColor;
    }

    private static Color BrushColor(Brush brush, Color fallback)
    {
        return brush is SolidColorBrush solid ? solid.Color : fallback;
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

        var node = _scheme.Nodes.FirstOrDefault(item => item.Id == nodeId);
        if (node is null)
        {
            return null;
        }

        var handle = _portViews.GetValueOrDefault(new PortReference(nodeId, direction, itemId));
        if (handle is null || !handle.IsVisible)
        {
            if (node.OnlyOutput && direction == "input")
            {
                return new Point(
                    node.X,
                    node.Y + Math.Max(0, nodeView.ActualHeight) / 2);
            }

            return null;
        }

        var localPoint = handle.TransformToAncestor(nodeView)
            .Transform(new Point(handle.ActualWidth / 2, handle.ActualHeight / 2));
        return new Point(node.X + localPoint.X, node.Y + localPoint.Y);
    }

    public void ClearSelection()
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
        _groupDragEdgeIds.Clear();

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

        foreach (var edge in _scheme.Edges)
        {
            if (_groupDragNodeStarts.ContainsKey(edge.SourceNodeId)
                || _groupDragNodeStarts.ContainsKey(edge.TargetNodeId)
                || _groupDragRoutePointStarts.Keys.Any(reference => reference.EdgeId == edge.Id))
            {
                _groupDragEdgeIds.Add(edge.Id);
            }
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
            UpdateCommentDragVisual(comment.Id, deltaX, deltaY);
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
            UpdateNodeDragVisual(node.Id, deltaX, deltaY);
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
        _groupDragEdgeIds.Clear();
    }

    private void RefreshDraggedEdges(bool immediate = false)
    {
        if (_groupDragEdgeIds.Count == 0)
        {
            return;
        }

        foreach (var edgeId in _groupDragEdgeIds)
        {
            _pendingDragEdgeIds.Add(edgeId);
        }

        if (immediate)
        {
            if (_dragEdgeRefreshScheduled)
            {
                CompositionTarget.Rendering -= DragEdgeRefresh_Rendering;
                _dragEdgeRefreshScheduled = false;
            }

            FlushDraggedEdgeRefresh();
            return;
        }

        if (_dragEdgeRefreshScheduled)
        {
            return;
        }

        _dragEdgeRefreshScheduled = true;
        CompositionTarget.Rendering += DragEdgeRefresh_Rendering;
    }

    private void DragEdgeRefresh_Rendering(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= DragEdgeRefresh_Rendering;
        _dragEdgeRefreshScheduled = false;
        FlushDraggedEdgeRefresh();
    }

    private void FlushDraggedEdgeRefresh()
    {
        if (_pendingDragEdgeIds.Count == 0)
        {
            return;
        }

        var edgeIds = _pendingDragEdgeIds.ToArray();
        _pendingDragEdgeIds.Clear();
        RefreshEdges(edgeIds, refreshAnalysis: false, updateSelectionVisuals: false);
    }

    private void UpdateNodeDragVisual(string nodeId, double offsetX, double offsetY)
    {
        if (_nodeViews.TryGetValue(nodeId, out var view))
        {
            view.RenderTransform = new TranslateTransform(offsetX, offsetY);
        }
    }

    private void UpdateCommentDragVisual(string commentId, double offsetX, double offsetY)
    {
        if (_commentViews.TryGetValue(commentId, out var view))
        {
            view.RenderTransform = new TranslateTransform(offsetX, offsetY);
        }
    }

    private void UpdateNodeViewPosition(SchemeNode node)
    {
        if (!_nodeViews.TryGetValue(node.Id, out var view))
        {
            return;
        }

        view.RenderTransform = null;
        Canvas.SetLeft(view, node.X);
        Canvas.SetTop(view, node.Y);
    }

    private void UpdateCommentViewPosition(SchemeComment comment)
    {
        if (!_commentViews.TryGetValue(comment.Id, out var view))
        {
            return;
        }

        view.RenderTransform = null;
        Canvas.SetLeft(view, comment.X);
        Canvas.SetTop(view, comment.Y);
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

    private void FocusCanvasViewport()
    {
        if (Keyboard.FocusedElement is TextBox { Tag: SchemeComment })
        {
            CanvasViewport.Focus();
        }
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
            Stroke = ThemeBrush("NodeCardTopBrush", Color.FromRgb(5, 12, 17)),
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

        FocusCanvasViewport();

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
        RefreshDraggedEdges();
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
            RefreshDraggedEdges(immediate: true);
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

    private void CommentTitle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        FocusCanvasViewport();
        e.Handled = true;
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

        FocusCanvasViewport();

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
        RefreshDraggedEdges();
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
            RefreshDraggedEdges(immediate: true);
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

    private void EdgeLayer_MouseLeftButtonDown(object? sender, EdgeLayerMouseEventArgs e)
    {
        var edge = _scheme.Edges.FirstOrDefault(item => item.Id == e.EdgeId);
        if (edge is null)
        {
            return;
        }

        FocusCanvasViewport();

        if (e.OriginalEventArgs.ClickCount == 2)
        {
            AddRoutePoint(edge, e.OriginalEventArgs.GetPosition(PlannerCanvas));
            e.OriginalEventArgs.Handled = true;
            return;
        }

        SelectSingleEdge(edge);
        UpdateInspector();
        UpdateSelectionVisuals();
        e.OriginalEventArgs.Handled = true;
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

        FocusCanvasViewport();

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
        RefreshDraggedEdges();
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
            RefreshDraggedEdges(immediate: true);
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

        FocusCanvasViewport();

        if (!IsPortReferenceAvailable(port))
        {
            SetStatus(UiText.Format("Status.ResourceNotAvailableForConnection", UiText.T("Text.NotAvailableForConnection")));
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
        Mouse.Capture(CanvasViewport);
        e.Handled = true;
    }

    private void PlannerCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == GridInputLayer)
        {
            FocusCanvasViewport();
            ClearSelection();
            _isSelecting = true;
            _selectionStart = e.GetPosition(PlannerCanvas);
            ShowSelectionRectangle(_selectionStart, _selectionStart);
            CanvasViewport.CaptureMouse();
            UpdateInspector();
            UpdateSelectionVisuals();
            e.Handled = true;
        }
    }

    private void PlannerCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != GridInputLayer || _connectionDrag is not null || _isSelecting || _isPanning)
        {
            return;
        }

        ClearSelection();
        _isCreatingComment = true;
        _commentStart = e.GetPosition(PlannerCanvas);
        ShowSelectionRectangle(_commentStart, _commentStart);
        CanvasViewport.CaptureMouse();
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

        StopCanvasTranslateAnimation();
        _isPanning = true;
        _panStartMouse = e.GetPosition(this);
        _panStartOffset = new Point(CanvasTranslate.X, CanvasTranslate.Y);
        CanvasViewport.Cursor = Cursors.SizeAll;
        CanvasViewport.CaptureMouse();
        e.Handled = true;
    }

    private void PlannerCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_portOrderDrag is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdatePortOrderDrag(e.GetPosition(PlannerCanvas));
            return;
        }

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

    private void PlannerCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_portOrderDrag is not null)
        {
            EndPortOrderDrag();
            e.Handled = true;
            return;
        }

        if (_connectionDrag is not null)
        {
            var drag = _connectionDrag;
            _connectionDrag = null;
            Mouse.Capture(null);
            PlannerCanvas.Children.Remove(drag.Path);

            // Capture is on the input layer, so e.OriginalSource isn't the dropped port.
            // Hit-test the content layer at the drop point to find the target port.
            var dropPoint = e.GetPosition(PlannerCanvas);
            var hit = VisualTreeHelper.HitTest(PlannerCanvas, dropPoint)?.VisualHit as DependencyObject;
            var targetPort = FindAncestor<FrameworkElement>(hit)?.Tag as PortReference;
            if (targetPort is not null && TryCreateEdge(drag.Port, targetPort))
            {
                RenderCanvas();
                return;
            }

            _suggestionCanvasPoint = e.GetPosition(PlannerCanvas);
            RunUiAsync(() => ShowSuggestionsAsync(drag.Port, _suggestionCanvasPoint), "MainWindow.ShowSuggestions");
            return;
        }

        if (_isSelecting)
        {
            SelectInsideRectangle(new Rect(_selectionStart, e.GetPosition(PlannerCanvas)));
            HideSelectionRectangle();
            _isSelecting = false;
            CanvasViewport.ReleaseMouseCapture();
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
        CanvasViewport.ReleaseMouseCapture();

        if (rect.Width >= 80 && rect.Height >= 50)
        {
            var snappedTopLeft = _layoutService.Snap(rect.TopLeft);
            var comment = new SchemeComment
            {
                Text = UiText.T("Text.Comment"),
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
        CanvasViewport.Cursor = Cursors.Arrow;
        if (CanvasViewport.IsMouseCaptured)
        {
            CanvasViewport.ReleaseMouseCapture();
        }
    }

    private void PlannerCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        StopCanvasTranslateAnimation();
        var oldZoom = CanvasScale.ScaleX;
        var newZoom = Math.Clamp(oldZoom * (e.Delta > 0 ? 1.1 : 0.9), 0.35, 2.4);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            return;
        }

        // Zoom about the cursor: keep the world point under the mouse fixed on screen.
        // screen = world*zoom + translate, so translate' = cursor - (cursor - translate) * (newZoom/oldZoom).
        var cursor = e.GetPosition(GridInputLayer);
        var ratio = newZoom / oldZoom;
        CanvasTranslate.X = cursor.X - (cursor.X - CanvasTranslate.X) * ratio;
        CanvasTranslate.Y = cursor.Y - (cursor.Y - CanvasTranslate.Y) * ratio;
        CanvasScale.ScaleX = newZoom;
        CanvasScale.ScaleY = newZoom;
        UpdateZoomText();
    }

    private void UpdateZoomText()
    {
        // The zoom readout now lives in the alerts bar; publish the value via the session.
        _session.Zoom = CanvasScale.ScaleX;
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
            SetStatus(UiText.Format("Status.ResourceNotAvailableForConnection", UiText.T("Text.NotAvailableForConnection")));
            return false;
        }

        var sourceNode = _scheme.Nodes.FirstOrDefault(node => node.Id == source.NodeId);
        var sourceOutput = PlannerEdgeService.OutputForNode(_catalog, sourceNode, source.ItemId);
        var targetRecipe = RecipeForNode(_scheme.Nodes.FirstOrDefault(node => node.Id == target.NodeId));
        if (sourceOutput is null
            || targetRecipe is null
            || !targetRecipe.Inputs.Any(input => input.ItemId == source.ItemId))
        {
            SetStatus(UiText.T("Status.PortsNotCompatible"));
            return false;
        }

        if (_scheme.Edges.Any(edge =>
                edge.SourceNodeId == source.NodeId
                && edge.TargetNodeId == target.NodeId
                && edge.SourceItemId == source.ItemId))
        {
            SetStatus(UiText.T("Status.ConnectionAlreadyExists"));
            return false;
        }

        _scheme.Edges.Add(new SchemeEdge
        {
            SourceNodeId = source.NodeId,
            SourceItemId = source.ItemId,
            TargetNodeId = target.NodeId,
            TargetItemId = target.ItemId,
        });
        SetStatus(UiText.T("Status.ConnectedMachines"));
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
                ? UiText.T("Status.NoCompatibleMachines")
                : UiText.Format("Status.FoundCompatibleMachines", response.Suggestions.Count));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(UiText.Format("Status.CouldNotLoadSuggestions", ex.Message));
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

    public bool HasNoCanvasSelection()
    {
        return _selectedNode is null
            && _selectedEdge is null
            && _selectedComment is null
            && _selectedRoutePoint is null
            && _selectedNodeIds.Count == 0
            && _selectedCommentIds.Count == 0
            && _selectedRoutePoints.Count == 0;
    }

    private static string PriorityDisplay(ProductionPriority priority)
    {
        return priority switch
        {
            ProductionPriority.High => UiText.T("Priority.High"),
            ProductionPriority.Low => UiText.T("Priority.Low"),
            _ => UiText.T("Priority.Mid"),
        };
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
        // Default card text follows the active theme (dark text in light theme). A user who
        // customized the canvas-card colour away from the default keeps their explicit colour.
        var configured = _settings.CanvasCardFont.Color;
        var color = string.IsNullOrWhiteSpace(configured) || string.Equals(configured, "#F4F0E8", StringComparison.OrdinalIgnoreCase)
            ? ThemeColor("NodeCardTextBrush", Color.FromRgb(0xF4, 0xF0, 0xE8))
            : BrushFromString(configured, "#F4F0E8").Color;
        return new SolidColorBrush(color) { Opacity = opacity };
    }

    private static Color EdgeLabelBackgroundColor()
    {
        // Pair the edge-label chip with the same theme surface the label text follows so the
        // text stays legible in both themes (dark text on a light chip in the light theme).
        return ThemeColor("NodeCardBottomBrush", Color.FromArgb(232, 8, 15, 20));
    }

    public void DeleteSelection()
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

}
