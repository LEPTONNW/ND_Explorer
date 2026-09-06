using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ND_Explorer.Models;

namespace ND_Explorer.Controls;

public sealed class StorageTreemapControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(StorageTreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ItemsSource_OnChanged));

    public static readonly DependencyProperty SelectedEntryProperty = DependencyProperty.Register(
        nameof(SelectedEntry),
        typeof(StorageEntry),
        typeof(StorageTreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush[] FolderBrushes = CreateBrushes("#315D9E", "#3B6CA9", "#45658C", "#284E82", "#3D587B");
    private static readonly Brush[] FileBrushes = CreateBrushes("#6A4D91", "#765D9F", "#3D7A78", "#8A5A46", "#59657E", "#486F99");
    private static readonly Pen NormalBorderPen = CreatePen("#8011161E", 1);
    private static readonly Pen HoverBorderPen = CreatePen("#E6FFFFFF", 2);
    private static readonly Pen SelectedBorderPen = CreatePen("#FFFFFFFF", 3);
    private readonly List<TreemapItem> _layout = [];
    private INotifyCollectionChanged? _observedCollection;
    private StorageEntry? _hoveredEntry;

    public StorageTreemapControl()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
    }

    public event EventHandler<StorageEntry>? EntryInvoked;

    public event EventHandler<StorageEntry>? OpenInNdExplorerRequested;

    public event EventHandler<StorageEntry>? OpenInWindowsExplorerRequested;

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public StorageEntry? SelectedEntry
    {
        get => (StorageEntry?)GetValue(SelectedEntryProperty);
        set => SetValue(SelectedEntryProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 12, 16)), null, new Rect(RenderSize));
        _layout.Clear();

        var allEntries = ItemsSource?.Cast<object>()
            .OfType<StorageEntry>()
            .OrderByDescending(entry => entry.Size)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray() ?? [];
        var entries = CreateDisplayEntries(allEntries);

        if (entries.Length == 0 || ActualWidth < 20 || ActualHeight < 20)
        {
            DrawEmptyMessage(drawingContext);
            return;
        }

        var bounds = new Rect(4, 4, Math.Max(0, ActualWidth - 8), Math.Max(0, ActualHeight - 8));
        LayoutRange(entries, 0, entries.Length, bounds, 0);

        foreach (var item in _layout)
        {
            DrawItem(drawingContext, item);
        }
    }

    private void LayoutRange(StorageEntry[] entries, int start, int count, Rect bounds, int depth)
    {
        if (count <= 0 || bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        if (count == 1)
        {
            AddEntryLayout(entries[start], Inset(bounds, 1), depth);
            return;
        }

        double total = 0;
        for (var index = start; index < start + count; index++)
        {
            total += Math.Max(1, entries[index].Size);
        }

        var half = total / 2;
        double firstTotal = 0;
        var firstCount = 0;
        while (firstCount < count - 1)
        {
            var next = Math.Max(1, entries[start + firstCount].Size);
            if (firstCount > 0 && Math.Abs(half - firstTotal) <= Math.Abs(half - (firstTotal + next)))
            {
                break;
            }

            firstTotal += next;
            firstCount++;
        }

        firstCount = Math.Clamp(firstCount, 1, count - 1);
        var ratio = total <= 0 ? 0.5 : Math.Clamp(firstTotal / total, 0.02, 0.98);

        if (bounds.Width >= bounds.Height)
        {
            var split = bounds.Width * ratio;
            LayoutRange(entries, start, firstCount, new Rect(bounds.X, bounds.Y, split, bounds.Height), depth);
            LayoutRange(entries, start + firstCount, count - firstCount, new Rect(bounds.X + split, bounds.Y, bounds.Width - split, bounds.Height), depth);
        }
        else
        {
            var split = bounds.Height * ratio;
            LayoutRange(entries, start, firstCount, new Rect(bounds.X, bounds.Y, bounds.Width, split), depth);
            LayoutRange(entries, start + firstCount, count - firstCount, new Rect(bounds.X, bounds.Y + split, bounds.Width, bounds.Height - split), depth);
        }
    }

    private void AddEntryLayout(StorageEntry entry, Rect bounds, int depth)
    {
        _layout.Add(new TreemapItem(entry, bounds, depth));
        if (!entry.IsExpanded
            || entry.Children is not { Count: > 0 }
            || bounds.Width < 20
            || bounds.Height < 26)
        {
            return;
        }

        var headerHeight = bounds.Height >= 58 ? 25 : 14;
        var childBounds = new Rect(
            bounds.X + 3,
            bounds.Y + headerHeight,
            Math.Max(0, bounds.Width - 6),
            Math.Max(0, bounds.Height - headerHeight - 3));
        var children = CreateDisplayEntries(entry.Children
            .OrderByDescending(child => child.Size)
            .ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
        LayoutRange(children, 0, children.Length, childBounds, depth + 1);
    }

    private void DrawItem(DrawingContext drawingContext, TreemapItem item)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(item.Entry.IsDirectory ? item.Entry.Name : item.Entry.Extension);
        var palette = item.Entry.IsDirectory ? FolderBrushes : FileBrushes;
        var fill = palette[(hash & int.MaxValue) % palette.Length];
        var isSelected = Equals(item.Entry, SelectedEntry);
        var isHovered = Equals(item.Entry, _hoveredEntry);
        var pen = isSelected ? SelectedBorderPen : isHovered ? HoverBorderPen : NormalBorderPen;

        drawingContext.DrawRoundedRectangle(fill, pen, item.Bounds, item.Depth == 0 ? 4 : 3, item.Depth == 0 ? 4 : 3);

        if (item.Entry.IsPartial && item.Bounds.Width >= 22 && item.Bounds.Height >= 22)
        {
            var warning = new FormattedText(
                "!",
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"),
                11,
                Brushes.Gold,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(warning, new Point(item.Bounds.Right - 14, item.Bounds.Top + 3));
        }

        if (item.Bounds.Width < 54 || item.Bounds.Height < 28)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var displayName = item.Entry.IsDirectory
            ? $"{(item.Entry.IsExpanded ? "▾" : "▸")} {item.Entry.Name}"
            : item.Entry.Name;
        var name = new FormattedText(
            displayName,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            item.Bounds.Height >= 52 ? 12 : 10.5,
            Brushes.White,
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, item.Bounds.Width - 12),
            MaxTextHeight = item.Entry.IsExpanded ? 18 : Math.Max(1, item.Bounds.Height - 8),
            Trimming = TextTrimming.CharacterEllipsis
        };
        drawingContext.DrawText(name, new Point(item.Bounds.X + 6, item.Bounds.Y + 5));

        if (item.Entry.IsLoading && item.Bounds.Width >= 90)
        {
            var loading = new FormattedText(
                "분석 중…",
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                Brushes.LightCyan,
                pixelsPerDip);
            drawingContext.DrawText(loading, new Point(item.Bounds.Right - loading.Width - 6, item.Bounds.Y + 6));
        }
        else if (item.Entry.IsExpanded && item.Bounds.Width >= 130)
        {
            var headerSize = new FormattedText(
                item.Entry.SizeText,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9.5,
                new SolidColorBrush(Color.FromArgb(210, 230, 235, 244)),
                pixelsPerDip);
            drawingContext.DrawText(headerSize, new Point(item.Bounds.Right - headerSize.Width - 6, item.Bounds.Y + 6));
        }
        else if (item.Bounds.Width >= 78 && item.Bounds.Height >= 48)
        {
            var size = new FormattedText(
                item.Entry.SizeText,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                new SolidColorBrush(Color.FromArgb(220, 230, 235, 244)),
                pixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, item.Bounds.Width - 12),
                Trimming = TextTrimming.CharacterEllipsis
            };
            drawingContext.DrawText(size, new Point(item.Bounds.X + 6, Math.Min(item.Bounds.Bottom - 18, item.Bounds.Y + 24)));
        }
    }

    private void DrawEmptyMessage(DrawingContext drawingContext)
    {
        var message = new FormattedText(
            "표시할 항목이 없습니다.",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            new SolidColorBrush(Color.FromRgb(125, 135, 152)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(
            message,
            new Point(Math.Max(12, (ActualWidth - message.Width) / 2), Math.Max(12, (ActualHeight - message.Height) / 2)));
    }

    private static StorageEntry[] CreateDisplayEntries(StorageEntry[] entries)
    {
        const int maximumRectangles = 1200;
        if (entries.Length <= maximumRectangles)
        {
            return entries;
        }

        var visible = entries.Take(maximumRectangles - 1).ToList();
        long remainingSize = 0;
        foreach (var entry in entries.Skip(maximumRectangles - 1))
        {
            remainingSize = remainingSize >= long.MaxValue - entry.Size
                ? long.MaxValue
                : remainingSize + entry.Size;
        }

        visible.Add(new StorageEntry(
            $"기타 {entries.Length - maximumRectangles + 1:N0}개 항목",
            string.Empty,
            remainingSize,
            false,
            false,
            string.Empty));
        return visible.ToArray();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var entry = HitTestEntry(e.GetPosition(this));
        if (Equals(entry, _hoveredEntry))
        {
            return;
        }

        _hoveredEntry = entry;
        ToolTip = entry is null
            ? null
            : $"{entry.Name}\n{entry.TypeText} · {entry.SizeText}\n{entry.FullPath}";
        Cursor = entry is null ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _hoveredEntry = null;
        ToolTip = null;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var entry = HitTestEntry(e.GetPosition(this));
        if (entry is null)
        {
            return;
        }

        Focus();
        SelectedEntry = entry;
        InvalidateVisual();
        if (entry.IsDirectory && e.ClickCount == 1)
        {
            EntryInvoked?.Invoke(this, entry);
        }

        e.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var entry = HitTestEntry(e.GetPosition(this));
        if (entry is null || string.IsNullOrWhiteSpace(entry.FullPath))
        {
            return;
        }

        Focus();
        SelectedEntry = entry;
        InvalidateVisual();

        var itemKind = entry.IsDirectory ? "폴더" : "파일";
        var ndExplorerItem = new MenuItem { Header = $"ND Explorer에서 {itemKind} 위치 열기" };
        ndExplorerItem.Click += (_, _) => OpenInNdExplorerRequested?.Invoke(this, entry);

        var windowsExplorerItem = new MenuItem { Header = $"Windows 탐색기에서 {itemKind} 위치 열기" };
        windowsExplorerItem.Click += (_, _) => OpenInWindowsExplorerRequested?.Invoke(this, entry);

        if (TryFindResource(typeof(MenuItem)) is Style itemStyle)
        {
            ndExplorerItem.Style = itemStyle;
            windowsExplorerItem.Style = itemStyle;
        }

        var menu = new ContextMenu { PlacementTarget = this };
        if (TryFindResource(typeof(ContextMenu)) is Style menuStyle)
        {
            menu.Style = menuStyle;
        }

        menu.Items.Add(ndExplorerItem);
        menu.Items.Add(windowsExplorerItem);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(ContextMenu, menu))
            {
                ContextMenu = null;
            }
        };
        ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private StorageEntry? HitTestEntry(Point point)
    {
        for (var index = _layout.Count - 1; index >= 0; index--)
        {
            if (_layout[index].Bounds.Contains(point))
            {
                return _layout[index].Entry;
            }
        }

        return null;
    }

    private static Rect Inset(Rect bounds, double amount)
    {
        var width = Math.Max(0, bounds.Width - (amount * 2));
        var height = Math.Max(0, bounds.Height - (amount * 2));
        return new Rect(bounds.X + amount, bounds.Y + amount, width, height);
    }

    private static Brush[] CreateBrushes(params string[] colors)
        => colors.Select(color =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return (Brush)brush;
        }).ToArray();

    private static Pen CreatePen(string color, double thickness)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static void ItemsSource_OnChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (StorageTreemapControl)dependencyObject;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged -= control.Items_OnCollectionChanged;
        }

        control._observedCollection = e.NewValue as INotifyCollectionChanged;
        if (control._observedCollection is not null)
        {
            control._observedCollection.CollectionChanged += control.Items_OnCollectionChanged;
        }

        control.InvalidateVisual();
    }

    private void Items_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    private sealed record TreemapItem(StorageEntry Entry, Rect Bounds, int Depth);
}
