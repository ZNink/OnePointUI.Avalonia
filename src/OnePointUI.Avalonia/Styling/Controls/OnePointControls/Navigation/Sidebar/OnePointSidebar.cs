using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.Sidebar;

/// <summary>侧边栏选中变化参数。</summary>
public sealed class OnePointSidebarSelectionChangedEventArgs : EventArgs
{
    public OnePointSidebarSelectionChangedEventArgs(
        string? key,
        OnePointSidebarItem? item,
        bool isUserInitiated)
    {
        Key = key;
        Item = item;
        IsUserInitiated = isUserInitiated;
    }

    /// <summary>选中项的 <see cref="OnePointSidebarItem.NavigationKey" />。</summary>
    public string? Key { get; }

    public OnePointSidebarItem? Item { get; }

    /// <summary>真表示由指针或键盘激活，假表示由宿主设置 <c>SelectedKey</c>。</summary>
    public bool IsUserInitiated { get; }
}

/// <summary>
/// 贴窗口边缘的侧边栏。由上到下是品牌区（与窗口标题栏等高，直接落在标题栏带里）、
/// 导航项列表与底部状态区。
/// </summary>
/// <remarks>
/// <para>固定宽度、单一形态（只有图标栏，没有展开态）：没有宽度手柄，也不存在随模式变化的
/// 紧凑布局。宿主只需要声明当前路由（<see cref="SelectedKey" />）并注入品牌与底部内容。</para>
/// <para>外壳默认 <see cref="SidebarCornerRadius" /> 与窗口边框圆角同值，四角一致；
/// 贴在窗口左/上/下边缘时左侧两个圆角正好沿窗口圆角走，右侧两个圆角落在窗口内部。</para>
/// </remarks>
public class OnePointSidebar : ItemsControl
{
    public static readonly StyledProperty<string?> SelectedKeyProperty =
        AvaloniaProperty.Register<OnePointSidebar, string?>(nameof(SelectedKey));

    public static readonly StyledProperty<object?> BrandIconProperty =
        AvaloniaProperty.Register<OnePointSidebar, object?>(nameof(BrandIcon));

    public static readonly StyledProperty<string> BrandTitleProperty =
        AvaloniaProperty.Register<OnePointSidebar, string>(nameof(BrandTitle), "OnePointUI");

    /// <summary>底部状态区内容（账号、命令项等）。</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<OnePointSidebar, object?>(nameof(Footer));

    /// <summary>只读辅助属性：底部状态区是否为空，用于隐藏分隔线。</summary>
    public static readonly StyledProperty<bool> HasFooterProperty =
        AvaloniaProperty.Register<OnePointSidebar, bool>(nameof(HasFooter));

    /// <summary>外壳圆角。默认与窗口边框 8px 圆角一致。</summary>
    public static readonly StyledProperty<CornerRadius> SidebarCornerRadiusProperty =
        AvaloniaProperty.Register<OnePointSidebar, CornerRadius>(
            nameof(SidebarCornerRadius),
            new CornerRadius(8));

    /// <summary>
    /// 外壳材质层：宿主放实时材质用（例如侧边栏专用的模糊泰森多边形着色器）。
    /// </summary>
    /// <remarks>
    /// 该内容铺满整个侧边栏，位于 <see cref="MaskBrush" /> 遮罩层之下、导航内容之下，
    /// 且不参与命中测试（它只是背景材质，不该吃掉按钮的点击）。
    /// </remarks>
    public static readonly StyledProperty<object?> MaterialProperty =
        AvaloniaProperty.Register<OnePointSidebar, object?>(nameof(Material));

    /// <summary>
    /// 亮度遮罩：画在材质层之上的一层半透明刷子，用来压暗（深色）/提亮（浅色）侧边栏，
    /// 让 72px 图标栏在任意背景上都能读出对比度。
    /// </summary>
    /// <remarks>
    /// 宿主按背景本体决定给不给：无遮罩亚克力时置 <c>null</c>（整层不画）。
    /// 之所以做成属性、而不是让宿主往模板部件的样式里塞 <c>DynamicResource</c>，
    /// 是因为模板部件的样式在模板实例化时就求值，之后往 <c>Application.Resources</c>
    /// 里补同一个键<b>不会</b>再回流到已经应用过的 setter 上——曾经因此出现过
    /// “遮罩看起来完全失效”的问题。
    /// </remarks>
    public static readonly StyledProperty<IBrush?> MaskBrushProperty =
        AvaloniaProperty.Register<OnePointSidebar, IBrush?>(nameof(MaskBrush));

    public OnePointSidebar()
    {
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.List);
        AddHandler(Button.ClickEvent, OnItemClick, RoutingStrategies.Bubble);
    }

    /// <summary>选中项变化。宿主页面切换逻辑可订阅该事件，无需重建导航项。</summary>
    public event EventHandler<OnePointSidebarSelectionChangedEventArgs>? SelectionChanged;

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

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool HasFooter => GetValue(HasFooterProperty);

    public CornerRadius SidebarCornerRadius
    {
        get => GetValue(SidebarCornerRadiusProperty);
        set => SetValue(SidebarCornerRadiusProperty, value);
    }

    public object? Material
    {
        get => GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }

    public IBrush? MaskBrush
    {
        get => GetValue(MaskBrushProperty);
        set => SetValue(MaskBrushProperty, value);
    }

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
            new OnePointSidebarSelectionChangedEventArgs(key, item, userInitiated));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplySelection();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedKeyProperty)
        {
            ApplySelection();
        }
        else if (change.Property == FooterProperty)
        {
            SetValue(HasFooterProperty, Footer is not null);
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
        }

        base.OnKeyDown(e);
    }

    /// <summary>按视觉顺序枚举导航项（主体 + 底部状态区）。</summary>
    internal IEnumerable<OnePointSidebarItem> EnumerateItems()
    {
        foreach (var entry in Items)
        {
            if (entry is OnePointSidebarItem item)
            {
                yield return item;
            }
        }

        foreach (var item in EnumerateLogicalTree(Footer))
        {
            yield return item;
        }
    }

    private static IEnumerable<OnePointSidebarItem> EnumerateLogicalTree(object? node)
    {
        switch (node)
        {
            case OnePointSidebarItem item:
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

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not OnePointSidebarItem item || !item.IsEffectivelyEnabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.NavigationKey))
        {
            // 无导航键的项按“命令项”处理，不改变当前选中态。
            SelectionChanged?.Invoke(
                this,
                new OnePointSidebarSelectionChangedEventArgs(SelectedKey, item, true));
            return;
        }

        SelectedKey = item.NavigationKey;
        SelectionChanged?.Invoke(
            this,
            new OnePointSidebarSelectionChangedEventArgs(item.NavigationKey, item, true));
    }

    private void ApplySelection()
    {
        var key = SelectedKey;
        foreach (var item in EnumerateItems())
        {
            item.IsSelected = !string.IsNullOrEmpty(key)
                              && !string.IsNullOrEmpty(item.NavigationKey)
                              && string.Equals(item.NavigationKey, key, StringComparison.Ordinal);
        }
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
