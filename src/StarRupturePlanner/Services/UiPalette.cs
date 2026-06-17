using System.Windows.Media;

namespace StarRupturePlanner.Services;

/// <summary>
/// Shared accent colors used by the canvas, node cards and alerts bar.
/// Extracted from MainWindow so each UI module references one palette.
/// </summary>
public static class UiPalette
{
    public static readonly Color InputPort = Color.FromRgb(10, 132, 255);
    public static readonly Color OutputPort = Color.FromRgb(24, 160, 255);
    public static readonly Color SignalGreen = Color.FromRgb(99, 214, 77);
    public static readonly Color Shortage = Color.FromRgb(255, 72, 72);
    public static readonly Color ReactorOrange = Color.FromRgb(0xFF, 0x8A, 0x00);
    public static readonly Color LockedPort = Color.FromRgb(255, 72, 72);
    public static readonly Color PanelGlass = Color.FromRgb(16, 24, 32);
    public static readonly Color GraphiteLine = Color.FromRgb(38, 52, 61);
}
