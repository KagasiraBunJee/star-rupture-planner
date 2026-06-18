using System.Windows;
using System.Windows.Controls;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.Views;

/// <summary>
/// Top command bar: app title/version chrome, global search box, command buttons and
/// window caption buttons. App commands are raised as intents for the shell (MainWindow)
/// to orchestrate; the caption buttons operate the host window directly.
/// </summary>
public partial class CommandBarView : UserControl
{
    public CommandBarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public event EventHandler? NewRequested;
    public event EventHandler? AddSchemeRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? SettingsRequested;

    /// <summary>Set the channel badge + version subtitle from build info (call after a language change).</summary>
    public void RefreshChrome()
    {
        AppChannelText.Text = AppVersionInfo.ChannelLabel;
        AppVersionText.Text = AppVersionInfo.Subtitle(UiText.T("App.Subtitle"));
    }

    /// <summary>Focus and select the search box (Ctrl+K from the shell).</summary>
    public void FocusSearch()
    {
        TopSearchBox.Focus();
        TopSearchBox.SelectAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.StateChanged += (_, _) => UpdateMaxRestoreGlyph(window.WindowState);
            UpdateMaxRestoreGlyph(window.WindowState);
        }
    }

    private void UpdateMaxRestoreGlyph(WindowState state)
    {
        var maximized = state == WindowState.Maximized;
        MaxRestoreButton.Content = maximized ? "" : "";
        MaxRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void TopSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TopSearchPlaceholder is not null)
        {
            TopSearchPlaceholder.Visibility = string.IsNullOrEmpty(TopSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void NewScheme_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke(this, EventArgs.Empty);

    private void AddScheme_Click(object sender, RoutedEventArgs e) => AddSchemeRequested?.Invoke(this, EventArgs.Empty);

    private void SaveScheme_Click(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
