using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StarRupturePlanner.ViewModels;

namespace StarRupturePlanner.Views;

public partial class AlertsBarView : UserControl
{
    public AlertsBarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AlertsBarViewModel oldVm)
        {
            oldVm.AlertsRebuilt -= OnAlertsRebuilt;
        }

        if (e.NewValue is AlertsBarViewModel newVm)
        {
            newVm.AlertsRebuilt += OnAlertsRebuilt;
        }
    }

    // Reset the horizontal scroll to the start whenever the alert chips are rebuilt.
    private void OnAlertsRebuilt() => AlertsScroller?.ScrollToHorizontalOffset(0);

    private void AlertsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller)
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
