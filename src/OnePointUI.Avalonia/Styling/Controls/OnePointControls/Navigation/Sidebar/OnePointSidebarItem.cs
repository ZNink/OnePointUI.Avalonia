using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.Sidebar;

/// <summary>
/// 状态点语义色调。所有取值都在样式中映射到 OnePointUI 主题令牌，
/// 调用方只需描述语义，不必关心具体画刷。
/// </summary>
public enum SidebarStatusTone
{
    None,
    Accent,
    Success,
    Warning,
    Error
}

/// <summary>
/// 侧边栏按钮。保留 Avalonia <see cref="Button" /> 的指针、按下、禁用与键盘激活行为，
/// 追加侧边栏需要的选中态、徽标、状态点与自定义矢量图标。
/// </summary>
/// <remarks>
/// 单一形态：没有紧凑/折叠态，文本始终可见，因此无障碍名称与视觉文本始终一致。
/// </remarks>
public class OnePointSidebarItem : Button
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
        AvaloniaProperty.Register<OnePointSidebarItem, string>(nameof(Glyph), "\uE80F");

    public static readonly StyledProperty<string> ItemTextProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, string>(nameof(ItemText), "Item");

    /// <summary>
    /// 可选的自定义矢量图标（例如 Avalonia 自带的 <see cref="PathIcon" />）。
    /// 设置后替代 <see cref="Glyph" /> 字形，并自动跟随按钮的前景色。
    /// </summary>
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, object?>(nameof(Icon));

    /// <summary>只读辅助属性：使用自定义矢量图标。</summary>
    public static readonly StyledProperty<bool> HasCustomIconProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, bool>(nameof(HasCustomIcon));

    /// <summary>只读辅助属性：使用字形图标。</summary>
    public static readonly StyledProperty<bool> HasGlyphIconProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, bool>(nameof(HasGlyphIcon), true);

    /// <summary>业务路由键。侧边栏依据该键维护选中态，不参与任何具体业务判断。</summary>
    public static readonly StyledProperty<string?> NavigationKeyProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, string?>(nameof(NavigationKey));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, bool>(nameof(IsSelected));

    public static readonly StyledProperty<string?> BadgeTextProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, string?>(nameof(BadgeText));

    /// <summary>只读辅助属性：<see cref="BadgeText" /> 非空时用于显示数字徽标。</summary>
    public static readonly StyledProperty<bool> HasBadgeProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, bool>(nameof(HasBadge));

    public static readonly StyledProperty<bool> ShowBadgeDotProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, bool>(nameof(ShowBadgeDot));

    public static readonly StyledProperty<bool> ShowStatusDotProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, bool>(nameof(ShowStatusDot));

    public static readonly StyledProperty<SidebarStatusTone> StatusToneProperty =
        AvaloniaProperty.Register<OnePointSidebarItem, SidebarStatusTone>(
            nameof(StatusTone),
            SidebarStatusTone.Accent);

    public OnePointSidebarItem()
    {
        // 侧边栏项在无障碍树中表达为选项卡式导航项，屏幕阅读器可直接朗读标题与徽标。
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

    public bool ShowStatusDot
    {
        get => GetValue(ShowStatusDotProperty);
        set => SetValue(ShowStatusDotProperty, value);
    }

    public SidebarStatusTone StatusTone
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
            UpdateAccessibility();
        }
        else if (change.Property == ShowBadgeDotProperty || change.Property == ShowStatusDotProperty)
        {
            UpdateAccessibility();
        }
        else if (change.Property == IsSelectedProperty)
        {
            PseudoClasses.Set(":selected", IsSelected);
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

    private void UpdateIconMode()
    {
        var hasCustomIcon = Icon is not null;
        SetValue(HasCustomIconProperty, hasCustomIcon);
        SetValue(HasGlyphIconProperty, !hasCustomIcon);
        PropagateIconForeground();
    }

    /// <summary>
    /// 自定义矢量图标（<see cref="PathIcon" />）不会自动继承按钮的前景色，
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
