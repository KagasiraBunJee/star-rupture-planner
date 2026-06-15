using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StarRupturePlanner.Models;

namespace StarRupturePlanner;

public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<TransportTierInfo> _railTiers;

    public SettingsWindow(AppSettings settings, IReadOnlyList<TransportTierInfo> railTiers)
    {
        InitializeComponent();
        Settings = Clone(settings);
        _railTiers = railTiers;
        PopulateControls();
    }

    public AppSettings Settings { get; private set; }

    private void PopulateControls()
    {
        var families = Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        CanvasFontFamilyPicker.ItemsSource = families;
        LeftFontFamilyPicker.ItemsSource = families;
        CanvasFontFamilyPicker.SelectedItem = Settings.CanvasCardFont.Family;
        LeftFontFamilyPicker.SelectedItem = Settings.LeftBarListFont.Family;
        CanvasFontSizeBox.Text = Settings.CanvasCardFont.Size.ToString(CultureInfo.CurrentCulture);
        LeftFontSizeBox.Text = Settings.LeftBarListFont.Size.ToString(CultureInfo.CurrentCulture);
        CanvasFontColorBox.Text = Settings.CanvasCardFont.Color;
        LeftFontColorBox.Text = Settings.LeftBarListFont.Color;

        ThemePicker.ItemsSource = new[]
        {
            new ThemeOption("System", AppTheme.System),
            new ThemeOption("Dark", AppTheme.Dark),
            new ThemeOption("Light", AppTheme.Light),
        };
        ThemePicker.SelectedValue = Settings.Theme;

        RailTierPicker.ItemsSource = _railTiers;
        RailTierPicker.SelectedValue = Settings.CurrentRailTierId;
        RailTierHelpText.Text = _railTiers.Count == 0
            ? "No rail tiers are configured yet. Add tiers to data/transport_tiers.json."
            : "Connection labels will compare required throughput with this selected in-game rail tier.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFont(CanvasFontFamilyPicker, CanvasFontSizeBox, CanvasFontColorBox, out var canvasFont)
            || !TryReadFont(LeftFontFamilyPicker, LeftFontSizeBox, LeftFontColorBox, out var leftFont))
        {
            return;
        }

        Settings.CanvasCardFont = canvasFont;
        Settings.LeftBarListFont = leftFont;
        Settings.Theme = ThemePicker.SelectedValue is AppTheme theme ? theme : AppTheme.System;
        Settings.CurrentRailTierId = RailTierPicker.SelectedValue as string;
        DialogResult = true;
    }

    private static bool TryReadFont(
        ComboBox familyPicker,
        TextBox sizeBox,
        TextBox colorBox,
        out FontSettings fontSettings)
    {
        fontSettings = new FontSettings();
        var family = familyPicker.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(family))
        {
            MessageBox.Show("Select a font family.", "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if ((!double.TryParse(sizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size)
                && !double.TryParse(sizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            || size < 8
            || size > 32)
        {
            MessageBox.Show("Font size must be between 8 and 32.", "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            _ = (Color)ColorConverter.ConvertFromString(colorBox.Text)!;
        }
        catch
        {
            MessageBox.Show("Color must be a valid WPF color, for example #F4F0E8.", "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        fontSettings = new FontSettings
        {
            Family = family,
            Size = size,
            Color = colorBox.Text.Trim(),
        };
        return true;
    }

    private static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            Theme = settings.Theme,
            CurrentRailTierId = settings.CurrentRailTierId,
            CanvasCardFont = new FontSettings
            {
                Family = settings.CanvasCardFont.Family,
                Size = settings.CanvasCardFont.Size,
                Color = settings.CanvasCardFont.Color,
            },
            LeftBarListFont = new FontSettings
            {
                Family = settings.LeftBarListFont.Family,
                Size = settings.LeftBarListFont.Size,
                Color = settings.LeftBarListFont.Color,
            },
        };
    }

    private sealed record ThemeOption(string Label, AppTheme Value);
}
