using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using StarRupturePlanner.Controls;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;
using StarRupturePlanner.ViewModels;
using Registry = Microsoft.Win32.Registry;

namespace StarRupturePlanner;

public partial class MainWindow : Window
{
    private readonly IPlannerApiClient _apiClient;
    private readonly IApiProcessManager _apiProcessManager;
    private readonly ISchemeStore _schemeStore;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IPlannerCalculator _calculator;
    private readonly ICanvasLayoutService _layoutService;
    private readonly ISchemeSession _session;
    private readonly MainWindowViewModel _viewModel;
    private readonly PlannerCanvasViewModel _canvasViewModel;
    private readonly AlertsBarViewModel _alertsBarViewModel;

    private PlannerCatalog _catalog = new();
    private SchemeDocument _scheme = new();
    private AppSettings _settings = new();
    private string _lastToolboxUnlockSignature = "";
    private string _lastStatus = "";
    private bool _disposed;

    public MainWindow(
        IPlannerApiClient apiClient,
        IApiProcessManager apiProcessManager,
        ISchemeStore schemeStore,
        IAppSettingsStore settingsStore,
        IPlannerCalculator calculator,
        ICanvasLayoutService layoutService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ISchemeSession session)
    {
        InitializeComponent();

        _apiClient = apiClient;
        _apiProcessManager = apiProcessManager;
        _schemeStore = schemeStore;
        _settingsStore = settingsStore;
        _calculator = calculator;
        _layoutService = layoutService;
        _session = session;
        var uiDispatcher = new WpfUiDispatcher(Dispatcher);
        _viewModel = new MainWindowViewModel(
            _apiClient,
            _apiProcessManager,
            _schemeStore,
            _settingsStore,
            uiDispatcher,
            backgroundTaskRunner);
        _canvasViewModel = new PlannerCanvasViewModel(_calculator, _layoutService);
        DataContext = _viewModel;
        _settings = _viewModel.Settings;
        _scheme = _viewModel.Scheme;
        _session.CurrentSettings = _settings;
        _session.CurrentScheme = _scheme;
        _session.Status = _viewModel.Status;
        _alertsBarViewModel = new AlertsBarViewModel(_session, _calculator);
        AlertsBar.DataContext = _alertsBarViewModel;
        _session.StatusRequested += (_, message) => SetStatus(message);
        _session.AnalysisChanged += (_, _) => RefreshToolboxUnlocksIfNeeded();
        CommandBar.NewRequested += (_, _) => NewScheme();
        CommandBar.OpenFolderRequested += ChooseFolder_Click;
        CommandBar.SaveRequested += (_, _) => RunUiAsync(SaveCurrentSchemeAsync, "MainWindow.SaveScheme");
        CommandBar.SettingsRequested += Settings_Click;
        ToolboxPanel.SchemeOpenRequested += (_, item) => RunUiAsync(() => OpenSchemeListItemAsync(item), "MainWindow.OpenScheme");
        ToolboxPanel.SchemeDeleteRequested += (_, item) => DeleteSchemeFromToolbox(item);
        ToolboxPanel.ResourceActivated += (_, item) => CanvasPanel.AddRecipeNode(item.Recipe, _layoutService.Snap(new Point(260, 180)));
        ToolboxPanel.MachineActivated += (_, item) => CanvasPanel.AddMachineNode(item.Building, _layoutService.Snap(new Point(260, 180)));
        CanvasPanel.Initialize(_session, _apiClient, _calculator, _layoutService, _canvasViewModel);
        InspectorPanel.Initialize(_session, _apiClient, _calculator, CanvasPanel.HasNoCanvasSelection, BlueprintSourceSchemeExists, OpenBlueprintSource);
        ApplySettings();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => DisposeWindowResources();
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeWindowResources();
        base.OnClosed(e);
    }

    private void DisposeWindowResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.Dispose();
    }

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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) =>
        RunUiAsync(async () =>
        {
            await RefreshSchemeListAsync();
            NewScheme();
            await InitializeApiAsync();
        }, "MainWindow.Loaded");

    private async Task InitializeApiAsync()
    {
        await _viewModel.InitializeAsync();
        SyncFromViewModel();
        CanvasPanel.Render();
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
        _session.CurrentCatalog = _catalog;
        _session.CurrentScheme = _scheme;
        _session.CurrentSettings = _settings;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        var maximized = WindowState == WindowState.Maximized;

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
        CanvasPanel.ResetView();
        UpdateInspector();
    }

    private void ChooseFolder_Click(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiText.T("Text.ChooseSchemeFolder"),
            InitialDirectory = _schemeStore.FolderPath,
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetSchemeFolder(dialog.FolderName);
            RunUiAsync(RefreshSchemeListAsync, "MainWindow.RefreshSchemeList");
            SetStatus(UiText.T("Status.SchemeFolderChanged"));
        }
    }

    private void Settings_Click(object? sender, EventArgs e) =>
        RunUiAsync(async () =>
        {
            var previousLanguage = PlannerLanguages.Normalize(_settings.PlannerLanguage);
            var window = new SettingsWindow(_settings)
            {
                Owner = this,
            };
            if (window.ShowDialog() != true)
            {
                return;
            }

            _settings = window.Settings;
            _viewModel.SaveSettings(_settings);
            var languageChanged = !string.Equals(
                previousLanguage,
                PlannerLanguages.Normalize(_settings.PlannerLanguage),
                StringComparison.Ordinal);
            if (languageChanged)
            {
                ApplySettings();
                _viewModel.RefreshLocalizedText();
                await _viewModel.ReloadCatalogAsync();
            }

            SyncFromViewModel();
            ApplySettings();
            CanvasPanel.Render();
            UpdateInspector();
        }, "MainWindow.Settings");

    private void ApplySettings()
    {
        // Per-user font overrides shadow the theme defaults for the window scope.
        Resources["LeftListFontFamily"] = new FontFamily(UiBrushHelpers.SafeFontFamily(_settings.LeftBarListFont.Family));
        Resources["LeftListFontSize"] = _settings.LeftBarListFont.Size;
        Resources["LeftListForegroundBrush"] = UiBrushHelpers.BrushFromString(_settings.LeftBarListFont.Color, "#F4F0E8");

        ApplyTheme(ResolveTheme(_settings.Theme));
        ApplyLanguage(_settings.PlannerLanguage);
        UpdateVersionChrome();
    }

    private void UpdateVersionChrome()
    {
        Title = AppVersionInfo.WindowTitle(UiText.T("App.Title"));
        CommandBar.RefreshChrome();
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

    private static void ApplyLanguage(string? language)
    {
        var code = PlannerLanguages.Normalize(language);
        var languageUri = new Uri($"Localization/Strings.{code}.xaml", UriKind.Relative);
        var merged = Application.Current.Resources.MergedDictionaries;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("Localization/Strings.", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }

        var insertIndex = Math.Min(1, merged.Count);
        merged.Insert(insertIndex, new ResourceDictionary { Source = languageUri });
    }

    private async Task OpenSchemeListItemAsync(SchemeListItem item)
    {
        await _viewModel.OpenSchemeAsync(item);
        SyncFromViewModel();
        CanvasPanel.LoadView();
        UpdateInspector();
    }

    private void OpenBlueprintSource(SchemeNode node) =>
        RunUiAsync(async () =>
        {
            if (node.NodeType != SchemeNodeType.BlueprintSource)
            {
                return;
            }

            var item = FindBlueprintSourceScheme(node);
            if (item is null)
            {
                SetStatus(UiText.T("Status.SourceSchemeUnavailable"));
                UpdateInspector();
                return;
            }

            await OpenSchemeListItemAsync(item);
        }, "MainWindow.OpenBlueprintSource");

    private bool BlueprintSourceSchemeExists(SchemeNode node)
    {
        return FindBlueprintSourceScheme(node) is not null;
    }

    private SchemeListItem? FindBlueprintSourceScheme(SchemeNode node)
    {
        var schemes = _viewModel.Toolbox.Schemes.ToList();
        if (!string.IsNullOrWhiteSpace(node.SourceSchemePath))
        {
            var byPath = schemes.FirstOrDefault(item => SamePath(item.FilePath, node.SourceSchemePath));
            if (byPath is not null)
            {
                return byPath;
            }
        }

        return schemes.FirstOrDefault(item =>
            string.Equals(item.Name, node.SourceSchemeName, StringComparison.CurrentCultureIgnoreCase));
    }

    private void DeleteSchemeFromToolbox(SchemeListItem item) =>
        RunUiAsync(async () =>
        {
            var result = MessageBox.Show(
                this,
                UiText.Format("Text.DeleteSchemeConfirm", item.Name),
                UiText.T("Text.DeleteScheme"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var deletedCurrentScheme = SamePath(_scheme.FilePath, item.FilePath);
            var deleted = await _viewModel.DeleteSchemeAsync(item);
            if (!deleted || !deletedCurrentScheme)
            {
                if (deleted)
                {
                    RemoveBlueprintReferencesFromCurrentScheme(item);
                }

                return;
            }

            _viewModel.NewScheme();
            SyncFromViewModel();
            CanvasPanel.ResetView();
            UpdateInspector();
            SetStatus(UiText.Format("Status.Deleted", item.Name));
        }, "MainWindow.DeleteScheme");

    private void RemoveBlueprintReferencesFromCurrentScheme(SchemeListItem deletedScheme)
    {
        var removedNodeIds = _scheme.Nodes
            .Where(node => node.NodeType == SchemeNodeType.BlueprintSource
                && BlueprintReferencesScheme(node, deletedScheme))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (removedNodeIds.Count == 0)
        {
            return;
        }

        _scheme.Nodes.RemoveAll(node => removedNodeIds.Contains(node.Id));
        _scheme.Edges.RemoveAll(edge =>
            removedNodeIds.Contains(edge.SourceNodeId) || removedNodeIds.Contains(edge.TargetNodeId));
        CanvasPanel.ClearSelection();
        CanvasPanel.Render();
        UpdateInspector();
        SetStatus(UiText.Format("Status.RemovedBlueprintReferences", removedNodeIds.Count, deletedScheme.Name));
    }

    private static bool BlueprintReferencesScheme(SchemeNode node, SchemeListItem scheme)
    {
        return (!string.IsNullOrWhiteSpace(node.SourceSchemePath) && SamePath(node.SourceSchemePath, scheme.FilePath))
            || (string.IsNullOrWhiteSpace(node.SourceSchemePath)
                && string.Equals(node.SourceSchemeName, scheme.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    private async Task SaveCurrentSchemeAsync()
    {
        if (_scheme.FilePath is null && _scheme.Name == "Untitled")
        {
            var name = PromptForName(UiText.T("Text.SchemeName"), "Untitled");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _scheme.Name = name.Trim();
        }

        CanvasPanel.CaptureViewState();
        CanvasPanel.Render();
        await _viewModel.SaveSchemeAsync();
        await RefreshSchemeListAsync();
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
        RunUiAsync(() => _viewModel.Toolbox.SetSchemeAsync(_scheme), "MainWindow.RefreshToolboxUnlocks");
    }

    private void UpdateInspector() => _session.NotifySelectionChanged();

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
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow.ResolveTheme] Failed to read Windows theme: {ex.Message}");
            return AppTheme.Dark;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            RunUiAsync(SaveCurrentSchemeAsync, "MainWindow.SaveShortcut");
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            CommandBar.FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            CanvasPanel.DeleteSelection();
            e.Handled = true;
        }
    }

    private void SetStatus(string status)
    {
        _lastStatus = status;
        _viewModel.SetStatus(status);
        _session.Status = _viewModel.Status;
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
        var ok = new Button { Content = UiText.T("Command.Save"), IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = UiText.T("Command.Cancel"), IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(new TextBlock { Text = UiText.T("Text.Name"), Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(textBox);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? textBox.Text : null;
    }

    private static bool SamePath(string? left, string? right) => PathUtil.SamePath(left, right);

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
        => WpfVisualTreeHelpers.FindAncestor<T>(current);

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject ancestor)
        => WpfVisualTreeHelpers.IsDescendantOf(current, ancestor);

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject current)
        where T : DependencyObject
        => WpfVisualTreeHelpers.FindVisualChildren<T>(current);

}
