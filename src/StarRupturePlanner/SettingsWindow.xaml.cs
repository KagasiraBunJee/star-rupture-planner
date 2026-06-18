using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = Clone(settings);
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
            new ThemeOption(UiText.T("Text.ThemeSystem"), AppTheme.System),
            new ThemeOption(UiText.T("Text.ThemeDark"), AppTheme.Dark),
            new ThemeOption(UiText.T("Text.ThemeLight"), AppTheme.Light),
        };
        ThemePicker.SelectedValue = Settings.Theme;

        LanguagePicker.ItemsSource = new[]
        {
            new LanguageOption(UiText.T("Text.LanguageEnglish"), PlannerLanguages.English),
            new LanguageOption(UiText.T("Text.LanguageRussian"), PlannerLanguages.Russian),
            new LanguageOption(UiText.T("Text.LanguageGerman"), PlannerLanguages.German),
            new LanguageOption(UiText.T("Text.LanguageUkrainian"), PlannerLanguages.Ukrainian),
        };
        LanguagePicker.SelectedValue = PlannerLanguages.Normalize(Settings.PlannerLanguage);

        ApiPortBox.Text = AppSettings.NormalizeApiPort(Settings.ApiPort).ToString(CultureInfo.InvariantCulture);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFont(CanvasFontFamilyPicker, CanvasFontSizeBox, CanvasFontColorBox, out var canvasFont)
            || !TryReadFont(LeftFontFamilyPicker, LeftFontSizeBox, LeftFontColorBox, out var leftFont)
            || !TryReadPort(out var apiPort))
        {
            return;
        }

        Settings.CanvasCardFont = canvasFont;
        Settings.LeftBarListFont = leftFont;
        Settings.Theme = ThemePicker.SelectedValue is AppTheme theme ? theme : AppTheme.System;
        Settings.PlannerLanguage = LanguagePicker.SelectedValue as string ?? PlannerLanguages.English;
        Settings.ApiPort = apiPort;
        DialogResult = true;
    }

    private bool TryReadPort(out int apiPort)
    {
        if (!int.TryParse(ApiPortBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out apiPort)
            || apiPort is < 1 or > 65535)
        {
            MessageBox.Show(UiText.T("Text.ApiPortRange"), UiText.T("Text.InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
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
            MessageBox.Show(UiText.T("Text.SelectFontFamily"), UiText.T("Text.InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if ((!double.TryParse(sizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size)
                && !double.TryParse(sizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
            || size < 8
            || size > 32)
        {
            MessageBox.Show(UiText.T("Text.FontSizeRange"), UiText.T("Text.InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try
        {
            _ = (Color)ColorConverter.ConvertFromString(colorBox.Text)!;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsWindow] Invalid WPF color '{colorBox.Text}': {ex.Message}");
            MessageBox.Show(UiText.T("Text.InvalidWpfColor"), UiText.T("Text.InvalidSettings"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            PlannerLanguage = PlannerLanguages.Normalize(settings.PlannerLanguage),
            ApiPort = AppSettings.NormalizeApiPort(settings.ApiPort),
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

    private sealed record ThemeOption(string Label, AppTheme Value)
    {
        public override string ToString() => Label;
    }

    private sealed record LanguageOption(string Label, string Value)
    {
        public override string ToString() => Label;
    }
}
