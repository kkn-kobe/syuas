using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Syuas.Core.ViewModels;

namespace Syuas.App.Services;

// Only the headers participate in this drag. File drops continue through the window's existing handlers.
internal sealed class TabDragReorder : IDisposable
{
    private const string TabFormat = "SYUAS.DocumentTab";
    private readonly TabControl tabs;
    private readonly MainViewModel workspace;
    private readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(75) };
    private DocumentTabViewModel? candidate;
    private Point start, lastPoint;
    private IDataObject? hoveringData;
    private bool dragging, disposed;

    private sealed record Payload(TabDragReorder Owner, DocumentTabViewModel Document);

    public TabDragReorder(TabControl tabs, MainViewModel workspace)
    {
        this.tabs = tabs;
        this.workspace = workspace;
        tabs.AllowDrop = true;
        tabs.PreviewMouseLeftButtonDown += OnMouseDown;
        tabs.PreviewMouseLeftButtonUp += OnMouseUp;
        tabs.PreviewMouseMove += OnMouseMove;
        tabs.MouseLeave += OnMouseLeave;
        tabs.PreviewDragOver += OnDragOver;
        tabs.PreviewDragLeave += OnDragLeave;
        tabs.PreviewDrop += OnDrop;
        scrollTimer.Tick += OnScroll;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        candidate = null;
        if (workspace.IsBusy || !CanStart(e.OriginalSource as DependencyObject)) return;
        var item = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
        candidate = item?.DataContext as DocumentTabViewModel;
        start = e.GetPosition(tabs);
    }

    internal bool CanStart(DependencyObject? source)
    {
        // A click or drag on the close button / scrollbar must retain its normal behavior.
        if (FindAncestor<ButtonBase>(source) is not null) return false;
        var item = FindAncestor<TabItem>(source);
        return item is not null && ItemsControl.ItemsControlFromItemContainer(item) == tabs;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => candidate = null;
    private void OnMouseLeave(object sender, MouseEventArgs e) { if (!dragging) candidate = null; }
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || workspace.IsBusy) { candidate = null; return; }
        if (candidate is not { } document || dragging) return;
        var point = e.GetPosition(tabs);
        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        candidate = null;
        dragging = true;
        e.Handled = true;
        try { DragDrop.DoDragDrop(tabs, CreateData(document), DragDropEffects.Move); }
        finally { dragging = false; ClearDrop(); }
    }

    internal IDataObject CreateData(DocumentTabViewModel document) =>
        new DataObject(TabFormat, new Payload(this, document));

    private DocumentTabViewModel? GetDocument(IDataObject data) =>
        !disposed && !workspace.IsBusy && data.GetDataPresent(TabFormat, autoConvert: false) &&
        data.GetData(TabFormat, autoConvert: false) is Payload payload &&
        ReferenceEquals(payload.Owner, this) && workspace.Documents.Contains(payload.Document) ? payload.Document : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabFormat, autoConvert: false)) return;
        e.Effects = (e.AllowedEffects & DragDropEffects.Move) != 0 && UpdateDrop(e.Data, e.GetPosition(tabs))
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabFormat, autoConvert: false)) return;
        if (FindPlacement(e.GetPosition(tabs)) is null) ClearDrop();
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabFormat, autoConvert: false)) return;
        e.Effects = (e.AllowedEffects & DragDropEffects.Move) != 0 && CompleteDrop(e.Data, e.GetPosition(tabs))
            ? DragDropEffects.Move : DragDropEffects.None;
        ClearDrop();
        e.Handled = true;
    }

    internal bool UpdateDrop(IDataObject data, Point point)
    {
        if (GetDocument(data) is null || FindPlacement(point) is not { } placement)
        {
            ClearDrop();
            return false;
        }
        hoveringData = data;
        lastPoint = point;
        if (tabs.Template.FindName("DropIndicator", tabs) is Border marker)
        {
            Canvas.SetLeft(marker, Math.Clamp(placement.X - 1, 0, Math.Max(0, tabs.ActualWidth - 3)));
            Canvas.SetTop(marker, placement.Top);
            marker.Height = placement.Height;
            marker.Visibility = Visibility.Visible;
        }
        scrollTimer.Start();
        return true;
    }

    internal bool CompleteDrop(IDataObject data, Point point)
    {
        var document = GetDocument(data);
        var placement = FindPlacement(point);
        ClearDrop();
        return document is not null && placement is not null && workspace.MoveDocument(document, placement.Value.Index);
    }

    private readonly record struct Placement(int Index, double X, double Top, double Height);

    private ScrollViewer? HeaderScroll => tabs.Template.FindName("HeaderScroll", tabs) as ScrollViewer;

    private Rect? Viewport()
    {
        var presenter = HeaderScroll is { } scroll ? FindDescendant<ScrollContentPresenter>(scroll) : null;
        return presenter is null ? null : presenter.TransformToAncestor(tabs)
            .TransformBounds(new Rect(presenter.RenderSize));
    }

    private Placement? FindPlacement(Point point)
    {
        if (Viewport() is not { } viewport || !viewport.Contains(point)) return null;
        Rect? last = null;
        for (var i = 0; i < tabs.Items.Count; i++)
        {
            if (tabs.ItemContainerGenerator.ContainerFromIndex(i) is not TabItem item) return null;
            var rect = item.TransformToAncestor(tabs).TransformBounds(new Rect(item.RenderSize));
            last = rect;
            if (point.X < rect.Left + rect.Width / 2)
                return new(i, Math.Clamp(rect.Left, viewport.Left, viewport.Right), rect.Top, rect.Height);
        }
        return last is { } end ? new(tabs.Items.Count, Math.Clamp(end.Right, viewport.Left, viewport.Right), end.Top, end.Height) : null;
    }

    private void OnScroll(object? sender, EventArgs e)
    {
        if (hoveringData is not { } data || GetDocument(data) is null || Viewport() is not { } viewport || HeaderScroll is not { } scroll)
        {
            ClearDrop();
            return;
        }
        // Keep scrolling even when the pointer remains stationary at an edge.
        var delta = lastPoint.X < viewport.Left + 24 ? -24 : lastPoint.X > viewport.Right - 24 ? 24 : 0;
        if (delta == 0 || scroll.ScrollableWidth <= 0) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + delta);
        tabs.UpdateLayout();
        UpdateDrop(data, lastPoint);
    }

    internal void ClearDrop()
    {
        hoveringData = null;
        scrollTimer.Stop();
        if (tabs.Template.FindName("DropIndicator", tabs) is Border marker) marker.Visibility = Visibility.Collapsed;
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T result) return result;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject element) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        candidate = null;
        ClearDrop();
        scrollTimer.Tick -= OnScroll;
        tabs.PreviewMouseLeftButtonDown -= OnMouseDown;
        tabs.PreviewMouseLeftButtonUp -= OnMouseUp;
        tabs.PreviewMouseMove -= OnMouseMove;
        tabs.MouseLeave -= OnMouseLeave;
        tabs.PreviewDragOver -= OnDragOver;
        tabs.PreviewDragLeave -= OnDragLeave;
        tabs.PreviewDrop -= OnDrop;
    }
}
