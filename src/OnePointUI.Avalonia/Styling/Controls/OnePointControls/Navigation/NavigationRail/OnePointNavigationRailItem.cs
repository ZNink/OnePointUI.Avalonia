using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.NavigationRail;

/// <summary>
/// 状态点语义色调。所有取值都在样式中映射到 OnePointUI 主题令牌，
/// 调用方只需描述语义，不必关心具体画刷。
/// </summary>
public enum NavigationRailStatusTone
{
    None,
    Accent,
    Success,
    Warning,
    Error
}

/// <summary>
/// 激发象限导航栏中的单个导航项。在保留 Avalonia <see cref="Button" /> 指针、
/// 按下、禁用与键盘激活行为的基础上，追加象限导航所需的选中、徽标、状态点与紧凑态。
/// </summary>
public class OnePointNavigationRailItem : Button
{
    private static readonly string[] TonePseudoClasses =
    [
        ":tone-none",
        ":tone-accent",
        ":tone-success",
        ":tone-warning",
        ":tone-error"
    ];

    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, string>(nameof(Glyph), "\uE80F");

    public static readonly StyledProperty<string> ItemTextProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, string>(nameof(ItemText), "Item");

    /// <summary>
    /// 可选的自定义矢量图标（例如 Avalonia 自带的 <see cref="PathIcon" />）。
    /// 设置后替代 <see cref="Glyph" /> 字形，并自动跟随导航项的前景色。
    /// </summary>
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, object?>(nameof(Icon));

    /// <summary>只读辅助属性：使用自定义矢量图标。</summary>
    public static readonly StyledProperty<bool> HasCustomIconProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(HasCustomIcon));

    /// <summary>只读辅助属性：使用字形图标。</summary>
    public static readonly StyledProperty<bool> HasGlyphIconProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(HasGlyphIcon), true);

    /// <summary>业务路由键。导航栏依据该键维护选中态，不参与任何具体业务判断。</summary>
    public static readonly StyledProperty<string?> NavigationKeyProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, string?>(nameof(NavigationKey));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(IsSelected));

    /// <summary>折叠态。为真时只保留图标，文本与徽标隐藏，并通过 ToolTip 暴露标题。</summary>
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(IsCompact));

    public static readonly StyledProperty<bool> ShowIndicatorProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(ShowIndicator), true);

    public static readonly StyledProperty<string?> BadgeTextProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, string?>(nameof(BadgeText));

    /// <summary>只读辅助属性：<see cref="BadgeText" /> 非空时用于显示数字徽标。</summary>
    public static readonly StyledProperty<bool> HasBadgeProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(HasBadge));

    public static readonly StyledProperty<bool> ShowBadgeDotProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(ShowBadgeDot));

    /// <summary>只读辅助属性：折叠态用于在图标右上角提示“有徽标/新内容”。</summary>
    public static readonly StyledProperty<bool> HasIndicatorBadgeProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(HasIndicatorBadge));

    public static readonly StyledProperty<bool> ShowStatusDotProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, bool>(nameof(ShowStatusDot));

    public static readonly StyledProperty<NavigationRailStatusTone> StatusToneProperty =
        AvaloniaProperty.Register<OnePointNavigationRailItem, NavigationRailStatusTone>(
            nameof(StatusTone),
            NavigationRailStatusTone.Accent);

    public OnePointNavigationRailItem()
    {
        // 导航项在无障碍树中表达为选项卡式导航项，屏幕阅读器可直接朗读标题与徽标。
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.TabItem);
        UpdatePseudoClasses();
        UpdateAccessibility();
    }

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string ItemText
    {
        get => GetValue(ItemTextProperty);
        set => SetValue(ItemTextProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool HasCustomIcon => GetValue(HasCustomIconProperty);

    public bool HasGlyphIcon => GetValue(HasGlyphIconProperty);

    public string? NavigationKey
    {
        get => GetValue(NavigationKeyProperty);
        set => SetValue(NavigationKeyProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool ShowIndicator
    {
        get => GetValue(ShowIndicatorProperty);
        set => SetValue(ShowIndicatorProperty, value);
    }

    public string? BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public bool HasBadge => GetValue(HasBadgeProperty);

    public bool ShowBadgeDot
    {
        get => GetValue(ShowBadgeDotProperty);
        set => SetValue(ShowBadgeDotProperty, value);
    }

    public bool HasIndicatorBadge => GetValue(HasIndicatorBadgeProperty);

    public bool ShowStatusDot
    {
        get => GetValue(ShowStatusDotProperty);
        set => SetValue(ShowStatusDotProperty, value);
    }

    public NavigationRailStatusTone StatusTone
    {
        get => GetValue(StatusToneProperty);
        set => SetValue(StatusToneProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemTextProperty)
        {
            UpdateAccessibility();
        }
        else if (change.Property == IconProperty)
        {
            UpdateIconMode();
        }
        else if (change.Property == ForegroundProperty)
        {
            PropagateIconForeground();
        }
        else if (change.Property == BadgeTextProperty)
        {
            SetValue(HasBadgeProperty, !string.IsNullOrWhiteSpace(BadgeText));
            UpdateIndicatorBadge();
            UpdateAccessibility();
        }
        else if (change.Property == ShowBadgeDotProperty || change.Property == ShowStatusDotProperty)
        {
            UpdateIndicatorBadge();
            UpdateAccessibility();
        }
        else if (change.Property == IsSelectedProperty)
        {
            PseudoClasses.Set(":selected", IsSelected);
        }
        else if (change.Property == IsCompactProperty)
        {
            PseudoClasses.Set(":compact", IsCompact);
        }
        else if (change.Property == StatusToneProperty)
        {
            UpdatePseudoClasses();
        }
    }

    private void UpdateAccessibility()
    {
        AutomationProperties.SetName(this, ItemText);
        var help = HasBadge
            ? $"徽标 {BadgeText}"
            : ShowBadgeDot
                ? "有新内容"
                : null;
        AutomationProperties.SetHelpText(this, help);
    }

    private void UpdateIndicatorBadge() =>
        SetValue(HasIndicatorBadgeProperty, HasBadge || ShowBadgeDot);

    private void UpdateIconMode()
    {
        var hasCustomIcon = Icon is not null;
        SetValue(HasCustomIconProperty, hasCustomIcon);
        SetValue(HasGlyphIconProperty, !hasCustomIcon);
        PropagateIconForeground();
    }

    /// <summary>
    /// 自定义矢量图标（<see cref="PathIcon" />）不会自动继承导航项的前景色，
    /// 这里在选中/悬停/禁用导致的前景色变化时同步，保证图标着色与字形图标一致。
    /// </summary>
    private void PropagateIconForeground()
    {
        if (Icon is PathIcon pathIcon)
        {
            pathIcon.Foreground = Foreground;
        }
    }

    private void UpdatePseudoClasses()
    {
        for (var index = 0; index < TonePseudoClasses.Length; index++)
        {
            PseudoClasses.Set(TonePseudoClasses[index], (int)StatusTone == index);
        }
    }
}
