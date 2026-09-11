using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.NavigationRail;

/// <summary>导航栏选中变化参数。</summary>
public sealed class OnePointNavigationRailSelectionChangedEventArgs : EventArgs
{
    public OnePointNavigationRailSelectionChangedEventArgs(
        string? key,
        OnePointNavigationRailItem? item,
        bool isUserInitiated)
    {
        Key = key;
        Item = item;
        IsUserInitiated = isUserInitiated;
    }

    /// <summary>选中项的 <see cref="OnePointNavigationRailItem.NavigationKey" />。</summary>
    public string? Key { get; }

    public OnePointNavigationRailItem? Item { get; }

    /// <summary>真表示由指针或键盘激活，假表示由宿主设置 <c>SelectedKey</c>。</summary>
    public bool IsUserInitiated { get; }
}

/// <summary>导航栏展开态变化参数。</summary>
public sealed class OnePointNavigationRailExpansionChangedEventArgs : EventArgs
{
    public OnePointNavigationRailExpansionChangedEventArgs(bool isExpanded, bool isExplicitToggle)
    {
        IsExpanded = isExpanded;
        IsExplicitToggle = isExplicitToggle;
    }

    public bool IsExpanded { get; }

    /// <summary>
    /// 真表示用户通过折叠按钮明确切换，宿主应据此持久化为固定的展开/折叠模式；
    /// 假表示程序化布局（如窄窗口自动折叠）或 Esc 临时折叠，宿主应保持原模式。
    /// </summary>
    public bool IsExplicitToggle { get; }
}

/// <summary>导航栏宽度变化参数（拖动右侧手柄调整展开宽度）。</summary>
public sealed class OnePointNavigationRailWidthChangedEventArgs : EventArgs
{
    public OnePointNavigationRailWidthChangedEventArgs(double width, bool isCompleted)
    {
        Width = width;
        IsCompleted = isCompleted;
    }

    /// <summary>拖动后的展开宽度（已按最小/最大值收敛）。</summary>
    public double Width { get; }

    /// <summary>真表示拖动结束，宿主可在此持久化宽度。</summary>
    public bool IsCompleted { get; }
}

/// <summary>
/// 激发象限竖直导航栏。由上到下依次是品牌区、象限分组导航区、弹性滚动区与底部状态区；
/// 支持展开/折叠、分组、徽标、禁用、键盘导航与无障碍命名。
/// </summary>
/// <remarks>
/// 组件不包含任何业务概念：宿主通过 <see cref="SelectedKey" /> 声明当前路由，
/// 通过 <see cref="Footer" />、<see cref="BrandIcon" /> 等属性注入内容。
/// </remarks>
public class OnePointNavigationRail : ItemsControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, bool>(nameof(IsExpanded), true);

    public static readonly StyledProperty<double> ExpandedWidthProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, double>(nameof(ExpandedWidth), 248d);

    public static readonly StyledProperty<double> CollapsedWidthProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, double>(nameof(CollapsedWidth), 68d);

    /// <summary>是否允许拖动右侧手柄改变展开宽度。</summary>
    public static readonly StyledProperty<bool> IsResizableProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, bool>(nameof(IsResizable), true);

    public static readonly StyledProperty<double> MinExpandedWidthProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, double>(nameof(MinExpandedWidth), 180d);

    public static readonly StyledProperty<double> MaxExpandedWidthProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, double>(nameof(MaxExpandedWidth), 420d);

    /// <summary>只读：当前是否正在拖动宽度手柄。</summary>
    public static readonly StyledProperty<bool> IsResizingProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, bool>(nameof(IsResizing));

    /// <summary>只读：按展开态解析出的实际宽度，模板用它驱动宽度过渡动画。</summary>
    public static readonly StyledProperty<double> CurrentWidthProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, double>(nameof(CurrentWidth), 248d);

    /// <summary>当前选中的导航键。宿主页面切换时同步该值即可自动高亮对应项。</summary>
    public static readonly StyledProperty<string?> SelectedKeyProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string?>(nameof(SelectedKey));

    public static readonly StyledProperty<object?> BrandIconProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, object?>(nameof(BrandIcon));

    public static readonly StyledProperty<string> BrandTitleProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(BrandTitle), "OnePointUI");

    public static readonly StyledProperty<string> BrandDescriptionProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(BrandDescription), string.Empty);

    /// <summary>底部状态区内容（账号、设置、帮助、通知等）。</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, object?>(nameof(Footer));

    /// <summary>只读辅助属性：底部状态区是否为空，用于隐藏分隔线。</summary>
    public static readonly StyledProperty<bool> HasFooterProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, bool>(nameof(HasFooter));

    public static readonly StyledProperty<string> CollapseGlyphProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(CollapseGlyph), "\uE76B");

    public static readonly StyledProperty<string> ExpandGlyphProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(ExpandGlyph), "\uE76C");

    /// <summary>折叠按钮的无障碍名称（不弹出 ToolTip）。</summary>
    public static readonly StyledProperty<string> CollapseHintProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(CollapseHint), "折叠导航栏");

    public static readonly StyledProperty<string> ExpandHintProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(ExpandHint), "展开导航栏");

    /// <summary>只读：当前折叠按钮字形，随展开态切换。</summary>
    public static readonly StyledProperty<string> CurrentCollapseGlyphProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(CurrentCollapseGlyph), "\uE76B");

    /// <summary>只读：当前折叠按钮的无障碍名称，随展开态切换。</summary>
    public static readonly StyledProperty<string> CurrentCollapseHintProperty =
        AvaloniaProperty.Register<OnePointNavigationRail, string>(nameof(CurrentCollapseHint), "折叠导航栏");

    private Button? _collapseToggle;
    private Panel? _resizeGrip;
    private bool _isDraggingWidth;
    private double _dragStartWidth;
    private double _dragStartX;
    private bool _isExplicitExpansionToggle;

    public OnePointNavigationRail()
    {
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
        AddHandler(Button.ClickEvent, OnRailItemClick, RoutingStrategies.Bubble);
        UpdatePseudoClasses();
        UpdateDerivedValues();
    }

    /// <summary>选中项变化。宿主页面切换逻辑可订阅该事件，无需重建导航项。</summary>
    public event EventHandler<OnePointNavigationRailSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// 展开态变化。<see cref="OnePointNavigationRailExpansionChangedEventArgs.IsExplicitToggle" />
    /// 区分用户点击折叠按钮与程序化/键盘触发的展开态变化。
    /// </summary>
    public event EventHandler<OnePointNavigationRailExpansionChangedEventArgs>? ExpansionChanged;

    /// <summary>
    /// 拖动右侧手柄改变展开宽度：拖动过程中 <c>IsCompleted=false</c>（宿主实时重排），
    /// 松开时 <c>IsCompleted=true</c>（宿主持久化）。
    /// </summary>
    public event EventHandler<OnePointNavigationRailWidthChangedEventArgs>? WidthChanged;

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public double ExpandedWidth
    {
        get => GetValue(ExpandedWidthProperty);
        set => SetValue(ExpandedWidthProperty, value);
    }

    public double CollapsedWidth
    {
        get => GetValue(CollapsedWidthProperty);
        set => SetValue(CollapsedWidthProperty, value);
    }

    public bool IsResizable
    {
        get => GetValue(IsResizableProperty);
        set => SetValue(IsResizableProperty, value);
    }

    public double MinExpandedWidth
    {
        get => GetValue(MinExpandedWidthProperty);
        set => SetValue(MinExpandedWidthProperty, value);
    }

    public double MaxExpandedWidth
    {
        get => GetValue(MaxExpandedWidthProperty);
        set => SetValue(MaxExpandedWidthProperty, value);
    }

    public bool IsResizing => GetValue(IsResizingProperty);

    public double CurrentWidth => GetValue(CurrentWidthProperty);

    public string? SelectedKey
    {
        get => GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    public object? BrandIcon
    {
        get => GetValue(BrandIconProperty);
        set => SetValue(BrandIconProperty, value);
    }

    public string BrandTitle
    {
        get => GetValue(BrandTitleProperty);
        set => SetValue(BrandTitleProperty, value);
    }

    public string BrandDescription
    {
        get => GetValue(BrandDescriptionProperty);
        set => SetValue(BrandDescriptionProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool HasFooter => GetValue(HasFooterProperty);

    public string CollapseGlyph
    {
        get => GetValue(CollapseGlyphProperty);
        set => SetValue(CollapseGlyphProperty, value);
    }

    public string ExpandGlyph
    {
        get => GetValue(ExpandGlyphProperty);
        set => SetValue(ExpandGlyphProperty, value);
    }

    public string CollapseHint
    {
        get => GetValue(CollapseHintProperty);
        set => SetValue(CollapseHintProperty, value);
    }

    public string ExpandHint
    {
        get => GetValue(ExpandHintProperty);
        set => SetValue(ExpandHintProperty, value);
    }

    public string CurrentCollapseGlyph => GetValue(CurrentCollapseGlyphProperty);

    public string CurrentCollapseHint => GetValue(CurrentCollapseHintProperty);

    /// <summary>按导航键选中，用于宿主路由变化时的程序化同步。</summary>
    public void SelectKey(string? key, bool userInitiated = false)
    {
        var item = key is null
            ? null
            : EnumerateItems().FirstOrDefault(candidate =>
                !string.IsNullOrEmpty(candidate.NavigationKey)
                && string.Equals(candidate.NavigationKey, key, StringComparison.Ordinal));
        SelectedKey = key;
        SelectionChanged?.Invoke(
            this,
            new OnePointNavigationRailSelectionChangedEventArgs(key, item, userInitiated));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_collapseToggle is not null)
        {
            _collapseToggle.Click -= OnCollapseToggleClick;
        }

        _collapseToggle = e.NameScope.Find<Button>("PART_CollapseToggle");
        if (_collapseToggle is not null)
        {
            _collapseToggle.Click += OnCollapseToggleClick;
        }

        if (_resizeGrip is not null)
        {
            DetachResizeGrip(_resizeGrip);
        }

        _resizeGrip = e.NameScope.Find<Panel>("PART_ResizeGrip");
        if (_resizeGrip is not null)
        {
            _resizeGrip.PointerPressed += OnResizeGripPointerPressed;
            _resizeGrip.PointerMoved += OnResizeGripPointerMoved;
            _resizeGrip.PointerReleased += OnResizeGripPointerReleased;
            _resizeGrip.PointerCaptureLost += OnResizeGripPointerCaptureLost;
        }

        PropagateState();
    }

    private void DetachResizeGrip(Panel grip)
    {
        grip.PointerPressed -= OnResizeGripPointerPressed;
        grip.PointerMoved -= OnResizeGripPointerMoved;
        grip.PointerReleased -= OnResizeGripPointerReleased;
        grip.PointerCaptureLost -= OnResizeGripPointerCaptureLost;
    }

    /// <summary>
    /// 拖动右侧手柄改变展开宽度。位移按手柄所在导航栏的局部坐标换算，
    /// 因此拖动时导航栏变宽不会影响指针与手柄的相对关系。
    /// </summary>
    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsResizable || !IsExpanded || sender is not Panel grip)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDraggingWidth = true;
        _dragStartWidth = ExpandedWidth;
        _dragStartX = e.GetPosition(this).X;
        SetValue(IsResizingProperty, true);
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnResizeGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingWidth || !IsExpanded)
        {
            return;
        }

        var width = Math.Clamp(
            _dragStartWidth + (e.GetPosition(this).X - _dragStartX),
            Math.Min(MinExpandedWidth, MaxExpandedWidth),
            Math.Max(MinExpandedWidth, MaxExpandedWidth));
        if (Math.Abs(width - ExpandedWidth) < 0.5)
        {
            e.Handled = true;
            return;
        }

        ExpandedWidth = width;
        WidthChanged?.Invoke(this, new OnePointNavigationRailWidthChangedEventArgs(width, false));
        e.Handled = true;
    }

    private void OnResizeGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingWidth)
        {
            return;
        }

        EndWidthDrag(endPointer: e.Pointer);
        e.Handled = true;
    }

    private void OnResizeGripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        EndWidthDrag(endPointer: null);

    private void EndWidthDrag(IPointer? endPointer)
    {
        if (!_isDraggingWidth)
        {
            return;
        }

        _isDraggingWidth = false;
        endPointer?.Capture(null);
        SetValue(IsResizingProperty, false);
        WidthChanged?.Invoke(this, new OnePointNavigationRailWidthChangedEventArgs(ExpandedWidth, true));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PropagateState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty)
        {
            UpdatePseudoClasses();
            UpdateDerivedValues();
            PropagateState();
            ExpansionChanged?.Invoke(
                this,
                new OnePointNavigationRailExpansionChangedEventArgs(
                    IsExpanded,
                    _isExplicitExpansionToggle));
        }
        else if (change.Property == ExpandedWidthProperty || change.Property == CollapsedWidthProperty)
        {
            UpdateDerivedValues();
        }
        else if (change.Property == SelectedKeyProperty)
        {
            ApplySelection();
        }
        else if (change.Property == IsResizingProperty)
        {
            PseudoClasses.Set(":resizing", IsResizing);
        }
        else if (change.Property == FooterProperty)
        {
            SetValue(HasFooterProperty, Footer is not null);
            PropagateState();
        }
        else if (change.Property == CollapseGlyphProperty
                 || change.Property == ExpandGlyphProperty
                 || change.Property == CollapseHintProperty
                 || change.Property == ExpandHintProperty)
        {
            UpdateDerivedValues();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveFocus(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                break;
            case Key.Home:
                FocusEdge(0);
                e.Handled = true;
                break;
            case Key.End:
                FocusEdge(-1);
                e.Handled = true;
                break;
            case Key.Escape:
                if (IsExpanded)
                {
                    IsExpanded = false;
                    e.Handled = true;
                }

                break;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// 递归展开象限分组，得到稳定顺序的导航项集合。使用逻辑结构而非视觉树，
    /// 因此不再依赖“分组是否已经完成模板化”。
    /// </summary>
    internal IEnumerable<OnePointNavigationRailItem> EnumerateItems()
    {
        foreach (var entry in Items)
        {
            foreach (var item in EnumerateItem(entry))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// 底部状态区里的导航项（例如“退出”）。它们不属于 <see cref="ItemsControl.Items" />，
    /// 但同样需要跟随展开/折叠态，否则折叠后图标不会切到居中布局。
    /// </summary>
    private IEnumerable<OnePointNavigationRailItem> EnumerateFooterItems()
    {
        foreach (var item in EnumerateLogicalTree(Footer))
        {
            yield return item;
        }
    }

    private static IEnumerable<OnePointNavigationRailItem> EnumerateLogicalTree(object? node)
    {
        switch (node)
        {
            case OnePointNavigationRailItem item:
                yield return item;
                break;
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    foreach (var item in EnumerateLogicalTree(child))
                    {
                        yield return item;
                    }
                }

                break;
            case Border border:
                foreach (var item in EnumerateLogicalTree(border.Child))
                {
                    yield return item;
                }

                break;
            case ContentControl contentControl:
                foreach (var item in EnumerateLogicalTree(contentControl.Content))
                {
                    yield return item;
                }

                break;
        }
    }

    private static IEnumerable<OnePointNavigationRailItem> EnumerateItem(object? entry)
    {
        switch (entry)
        {
            case OnePointNavigationRailItem item:
                yield return item;
                break;
            case OnePointNavigationRailGroup group:
                foreach (var child in group.Items)
                {
                    foreach (var item in EnumerateItem(child))
                    {
                        yield return item;
                    }
                }

                break;
        }
    }

    private static IEnumerable<OnePointNavigationRailGroup> EnumerateGroups(object? entry)
    {
        if (entry is OnePointNavigationRailGroup group)
        {
            yield return group;
        }
    }

    private void OnCollapseToggleClick(object? sender, RoutedEventArgs e)
    {
        _isExplicitExpansionToggle = true;
        try
        {
            IsExpanded = !IsExpanded;
        }
        finally
        {
            _isExplicitExpansionToggle = false;
        }

        e.Handled = true;
    }

    private void OnRailItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not OnePointNavigationRailItem item || !item.IsEffectivelyEnabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.NavigationKey))
        {
            // 无导航键的项按“命令项”处理，不改变当前选中态。
            SelectionChanged?.Invoke(
                this,
                new OnePointNavigationRailSelectionChangedEventArgs(SelectedKey, item, true));
            return;
        }

        SelectedKey = item.NavigationKey;
        SelectionChanged?.Invoke(
            this,
            new OnePointNavigationRailSelectionChangedEventArgs(item.NavigationKey, item, true));
    }

    private void ApplySelection()
    {
        foreach (var item in EnumerateItems())
        {
            item.IsSelected = !string.IsNullOrEmpty(SelectedKey)
                              && !string.IsNullOrEmpty(item.NavigationKey)
                              && string.Equals(item.NavigationKey, SelectedKey, StringComparison.Ordinal);
        }
    }

    private void PropagateState()
    {
        foreach (var item in EnumerateItems())
        {
            item.IsCompact = !IsExpanded;
        }

        foreach (var item in EnumerateFooterItems())
        {
            item.IsCompact = !IsExpanded;
        }

        foreach (var group in Items.SelectMany(EnumerateGroups))
        {
            group.IsCompact = !IsExpanded;
        }

        ApplySelection();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":collapsed", !IsExpanded);
    }

    private void UpdateDerivedValues()
    {
        SetValue(CurrentWidthProperty, IsExpanded ? ExpandedWidth : CollapsedWidth);
        SetValue(CurrentCollapseGlyphProperty, IsExpanded ? CollapseGlyph : ExpandGlyph);
        SetValue(CurrentCollapseHintProperty, IsExpanded ? CollapseHint : ExpandHint);
    }

    private void MoveFocus(int delta)
    {
        var items = EnumerateItems().Where(item => item.IsEffectivelyEnabled).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        var index = items.FindIndex(item => ReferenceEquals(item, current));
        var next = index < 0
            ? (delta >= 0 ? 0 : items.Count - 1)
            : ((index + delta) % items.Count + items.Count) % items.Count;
        items[next].Focus(NavigationMethod.Tab);
    }

    private void FocusEdge(int edge)
    {
        var items = EnumerateItems().Where(item => item.IsEffectivelyEnabled).ToList();
        if (items.Count == 0)
        {
            return;
        }

        items[edge < 0 ? items.Count - 1 : 0].Focus(NavigationMethod.Tab);
    }
}
