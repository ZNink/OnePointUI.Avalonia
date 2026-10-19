using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Threading;
using OnePointUI.Avalonia.Styling.Controls.OnePointControls.Adaptive;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

/// <summary>
/// 按钮集群：一个统一面板包裹多个常规按钮。按钮本身无边框、彼此无缝隙，
/// 由面板统一把控圆角与视觉一致性。
/// </summary>
/// <remarks>
/// <para>三套显示样式（<see cref="DisplayMode"/>）：</para>
/// <list type="bullet">
/// <item><b>正常样式</b>（默认）：统一面板 + 完整按钮（图标 + 文本），间距与圆角由面板统一把控。</item>
/// <item><b>动态样式</b>：默认仅主项（<see cref="PrimaryItemMode"/> 指定，默认取高亮按钮）展开，
/// 其余按钮收缩为图标（无图标时显示名称首字符头像）；鼠标移到任一按钮上时该按钮临时展开，
/// 且与主项展开<b>互斥</b>——任一时刻至多一个按钮处于展开态，指针移出后主项恢复展开。</item>
/// <item><b>自适应样式</b>：父级宽度低于 <see cref="AdaptiveThresholdWidth"/> 时自动进入动态样式，
/// 宽度恢复后自动回到正常样式，切换过程播放非线性过渡动画。</item>
/// </list>
/// </remarks>
public class ButtonCluster : ItemsControl
{
    /// <summary>动态样式生效时宿主上的伪类。</summary>
    internal const string DynamicPseudoClass = ":dynamic";

    /// <summary>条目处于收缩形态时挂在容器上的类。</summary>
    internal const string ItemCollapsedClass = "collapsed";

    /// <summary>主项不显示文本时挂在容器上的类。</summary>
    internal const string ItemNoTextClass = "no-text";

    /// <summary>默认收缩宽度。</summary>
    internal const double DefaultCollapsedItemWidth = 40;

    private AdaptiveWidthTracker? _tracker;
    private ButtonClusterItem? _primaryItem;
    private bool _dynamicActive;

    /// <summary>当前持有「临时展开资格」的按钮（悬停 / 键盘聚焦申领，全局唯一）。</summary>
    private ButtonClusterItem? _temporaryItem;

    /// <summary>是否正在广播刷新：期间禁止按钮交叉申领临时资格，防止无限乒乓。</summary>
    private bool _claiming;

    public static readonly StyledProperty<AdaptiveDisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<ButtonCluster, AdaptiveDisplayMode>(nameof(DisplayMode));

    public static readonly StyledProperty<double> AdaptiveThresholdWidthProperty =
        AvaloniaProperty.Register<ButtonCluster, double>(
            nameof(AdaptiveThresholdWidth),
            defaultValue: 640d);

    public static readonly StyledProperty<AdaptiveWidthSource> AdaptiveWidthSourceProperty =
        AvaloniaProperty.Register<ButtonCluster, AdaptiveWidthSource>(
            nameof(AdaptiveWidthSource),
            defaultValue: AdaptiveWidthSource.Parent);

    public static readonly StyledProperty<double> AdaptiveHysteresisWidthProperty =
        AvaloniaProperty.Register<ButtonCluster, double>(
            nameof(AdaptiveHysteresisWidth),
            defaultValue: 8d);

    public static readonly StyledProperty<double> CollapsedItemWidthProperty =
        AvaloniaProperty.Register<ButtonCluster, double>(
            nameof(CollapsedItemWidth),
            defaultValue: DefaultCollapsedItemWidth);

    public static readonly StyledProperty<bool> IsHoverExpansionEnabledProperty =
        AvaloniaProperty.Register<ButtonCluster, bool>(nameof(IsHoverExpansionEnabled), true);

    public static readonly StyledProperty<AdaptivePrimaryItemMode> PrimaryItemModeProperty =
        AvaloniaProperty.Register<ButtonCluster, AdaptivePrimaryItemMode>(
            nameof(PrimaryItemMode),
            defaultValue: AdaptivePrimaryItemMode.Highlighted);

    public static readonly StyledProperty<bool> ShowPrimaryItemTextProperty =
        AvaloniaProperty.Register<ButtonCluster, bool>(nameof(ShowPrimaryItemText), true);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<ButtonCluster, double>(nameof(ItemSpacing), 0d);

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ButtonCluster, Orientation>(nameof(Orientation), Orientation.Horizontal);

    static ButtonCluster()
    {
        DisplayModeProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.OnAdaptiveSettingsChanged());
        AdaptiveThresholdWidthProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.OnAdaptiveSettingsChanged());
        AdaptiveHysteresisWidthProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.OnAdaptiveSettingsChanged());
        AdaptiveWidthSourceProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.OnAdaptiveSettingsChanged());

        // 收缩宽度 / 悬停开关 / 主项文本开关变化必须无条件刷新全部按钮：
        // 按需广播会在按钮形态未变时短路，导致新宽度、新开关不生效。
        CollapsedItemWidthProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.BroadcastRefresh());
        IsHoverExpansionEnabledProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.BroadcastRefresh());
        ShowPrimaryItemTextProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.BroadcastRefresh());
        PrimaryItemModeProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.InvalidatePrimary());
        ItemSpacingProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.UpdateItemsPanel());
        OrientationProperty.Changed.AddClassHandler<ButtonCluster>(
            (cluster, _) => cluster.UpdateItemsPanel());
    }

    /// <summary>显示样式档位。</summary>
    public AdaptiveDisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>自适应阈值：参考宽度低于该值时切换为动态样式。≤ 0 视为关闭自适应。</summary>
    public double AdaptiveThresholdWidth
    {
        get => GetValue(AdaptiveThresholdWidthProperty);
        set => SetValue(AdaptiveThresholdWidthProperty, value);
    }

    /// <summary>阈值参考的宽度来源：父级控件（默认）或控件自身。</summary>
    public AdaptiveWidthSource AdaptiveWidthSource
    {
        get => GetValue(AdaptiveWidthSourceProperty);
        set => SetValue(AdaptiveWidthSourceProperty, value);
    }

    /// <summary>滞回宽度：进入动态样式后，需参考宽度 ≥ 阈值 + 滞回才切回正常样式。0 表示关闭滞回。</summary>
    public double AdaptiveHysteresisWidth
    {
        get => GetValue(AdaptiveHysteresisWidthProperty);
        set => SetValue(AdaptiveHysteresisWidthProperty, value);
    }

    /// <summary>动态样式下未展开按钮的宽度（图标 / 首字符头像态）。</summary>
    public double CollapsedItemWidth
    {
        get => GetValue(CollapsedItemWidthProperty);
        set => SetValue(CollapsedItemWidthProperty, value);
    }

    /// <summary>动态样式下是否允许悬停 / 键盘聚焦临时展开未展开按钮。默认 true。</summary>
    public bool IsHoverExpansionEnabled
    {
        get => GetValue(IsHoverExpansionEnabledProperty);
        set => SetValue(IsHoverExpansionEnabledProperty, value);
    }

    /// <summary>动态样式下主项（默认展开的按钮）的指定方式，默认为高亮样式的按钮。</summary>
    public AdaptivePrimaryItemMode PrimaryItemMode
    {
        get => GetValue(PrimaryItemModeProperty);
        set => SetValue(PrimaryItemModeProperty, value);
    }

    /// <summary>
    /// 动态样式下主项是否显示文本。为 false 时主项也退化为图标 / 首字符
    /// （宽度仍按展开态度量，只是不占文本列）。
    /// </summary>
    public bool ShowPrimaryItemText
    {
        get => GetValue(ShowPrimaryItemTextProperty);
        set => SetValue(ShowPrimaryItemTextProperty, value);
    }

    /// <summary>按钮间距（面板内相邻按钮的间隔）。默认 0：按钮间无缝隙。</summary>
    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>集群排布方向：横向（默认）或纵向。</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>动态样式当前是否生效（自适应判定后的实际状态）。</summary>
    internal bool IsDynamicEffective => _dynamicActive;

    public ButtonCluster()
    {
        Items.CollectionChanged += (_, _) => InvalidatePrimary();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateItemsPanel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _tracker ??= new AdaptiveWidthTracker(this, () => AdaptiveWidthSource == AdaptiveWidthSource.Parent);
        _tracker.WidthChanged -= OnTrackedWidthChanged;
        _tracker.WidthChanged += OnTrackedWidthChanged;
        _tracker.Start();
        EvaluateAdaptive();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_tracker is not null)
        {
            _tracker.WidthChanged -= OnTrackedWidthChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override bool NeedsContainerOverride(
        object? item,
        int index,
        out object? recycleKey) =>
        NeedsContainer<ButtonClusterItem>(item, out recycleKey);

    protected override Control CreateContainerForItemOverride(
        object? item,
        int index,
        object? recycleKey) =>
        new ButtonClusterItem();

    /// <summary>指定的项是否为动态样式下的主项（默认展开的那一个）。</summary>
    /// <remarks>指定与动态样式是否生效无关；条目只在收缩判定时查询本属性。</remarks>
    internal bool IsPrimaryItem(ButtonClusterItem item)
        => ReferenceEquals(_primaryItem ??= ResolvePrimaryItem(), item);

    /// <summary>主项指定条件变化（高亮切换、增删项、模式变化）：清缓存并刷新全部按钮。</summary>
    internal void InvalidatePrimary()
    {
        _primaryItem = null;
        // 无条件刷新：主项可能易主，旧主项要折叠、新主项要展开，
        // 按需广播会在形态未变时短路。
        BroadcastRefresh();
    }

    private ButtonClusterItem? ResolvePrimaryItem()
    {
        if (PrimaryItemMode == AdaptivePrimaryItemMode.None)
        {
            return null;
        }

        var items = EnumerateItems();
        return PrimaryItemMode == AdaptivePrimaryItemMode.Highlighted
            ? items.FirstOrDefault(item => item.IsHighlighted)
            : items.FirstOrDefault();
    }

    private void OnAdaptiveSettingsChanged() => EvaluateAdaptive();

    private void OnTrackedWidthChanged(object? sender, EventArgs e) => EvaluateAdaptive();

    private void EvaluateAdaptive()
    {
        var width = _tracker?.ObservedWidth ?? 0d;
        var active = AdaptiveDisplayMath.IsDynamicActive(
            DisplayMode,
            width,
            AdaptiveThresholdWidth,
            AdaptiveHysteresisWidth,
            _dynamicActive);
        SetDynamicActive(active);
    }

    private void SetDynamicActive(bool active)
    {
        if (_dynamicActive == active)
        {
            return;
        }

        _dynamicActive = active;
        PseudoClasses.Set(DynamicPseudoClass, active);
        BroadcastCompactMode();
    }

    private void BroadcastCompactMode()
    {
        if (ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is ButtonClusterItem item)
            {
                item.ApplyCompactMode(_dynamicActive);
            }
        }
    }

    /// <summary>
    /// 当前持有「临时展开资格」的按钮（悬停 / 键盘聚焦申领，全局唯一）。
    /// 该资格与主项展开互斥：非 null 时连主项也一并折叠，从而保证动态样式下
    /// 任一时刻至多一个按钮处于展开态。
    /// </summary>
    internal ButtonClusterItem? TemporaryItem => _temporaryItem;

    /// <summary>
    /// 按钮申领 / 归还临时展开资格。任一时刻至多一个按钮持有：持有时其余按钮
    /// （含主项）全部折叠，杜绝多个按钮同时展开或残留展开态。
    /// </summary>
    internal void SetTemporaryItem(ButtonClusterItem? item)
    {
        if (ReferenceEquals(_temporaryItem, item))
        {
            return;
        }

        // 广播刷新期间只允许归还资格、禁止交叉申领：合成指针事件（指针从一按钮
        // 直接进入另一按钮而不触发退出）可能让多个按钮同时带有悬停标志，交叉
        // 申领会无限乒乓；归还是幂等的，必须放行，否则会残留展开态。
        if (_claiming && item is not null)
        {
            return;
        }

        var previous = _temporaryItem;
        _temporaryItem = item;
        _claiming = true;
        try
        {
            // 展开形态会变的只有两方：旧资格持有者（折叠）与主项（折叠 / 恢复）。
            // 新资格持有者正处在自己的 Refresh 流程中，会自行完成展开。
            previous?.Refresh();
            RefreshPrimaryItem();
        }
        finally
        {
            _claiming = false;
        }
    }

    /// <summary>刷新当前主项：互斥折叠 / 恢复展开的唯一对象。</summary>
    private void RefreshPrimaryItem()
    {
        if (ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is ButtonClusterItem item && IsPrimaryItem(item))
            {
                item.Refresh();
            }
        }
    }

    /// <summary>无条件刷新全部按钮（与 <see cref="BroadcastCompactMode"/> 的按需广播互补）。</summary>
    private void BroadcastRefresh()
    {
        if (ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        _claiming = true;
        try
        {
            foreach (var child in panel.Children)
            {
                if (child is ButtonClusterItem item)
                {
                    item.Refresh();
                }
            }
        }
        finally
        {
            _claiming = false;
        }
    }

    /// <summary>把间距与方向同步到内部 StackPanel（仅当调用方没有替换自定义面板时）。</summary>
    private void UpdateItemsPanel()
    {
        if (ItemsPanelRoot is StackPanel panel)
        {
            panel.Spacing = ItemSpacing;
            panel.Orientation = Orientation;
        }
    }

    private IEnumerable<ButtonClusterItem> EnumerateItems()
    {
        if (ItemsPanelRoot is not Panel panel)
        {
            yield break;
        }

        foreach (var child in panel.Children)
        {
            if (child is ButtonClusterItem item)
            {
                yield return item;
            }
        }
    }
}

/// <summary>
/// <see cref="ButtonCluster"/> 中的按钮：常规 OnePointUI 按钮 + 图标 / 首字符头像 / 文本结构。
/// </summary>
/// <remarks>
/// 继承自 <see cref="Button"/>，因此 Click / Command / 禁用态与普通按钮完全一致；
/// 动态样式下的展开 / 收缩由宿主广播驱动，悬停与键盘聚焦可临时展开，且与主项
/// 展开全局互斥（资格由宿主 <c>SetTemporaryItem</c> 统一仲裁）。
/// </remarks>
public class ButtonClusterItem : Button
{
    /// <summary>无图标时动态样式下显示的首字符来源名称。</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ButtonClusterItem, string?>(nameof(Text));

    /// <summary>Fluent 图标字形（Segoe Fluent Icons）；为空时动态样式下显示首字符头像。</summary>
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<ButtonClusterItem, string>(nameof(Glyph), string.Empty);

    /// <summary>是否为高亮按钮：默认作为动态样式下的主项（按 <see cref="ButtonCluster.PrimaryItemMode"/> 规范）。</summary>
    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<ButtonClusterItem, bool>(nameof(IsHighlighted));

    private const double DefaultCollapsedWidth = ButtonCluster.DefaultCollapsedItemWidth;

    private ButtonCluster? _host;
    private Control? _fullIconHost;
    private Control? _compactIconHost;
    private Control? _avatarHost;
    private TextBlock? _avatarText;
    private bool _compact;
    private bool _expanded = true;
    private bool _pointerOver;
    private bool _widthManaged;
    private bool _identityDirty;
    private bool _refreshQueued;

    static ButtonClusterItem()
    {
        TextProperty.Changed.AddClassHandler<ButtonClusterItem>(
            (item, _) => item.OnIdentityChanged());
        GlyphProperty.Changed.AddClassHandler<ButtonClusterItem>(
            (item, _) => item.OnIdentityChanged());
        ContentProperty.Changed.AddClassHandler<ButtonClusterItem>(
            (item, _) => item.OnIdentityChanged());
        IsHighlightedProperty.Changed.AddClassHandler<ButtonClusterItem>((item, _) =>
        {
            item.PseudoClasses.Set(":highlighted", item.IsHighlighted);
            item._host?.InvalidatePrimary();
            item.Refresh();
        });
    }

    /// <summary>按钮名称。无图标时作为动态样式下首字符头像的来源，并用于无障碍名称。</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Fluent 图标字形；为空表示该按钮没有图标。</summary>
    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>是否按高亮样式呈现（强调色底 / 强调色前景）。</summary>
    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>该按钮宽度是否已由控件托管（进入过动态样式后为 true）。</summary>
    internal bool IsWidthManaged => _widthManaged;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _fullIconHost = e.NameScope.Find<Control>("PART_Icon");
        _compactIconHost = e.NameScope.Find<Control>("PART_CompactIcon");
        _avatarHost = e.NameScope.Find<Control>("PART_Avatar");
        _avatarText = e.NameScope.Find<TextBlock>("PART_AvatarText");
        UpdateIdentityVisuals(showText: !_compact);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 自行向宿主拉取当前显示模式：后加入的按钮不需要宿主额外广播。
        _host ??= FindAncestor<ButtonCluster>(this);
        ApplyCompactMode(_host?.IsDynamicEffective == true);

        // 重新挂载时再同步一次：按当前指针 / 焦点状态申领或归还互斥资格。
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // 按钮被移除 / 回收，或宿主整体脱离可视树：归还互斥展开资格并清零
        // 悬停标志。分离期间不会收到 PointerExited，不清理会在重新挂载后
        // 残留「悬停展开」态。
        _pointerOver = false;
        if (_host is not null && ReferenceEquals(_host.TemporaryItem, this))
        {
            _host.SetTemporaryItem(null);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        // 本地标志：与 IsPointerOver 等价，但在合成指针事件（无头测试）下同样确定。
        _pointerOver = true;
        Refresh();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerOver = false;
        Refresh();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Refresh();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Input);
    }

    /// <summary>宿主广播：动态样式是否生效。</summary>
    internal void ApplyCompactMode(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }

        _compact = compact;
        Refresh();
    }

    private void OnIdentityChanged()
    {
        _identityDirty = true;
        QueueRefresh();
    }

    /// <summary>
    /// 把宽度刷新排到当前属性通知之后执行。
    /// </summary>
    /// <remarks>
    /// 内容变化时，条目模板里 <c>Content="{TemplateBinding Content}"</c> 向
    /// ContentPresenter 的推送发生在属性类处理器<b>之后</b>：若在
    /// <see cref="OnIdentityChanged"/> 里同步测量，Presenter 呈现的还是旧内容，
    /// 量到的是旧宽度，随后新文本被旧宽度裁切（切换语言后已展开按钮不增长）。
    /// 推迟到通知完成后再测量，拿到的才是新内容的自然宽度；
    /// 连续多次变化由 <c>_refreshQueued</c> 合并为一次刷新。
    /// </remarks>
    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _refreshQueued = false;
                Refresh();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 互斥展开资格的自愈同步：悬停 / 键盘聚焦期间向宿主申领单一临时资格，
    /// 条件消失时归还。资格全局唯一——申领成功即迫使其他按钮（含主项）折叠。
    /// </summary>
    private void SyncTemporaryClaim(ButtonCluster? host)
    {
        if (host is null)
        {
            return;
        }

        var wants = host.IsHoverExpansionEnabled &&
            (_pointerOver || IsPointerOver || IsKeyboardFocusWithin);
        var holds = ReferenceEquals(host.TemporaryItem, this);

        if (wants && !holds)
        {
            host.SetTemporaryItem(this);
        }
        else if (!wants && holds)
        {
            host.SetTemporaryItem(null);
        }
    }

    /// <summary>按显示模式、主项指定与互斥的临时展开资格重算本按钮形态与宽度。</summary>
    internal void Refresh()
    {
        var host = _host ??= FindAncestor<ButtonCluster>(this);
        var compact = _compact || (host is not null && host.IsDynamicEffective);
        var isPrimary = host is not null && host.IsPrimaryItem(this);
        var showPrimaryText = host?.ShowPrimaryItemText ?? true;

        // 先同步互斥资格：悬停申领会迫使主项折叠，之后再据此计算展开态。
        SyncTemporaryClaim(host);

        var temporary = host?.TemporaryItem;
        // 互斥展开：持临时资格的按钮（悬停 / 键盘聚焦）展开，其余一律收起——
        // 包括主项；没有临时资格时，仅主项展开。
        var expanded = !compact
            || ReferenceEquals(this, temporary)
            || (isPrimary && temporary is null);

        // 主项允许按规范不显示文本；其余按钮仅在展开态显示文本。
        var showText = !compact || (expanded && !(isPrimary && !showPrimaryText));

        if (!compact)
        {
            if (!_expanded)
            {
                _expanded = true;
                SetClass(ButtonCluster.ItemCollapsedClass, false);
                SetClass(ButtonCluster.ItemNoTextClass, false);
                UpdateIdentityVisuals(showText: true);
                UpdateAutomationName();
            }

            if (_widthManaged)
            {
                // 宽度曾被托管（此前进入过动态样式）：展开回实测自然宽度。
                // 与「自然宽度 → 收缩宽度」互为逆过程，两端都是具体数值，
                // 过渡合法——自适应切回正常样式时播放的正是这段非线性动画；
                // 内容变化（_identityDirty）时同样走这里，防止本地化切换后
                // 文本被旧宽度裁切。
                _identityDirty = false;
                Width = AdaptiveItemMetrics.MeasureNaturalWidth(this);
            }

            return;
        }

        if (expanded == _expanded && !_identityDirty)
        {
            return;
        }

        if (!_widthManaged)
        {
            // 首次进入动态样式：固化实测自然宽度，保证过渡两端都是具体数值。
            _widthManaged = true;
            AdaptiveItemMetrics.SetWidthImmediate(
                this,
                AdaptiveItemMetrics.MeasureNaturalWidth(this));
        }

        _expanded = expanded;
        _identityDirty = false;
        SetClass(ButtonCluster.ItemCollapsedClass, !expanded);
        SetClass(ButtonCluster.ItemNoTextClass, !showText);
        UpdateIdentityVisuals(showText);
        UpdateAutomationName();

        if (expanded)
        {
            Width = AdaptiveItemMetrics.MeasureNaturalWidth(this);
        }
        else
        {
            Width = host?.CollapsedItemWidth ?? DefaultCollapsedWidth;
        }
    }

    private void UpdateIdentityVisuals(bool showText)
    {
        var hasGlyph = !string.IsNullOrWhiteSpace(Glyph);
        var showAvatar = !hasGlyph && !showText && ComputeFallbackText().Length > 0;

        if (_fullIconHost is not null)
        {
            _fullIconHost.IsVisible = hasGlyph;
        }

        if (_compactIconHost is not null)
        {
            _compactIconHost.IsVisible = hasGlyph;
        }

        if (_avatarHost is not null)
        {
            _avatarHost.IsVisible = showAvatar;
            if (showAvatar && _avatarText is not null)
            {
                _avatarText.Text = ComputeFallbackText();
            }
        }
    }

    /// <summary>名称首字符：优先 <see cref="Text"/>，否则取 <see cref="ContentControl.Content"/> 的字符串形式。</summary>
    private string ComputeFallbackText()
    {
        var name = Text;
        if (string.IsNullOrWhiteSpace(name) && Content is string contentText)
        {
            name = contentText;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        return trimmed.Length > 0 ? trimmed[0].ToString() : string.Empty;
    }

    private void UpdateAutomationName()
    {
        if (_expanded)
        {
            return;
        }

        var name = Text ?? (Content as string);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (string.IsNullOrEmpty(AutomationProperties.GetName(this)))
        {
            AutomationProperties.SetName(this, name);
        }

        if (ToolTip.GetTip(this) is null)
        {
            ToolTip.SetTip(this, name);
        }
    }

    private void SetClass(string name, bool value)
    {
        if (value)
        {
            if (!Classes.Contains(name))
            {
                Classes.Add(name);
            }
        }
        else if (Classes.Contains(name))
        {
            Classes.Remove(name);
        }
    }

    private static T? FindAncestor<T>(Visual visual) where T : class
    {
        var parent = visual.GetVisualParent();
        while (parent is not null)
        {
            if (parent is T match)
            {
                return match;
            }

            parent = parent.GetVisualParent();
        }

        return null;
    }
}
