using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OnePointUI.Avalonia.Styling.Controls.OnePointControls.Adaptive;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

/// <summary>
/// 分段选择按钮（分类选择框）：一组互斥的横向选项，带选中指示器。
/// </summary>
/// <remarks>
/// <para>三套显示样式（<see cref="DisplayMode"/>）：</para>
/// <list type="bullet">
/// <item><b>正常样式</b>（默认）：每一项完整展示图标与文本，行为与旧版完全一致。</item>
/// <item><b>动态样式</b>：默认仅选中项完整展开，其余项收缩为图标（无图标时显示名称首字符头像），
/// 悬停<b>不</b>展开——仅选中项保持展开态；如需「悬停临时展开」旧行为，可显式设置
/// <see cref="IsHoverExpansionEnabled"/> = true，此时与选中展开互斥（任一时刻至多一个条目展开，
/// 指针移出后选中项恢复展开）。</item>
/// <item><b>自适应样式</b>：父级宽度低于 <see cref="AdaptiveThresholdWidth"/> 时自动进入
/// 动态样式，宽度恢复后自动回到正常样式，切换过程播放非线性过渡动画。</item>
/// </list>
/// <para>宽度管理约定：正常样式下条目宽度保持自动布局（<c>Width = NaN</c>）；
/// 进入过动态样式后条目宽度由控件托管（实测自然宽度 ↔ <see cref="CollapsedItemWidth"/>），
/// 内容（本地化文案、Glyph）变化时会自动重新测量，不会裁切文本。</para>
/// </remarks>
public class SegmentedButton : ListBox
{
    /// <summary>动态样式生效时宿主上的伪类，供样式与外部选择器使用。</summary>
    internal const string DynamicPseudoClass = ":dynamic";

    /// <summary>条目处于收缩形态时挂在容器上的类。</summary>
    internal const string ItemCollapsedClass = "collapsed";

    /// <summary>默认收缩宽度：与 44x44 最小交互目标一致。</summary>
    internal const double DefaultCollapsedItemWidth = 44;

    private AdaptiveWidthTracker? _tracker;
    private bool _dynamicActive;

    /// <summary>当前持有「临时展开资格」的条目（悬停 / 键盘聚焦申领，全局唯一）。</summary>
    private SegmentedButtonItem? _temporaryItem;

    /// <summary>是否正在广播刷新：期间禁止条目交叉申领临时资格，防止无限乒乓。</summary>
    private bool _claiming;

    public static readonly StyledProperty<AdaptiveDisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<SegmentedButton, AdaptiveDisplayMode>(nameof(DisplayMode));

    public static readonly StyledProperty<double> AdaptiveThresholdWidthProperty =
        AvaloniaProperty.Register<SegmentedButton, double>(
            nameof(AdaptiveThresholdWidth),
            defaultValue: 640d);

    public static readonly StyledProperty<AdaptiveWidthSource> AdaptiveWidthSourceProperty =
        AvaloniaProperty.Register<SegmentedButton, AdaptiveWidthSource>(
            nameof(AdaptiveWidthSource),
            defaultValue: AdaptiveWidthSource.Parent);

    public static readonly StyledProperty<double> AdaptiveHysteresisWidthProperty =
        AvaloniaProperty.Register<SegmentedButton, double>(
            nameof(AdaptiveHysteresisWidth),
            defaultValue: 8d);

    public static readonly StyledProperty<double> CollapsedItemWidthProperty =
        AvaloniaProperty.Register<SegmentedButton, double>(
            nameof(CollapsedItemWidth),
            defaultValue: DefaultCollapsedItemWidth);

    public static readonly StyledProperty<bool> IsHoverExpansionEnabledProperty =
        AvaloniaProperty.Register<SegmentedButton, bool>(nameof(IsHoverExpansionEnabled), false);

    static SegmentedButton()
    {
        DisplayModeProperty.Changed.AddClassHandler<SegmentedButton>(
            (button, _) => button.OnAdaptiveSettingsChanged());
        AdaptiveThresholdWidthProperty.Changed.AddClassHandler<SegmentedButton>(
            (button, _) => button.OnAdaptiveSettingsChanged());
        AdaptiveHysteresisWidthProperty.Changed.AddClassHandler<SegmentedButton>(
            (button, _) => button.OnAdaptiveSettingsChanged());
        AdaptiveWidthSourceProperty.Changed.AddClassHandler<SegmentedButton>(
            (button, _) => button.OnAdaptiveSettingsChanged());

        // 收缩宽度 / 悬停开关变化必须无条件刷新全部条目：按需广播会在条目
        // 形态未变时短路，导致新宽度、新开关不生效。
        CollapsedItemWidthProperty.Changed.AddClassHandler<SegmentedButton>(
            (button, _) => button.BroadcastRefresh());
        IsHoverExpansionEnabledProperty.Changed.AddClassHandler<SegmentedButton>(
            (button, _) => button.BroadcastRefresh());
    }

    /// <summary>显示样式档位。</summary>
    public AdaptiveDisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>
    /// 自适应阈值：参考宽度低于该值时切换为动态样式。≤ 0 视为关闭自适应
    /// （<see cref="AdaptiveDisplayMode.Adaptive"/> 退化为正常样式）。
    /// </summary>
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

    /// <summary>
    /// 滞回宽度：进入动态样式后，需要参考宽度 ≥ 阈值 + 滞回才切回正常样式，
    /// 避免在阈值边界反复抖动。0 表示不使用滞回。
    /// </summary>
    public double AdaptiveHysteresisWidth
    {
        get => GetValue(AdaptiveHysteresisWidthProperty);
        set => SetValue(AdaptiveHysteresisWidthProperty, value);
    }

    /// <summary>动态样式下未展开条目的宽度（图标 / 首字符头像态）。</summary>
    public double CollapsedItemWidth
    {
        get => GetValue(CollapsedItemWidthProperty);
        set => SetValue(CollapsedItemWidthProperty, value);
    }

    /// <summary>
    /// 动态样式下是否允许悬停 / 键盘聚焦临时展开未选中项。默认 <c>false</c>：
    /// 悬停不展开，仅选中项展开；置为 true 可恢复「悬停临时展开」行为，
    /// 并与选中展开互斥（任一时刻至多一个条目展开）。
    /// </summary>
    public bool IsHoverExpansionEnabled
    {
        get => GetValue(IsHoverExpansionEnabledProperty);
        set => SetValue(IsHoverExpansionEnabledProperty, value);
    }

    /// <summary>动态样式当前是否生效（自适应判定后的实际状态）。</summary>
    internal bool IsDynamicEffective => _dynamicActive;

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
        NeedsContainer<SegmentedButtonItem>(item, out recycleKey);

    protected override Control CreateContainerForItemOverride(
        object? item,
        int index,
        object? recycleKey) =>
        new SegmentedButtonItem();

    private void OnAdaptiveSettingsChanged() => EvaluateAdaptive();

    private void OnTrackedWidthChanged(object? sender, EventArgs e) => EvaluateAdaptive();

    /// <summary>按当前显示模式与参考宽度重算动态样式是否生效。</summary>
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

    /// <summary>
    /// 把「动态样式生效」的状态广播给全部条目容器。后加入的条目会在附加到
    /// 可视树时自行向宿主拉取同一状态，两条路径覆盖同一种情况。
    /// </summary>
    private void BroadcastCompactMode()
    {
        if (ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is SegmentedButtonItem item)
            {
                item.ApplyCompactMode(_dynamicActive);
            }
        }
    }

    /// <summary>
    /// 当前持有「临时展开资格」的条目（悬停 / 键盘聚焦申领，全局唯一）。
    /// 该资格与选中展开互斥：非 null 时连选中项也一并折叠，从而保证动态样式下
    /// 任一时刻至多一个条目处于展开态。
    /// </summary>
    internal SegmentedButtonItem? TemporaryItem => _temporaryItem;

    /// <summary>
    /// 条目申领 / 归还临时展开资格。任一时刻至多一个条目持有：持有时其余条目
    /// （含选中项）全部折叠，杜绝多个条目同时展开或残留展开态。
    /// </summary>
    internal void SetTemporaryItem(SegmentedButtonItem? item)
    {
        if (ReferenceEquals(_temporaryItem, item))
        {
            return;
        }

        // 广播刷新期间只允许归还资格、禁止交叉申领：合成指针事件（指针从一条目
        // 直接进入另一条目而不触发退出）可能让多个条目同时带有悬停标志，交叉
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
            // 展开形态会变的只有两方：旧资格持有者（折叠）与选中项（折叠 / 恢复）。
            // 新资格持有者正处在自己的 Refresh 流程中，会自行完成展开。
            previous?.Refresh();
            RefreshSelectedItem();
        }
        finally
        {
            _claiming = false;
        }
    }

    /// <summary>刷新当前选中项：互斥折叠 / 恢复展开的唯一对象。</summary>
    private void RefreshSelectedItem()
    {
        if (ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is SegmentedButtonItem { IsSelected: true } item)
            {
                item.Refresh();
            }
        }
    }

    /// <summary>无条件刷新全部条目（与 <see cref="BroadcastCompactMode"/> 的按需广播互补）。</summary>
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
                if (child is SegmentedButtonItem item)
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
}

/// <summary>
/// <see cref="SegmentedButton"/> 的条目容器：互斥选项之一。
/// </summary>
/// <remarks>
/// 内容结构为「图标 → 首字符头像 → 文本」一行：正常样式完整展示；
/// 动态样式收起时文本淡出、宽度收缩，无图标的项退化为名称首字符头像。
/// 默认悬停不展开，仅选中项展开；显式开启 <see cref="SegmentedButton.IsHoverExpansionEnabled"/>
/// 后，悬停 / 键盘聚焦临时展开与选中展开互斥——选中项让位折叠，指针移出后
/// 选中项恢复展开（资格由宿主 <c>SetTemporaryItem</c> 统一仲裁）。
/// </remarks>
public class SegmentedButtonItem : ListBoxItem
{
    /// <summary>无图标时动态样式下显示的首字符来源名称。</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SegmentedButtonItem, string?>(nameof(Text));

    /// <summary>Fluent 图标字形（Segoe Fluent Icons）；为空时动态样式下显示首字符头像。</summary>
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<SegmentedButtonItem, string>(nameof(Glyph), string.Empty);

    private const double DefaultCollapsedWidth = SegmentedButton.DefaultCollapsedItemWidth;

    private SegmentedButton? _host;
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

    static SegmentedButtonItem()
    {
        TextProperty.Changed.AddClassHandler<SegmentedButtonItem>(
            (item, _) => item.OnIdentityChanged());
        GlyphProperty.Changed.AddClassHandler<SegmentedButtonItem>(
            (item, _) => item.OnIdentityChanged());
        ContentProperty.Changed.AddClassHandler<SegmentedButtonItem>(
            (item, _) => item.OnIdentityChanged());
        IsSelectedProperty.Changed.AddClassHandler<SegmentedButtonItem>(
            (item, _) => item.Refresh());
    }

    /// <summary>选项名称。无图标时作为动态样式下首字符头像的来源，并用于无障碍名称。</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Fluent 图标字形；为空表示该项没有图标。</summary>
    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>该项宽度是否已由控件托管（进入过动态样式后为 true）。</summary>
    internal bool IsWidthManaged => _widthManaged;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _fullIconHost = e.NameScope.Find<Control>("PART_Icon");
        _compactIconHost = e.NameScope.Find<Control>("PART_CompactIcon");
        _avatarHost = e.NameScope.Find<Control>("PART_Avatar");
        _avatarText = e.NameScope.Find<TextBlock>("PART_AvatarText");
        UpdateIdentityVisuals();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 自行向宿主拉取当前显示模式：后加入的项不需要宿主额外广播。
        _host ??= FindAncestor<SegmentedButton>(this);
        ApplyCompactMode(_host?.IsDynamicEffective == true);

        // 重新挂载时再同步一次：按当前指针 / 焦点状态申领或归还互斥资格。
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // 条目被移除 / 回收，或宿主整体脱离可视树：归还互斥展开资格并清零
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

    /// <summary>内容（名称 / 图标 / 本地化文案）变化：刷新身份视觉并标记宽度待重测。</summary>
    private void OnIdentityChanged()
    {
        _identityDirty = true;
        UpdateIdentityVisuals();
        QueueRefresh();
    }

    /// <summary>
    /// 把宽度刷新排到当前属性通知之后执行。
    /// </summary>
    /// <remarks>
    /// 内容变化时，条目模板里 <c>Content="{TemplateBinding Content}"</c> 向
    /// ContentPresenter 的推送发生在属性类处理器<b>之后</b>：若在
    /// <see cref="OnIdentityChanged"/> 里同步测量，Presenter 呈现的还是旧内容，
    /// 量到的是旧宽度，随后新文本被旧宽度裁切（切换语言后已展开项不增长）。
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
    /// 互斥展开资格的自愈同步（仅在宿主显式开启 <see cref="SegmentedButton.IsHoverExpansionEnabled"/>
    /// 时有意义）：悬停 / 键盘聚焦期间向宿主申领单一临时资格，条件消失时归还。
    /// 资格全局唯一——申领成功即迫使其他条目（含选中项）折叠。
    /// </summary>
    private void SyncTemporaryClaim(SegmentedButton? host)
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

    /// <summary>按显示模式、选中态与互斥的临时展开资格重算本条目形态与宽度。</summary>
    internal void Refresh()
    {
        var host = _host ??= FindAncestor<SegmentedButton>(this);
        var compact = _compact || (host is not null && host.IsDynamicEffective);

        // 先同步互斥资格：悬停申领会迫使选中项折叠，之后再据此计算展开态。
        SyncTemporaryClaim(host);

        var temporary = host?.TemporaryItem;
        // 互斥展开（默认关闭，需宿主显式开启悬停展开）：持临时资格的条目
        // （悬停 / 键盘聚焦）展开，其余一律收起——包括选中项；
        // 没有临时资格时（默认情形），仅选中项展开。
        var expanded = !compact
            || ReferenceEquals(this, temporary)
            || (IsSelected && temporary is null);

        if (!compact)
        {
            // 正常样式：展开形态。
            if (!_expanded)
            {
                _expanded = true;
                SetCollapsedClass(false);
                UpdateIdentityVisuals();
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
            // 首次进入动态样式：把自动宽度固化为实测自然宽度（静默写入），
            // 保证后续 collapsed ↔ expanded 是两端都是具体数值的合法过渡。
            _widthManaged = true;
            AdaptiveItemMetrics.SetWidthImmediate(
                this,
                AdaptiveItemMetrics.MeasureNaturalWidth(this));
        }

        _expanded = expanded;
        _identityDirty = false;
        SetCollapsedClass(!expanded);
        UpdateIdentityVisuals();
        UpdateAutomationName();

        if (expanded)
        {
            // 过渡：收缩宽度 → 实测自然宽度。
            Width = AdaptiveItemMetrics.MeasureNaturalWidth(this);
        }
        else
        {
            // 过渡：自然宽度 → 收缩宽度。
            Width = host?.CollapsedItemWidth ?? DefaultCollapsedWidth;
        }
    }

    private void UpdateIdentityVisuals()
    {
        var hasGlyph = !string.IsNullOrWhiteSpace(Glyph);
        var showText = !_compact ? true : _expanded;
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

        // 收起态只剩图标 / 首字符：补齐无障碍名称与悬停提示（不覆盖调用方已设置的值）。
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

    private void SetCollapsedClass(bool value)
    {
        if (value)
        {
            if (!Classes.Contains(SegmentedButton.ItemCollapsedClass))
            {
                Classes.Add(SegmentedButton.ItemCollapsedClass);
            }
        }
        else if (Classes.Contains(SegmentedButton.ItemCollapsedClass))
        {
            Classes.Remove(SegmentedButton.ItemCollapsedClass);
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
