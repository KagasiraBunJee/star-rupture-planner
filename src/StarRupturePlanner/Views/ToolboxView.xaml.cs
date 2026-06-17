using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using StarRupturePlanner.Models;
using StarRupturePlanner.Services;

namespace StarRupturePlanner.Views;

/// <summary>
/// Left toolbox: schemes / resources / machines lists with filtering. Owns the drag-source
/// gesture detection (a drag puts the item in a <see cref="DataObject"/> the canvas drop reads).
/// Shell-coupled actions (open/delete scheme, double-click add node) are raised as intents.
/// Inherits the window's <c>MainWindowViewModel</c> DataContext for its bindings.
/// </summary>
public partial class ToolboxView : UserControl
{
    private Point? _schemeDragStart;
    private SchemeListItem? _schemeDragItem;
    private Point? _resourceDragStart;
    private ResourceToolboxItem? _resourceDragItem;
    private Point? _machineDragStart;
    private MachineToolboxItem? _machineDragItem;

    public ToolboxView()
    {
        InitializeComponent();
    }

    public event EventHandler<SchemeListItem>? SchemeOpenRequested;
    public event EventHandler<SchemeListItem>? SchemeDeleteRequested;
    public event EventHandler<ResourceToolboxItem>? ResourceActivated;
    public event EventHandler<MachineToolboxItem>? MachineActivated;

    private void SchemesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var button = WpfVisualTreeHelpers.FindAncestor<Button>(e.OriginalSource as DependencyObject);
        if (button?.Tag as string != "DeleteScheme" || button.DataContext is not SchemeListItem item)
        {
            BeginToolboxDrag(SchemesList, e, out _schemeDragStart, out _schemeDragItem);
            return;
        }

        e.Handled = true;
        SchemeDeleteRequested?.Invoke(this, item);
    }

    private void SchemesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (ShouldStartToolboxDrag(SchemesList, e, _schemeDragStart, _schemeDragItem))
        {
            var item = _schemeDragItem;
            ClearSchemeToolboxDrag();
            DragDrop.DoDragDrop(SchemesList, new DataObject(typeof(SchemeListItem), item!), DragDropEffects.Copy);
        }
    }

    private void SchemesList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = _schemeDragItem;
        ClearSchemeToolboxDrag();
        if (item is not null && Equals(SchemesList.SelectedItem, item))
        {
            SchemeOpenRequested?.Invoke(this, item);
        }
    }

    private void SchemesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_schemeDragItem is not null)
        {
            return;
        }

        if (SchemesList.SelectedItem is SchemeListItem item)
        {
            SchemeOpenRequested?.Invoke(this, item);
        }
    }

    private void ResourcesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetListItem(ResourcesList, e.OriginalSource as DependencyObject, out ResourceToolboxItem? item))
        {
            ResourceActivated?.Invoke(this, item!);
        }
    }

    private void ResourcesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => BeginToolboxDrag(ResourcesList, e, out _resourceDragStart, out _resourceDragItem);

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
        => ClearResourceToolboxDrag();

    private void MachinesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetListItem(MachinesList, e.OriginalSource as DependencyObject, out MachineToolboxItem? item))
        {
            MachineActivated?.Invoke(this, item!);
        }
    }

    private void MachinesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => BeginToolboxDrag(MachinesList, e, out _machineDragStart, out _machineDragItem);

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
        => ClearMachineToolboxDrag();

    private static void BeginToolboxDrag<T>(ListBox listBox, MouseButtonEventArgs e, out Point? dragStart, out T? dragItem)
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

    private void ClearSchemeToolboxDrag()
    {
        _schemeDragStart = null;
        _schemeDragItem = null;
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

        if (source is null || WpfVisualTreeHelpers.FindAncestor<ScrollBar>(source) is not null)
        {
            return false;
        }

        var container = WpfVisualTreeHelpers.FindAncestor<ListBoxItem>(source);
        if (container is null || !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(container), listBox))
        {
            return false;
        }

        item = container.DataContext as T;
        return item is not null;
    }
}
