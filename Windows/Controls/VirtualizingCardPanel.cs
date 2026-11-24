using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace StickyNoteWPF;

// 管理器卡片尺寸固定，只生成视口和相邻一行，数据仍完整保存在 ItemsSource 中。
public sealed class VirtualizingCardPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingCardPanel), new FrameworkPropertyMetadata(242d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingCardPanel), new FrameworkPropertyMetadata(188d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    private Size _viewport, _extent;
    private double _offset;
    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var count = owner?.Items.Count ?? 0;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(ItemWidth, owner?.ActualWidth ?? ItemWidth);
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : Math.Max(ItemHeight, owner?.ActualHeight ?? ItemHeight);
        _columns = Math.Max(1, (int)Math.Floor(width / ItemWidth));
        _viewport = new Size(width, height);
        _extent = new Size(width, Math.Ceiling((double)count / _columns) * ItemHeight);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent.Height - height));
        ScrollOwner?.InvalidateScrollInfo();

        var first = Math.Max(0, (int)Math.Floor(_offset / ItemHeight) - 1) * _columns;
        var last = Math.Min(count - 1, ((int)Math.Ceiling((_offset + height) / ItemHeight) + 1) * _columns - 1);
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        // 先移除离开视口的容器，避免滚动或搜索后仍持有大量 TextBox 和绑定。
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var index = owner!.ItemContainerGenerator.IndexFromContainer(children[i]);
            if (index >= first && index <= last) continue;
            if (index >= 0) generator.Remove(generator.GeneratorPositionFromIndex(index), 1);
            RemoveInternalChildRange(i, 1);
        }
        if (last >= first)
        {
            var start = generator.GeneratorPositionFromIndex(first);
            var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
            using (generator.StartAt(start, GeneratorDirection.Forward, true))
                for (var index = first; index <= last; index++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var added);
                    if (added)
                    {
                        InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(ItemWidth, ItemHeight));
                }
        }
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        foreach (UIElement child in InternalChildren)
        {
            var index = owner.ItemContainerGenerator.IndexFromContainer(child);
            if (index >= 0) child.Arrange(new Rect(index % _columns * ItemWidth, index / _columns * ItemHeight - _offset, ItemWidth, ItemHeight));
        }
        return finalSize;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        ItemContainerGenerator.RemoveAll();
        RemoveInternalChildRange(0, InternalChildren.Count);
        InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int index)
    {
        var top = index / _columns * ItemHeight;
        if (top < _offset) SetVerticalOffset(top);
        else if (top + ItemHeight > _offset + _viewport.Height) SetVerticalOffset(top + ItemHeight - _viewport.Height);
    }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        DependencyObject child = visual;
        while (child != null && VisualTreeHelper.GetParent(child) != this) child = VisualTreeHelper.GetParent(child);
        if (child is not UIElement element) return Rect.Empty;
        var index = ItemsControl.GetItemsOwner(this).ItemContainerGenerator.IndexFromContainer(element);
        if (index < 0) return Rect.Empty;
        BringIndexIntoView(index);
        return new Rect(index % _columns * ItemWidth, index / _columns * ItemHeight - _offset, ItemWidth, ItemHeight);
    }
    public void SetVerticalOffset(double offset)
    {
        offset = double.IsNaN(offset) ? 0 : Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (Math.Abs(offset - _offset) < 0.01) return;
        _offset = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }
    public void LineUp() => SetVerticalOffset(_offset - 16);
    public void LineDown() => SetVerticalOffset(_offset + 16);
    public void MouseWheelUp() => SetVerticalOffset(_offset - 48);
    public void MouseWheelDown() => SetVerticalOffset(_offset + 48);
    public void PageUp() => SetVerticalOffset(_offset - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset + _viewport.Height);
    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public ScrollViewer? ScrollOwner { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public void SetHorizontalOffset(double offset) { }
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
