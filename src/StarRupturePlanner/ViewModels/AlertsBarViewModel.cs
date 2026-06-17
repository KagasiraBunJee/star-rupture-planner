using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.ViewModels;

/// <summary>
/// Backs the bottom alerts/metrics bar. Reads the shared <see cref="ISchemeSession"/>
/// and rebuilds its bindable state whenever production analysis, status or zoom changes.
/// Replaces MainWindow's <c>UpdateProductionAlerts</c> / metric / zoom-text code-behind.
/// </summary>
public sealed class AlertsBarViewModel : ViewModelBase
{
    private readonly ISchemeSession _session;
    private readonly IPlannerCalculator _calculator;

    private string _status = "";
    private string _machines = "0";
    private string _power = "-";
    private string _temperature = "-";
    private string _schemeOutputs = "";
    private string _schemeOutputsTooltip = "";
    private string _zoomText = "100%";

    public AlertsBarViewModel(ISchemeSession session, IPlannerCalculator calculator)
    {
        _session = session;
        _calculator = calculator;
        ResetZoomCommand = new RelayCommand(() => _session.RequestResetZoom());

        _session.AnalysisChanged += (_, _) => RefreshMetricsAndAlerts();
        _session.StatusChanged += (_, _) => Status = _session.Status;
        _session.ZoomChanged += (_, _) => ZoomText = FormatZoom(_session.Zoom);

        Status = _session.Status;
        ZoomText = FormatZoom(_session.Zoom);
        RefreshMetricsAndAlerts();
    }

    public ObservableCollection<AlertChipItem> Alerts { get; } = [];

    public ICommand ResetZoomCommand { get; }

    /// <summary>Raised after <see cref="Alerts"/> is rebuilt so the view can reset its scroll offset.</summary>
    public event Action? AlertsRebuilt;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Machines
    {
        get => _machines;
        private set => SetProperty(ref _machines, value);
    }

    public string Power
    {
        get => _power;
        private set => SetProperty(ref _power, value);
    }

    public string Temperature
    {
        get => _temperature;
        private set => SetProperty(ref _temperature, value);
    }

    public string SchemeOutputs
    {
        get => _schemeOutputs;
        private set => SetProperty(ref _schemeOutputs, value);
    }

    public string SchemeOutputsTooltip
    {
        get => _schemeOutputsTooltip;
        private set => SetProperty(ref _schemeOutputsTooltip, value);
    }

    public string ZoomText
    {
        get => _zoomText;
        private set => SetProperty(ref _zoomText, value);
    }

    private static string FormatZoom(double zoom) => $"{zoom * 100:0}%";

    private void RefreshMetricsAndAlerts()
    {
        var scheme = _session.CurrentScheme;
        var catalog = _session.CurrentCatalog;
        var analysis = _session.ProductionAnalysis;

        var total = scheme.Nodes.Count(node => PlannerEdgeService.RecipeForNode(catalog, node) is not null);
        var starved = scheme.Nodes.Count(node =>
            PlannerEdgeService.RecipeForNode(catalog, node) is not null
            && PlannerNodeMetrics.FeedRatio(analysis, node).IsShort);
        Machines = total == 0 ? "0" : $"{total - starved}/{total}";

        var totals = PlannerMetricService.CalculateTotals(scheme, catalog);
        Power = totals.PowerGeneration > 0
            ? $"{totals.PowerConsumption:g} kW / +{totals.PowerGeneration:g} kW"
            : $"{totals.PowerConsumption:g} kW";
        Temperature = totals.Temperature > 0
            ? $"+{totals.Temperature:g} {UiText.T("Text.Temp")}"
            : $"{totals.Temperature:g} {UiText.T("Text.Temp")}";

        var outputs = PlannerMetricService.SchemeOutputs(scheme, catalog, _calculator);
        SchemeOutputs = outputs.Count == 0
            ? UiText.T("Text.NoSchemeOutputsMarked")
            : string.Join(", ", outputs.Select(output => $"{output.ItemName} {output.RatePerMinute:g}/min"));
        SchemeOutputsTooltip = outputs.Count == 0
            ? UiText.T("Text.NoSchemeOutputsMarked")
            : string.Join("\n", outputs.Select(output => $"{output.MachineName}: {output.ItemName} {output.RatePerMinute:g}/min"));

        Alerts.Clear();
        var lockedAlerts = PlannerUnlockService.LockedNodeAlerts(catalog, scheme);
        if (analysis.Alerts.Count == 0 && lockedAlerts.Count == 0)
        {
            Alerts.Add(new AlertChipItem(UiText.T("Text.NoProductionShortages"), "✓", UiPalette.SignalGreen));
            AlertsRebuilt?.Invoke();
            return;
        }

        // Show every alert; the row scrolls horizontally to reveal the rest.
        foreach (var alert in analysis.Alerts)
        {
            Alerts.Add(new AlertChipItem(alert.Message, "⚠", UiPalette.Shortage));
        }

        foreach (var alert in lockedAlerts)
        {
            Alerts.Add(new AlertChipItem(alert, "вљ ", UiPalette.Shortage));
        }

        AlertsRebuilt?.Invoke();
    }
}

/// <summary>One chip in the alerts row, with brushes precomputed from its accent color.</summary>
public sealed class AlertChipItem
{
    public AlertChipItem(string message, string glyph, Color accent)
    {
        Message = message;
        Glyph = glyph;
        BackgroundBrush = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B));
        BorderBrush = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
        GlyphBrush = new SolidColorBrush(accent);
    }

    public string Message { get; }
    public string Glyph { get; }
    public Brush BackgroundBrush { get; }
    public Brush BorderBrush { get; }
    public Brush GlyphBrush { get; }
}
