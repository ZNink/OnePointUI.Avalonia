using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.NavigationRail;

/// <summary>
/// 激发象限导航栏中的一个象限分组：小标题 + 可折叠的子项。
/// 折叠动画只重排分组自身的子树，不触发整树重绘。
/// </summary>
public class OnePointNavigationRailGroup : ItemsControl
{
    internal static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(200);

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<OnePointNavigationRailGroup, object?>(nameof(Header));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<OnePointNavigationRailGroup, bool>(nameof(IsExpanded), true);

    public static readonly StyledProperty<bool> IsCollapsibleProperty =
        AvaloniaProperty.Register<OnePointNavigationRailGroup, bool>(nameof(IsCollapsible), true);

    /// <summary>导航栏折叠态。为真时隐藏分组标题，只保留子项。</summary>
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<OnePointNavigationRailGroup, bool>(nameof(IsCompact));

    /// <summary>只读辅助属性：展开且非紧凑态时显示分组标题。</summary>
    public static readonly StyledProperty<bool> IsHeaderVisibleProperty =
        AvaloniaProperty.Register<OnePointNavigationRailGroup, bool>(nameof(IsHeaderVisible), true);

    private Border? _itemsHost;
    private IDisposable? _settleTimer;

    public OnePointNavigationRailGroup()
    {
        AddHandler(Button.ClickEvent, OnHeaderClick, RoutingStrategies.Bubble);
        UpdatePseudoClasses();
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public bool IsCollapsible
    {
        get => GetValue(IsCollapsibleProperty);
        set => SetValue(IsCollapsibleProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool IsHeaderVisible => GetValue(IsHeaderVisibleProperty);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _itemsHost = e.NameScope.Find<Border>("PART_ItemsHost");
        ApplyExpansionState(animate: false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty)
        {
            UpdatePseudoClasses();
            ApplyExpansionState(animate: true);
        }
        else if (change.Property == IsCompactProperty)
        {
            UpdatePseudoClasses();
            SetValue(IsHeaderVisibleProperty, !IsCompact);
            UpdateAccessibility();
        }
        else if (change.Property == HeaderProperty)
        {
            UpdateAccessibility();
        }
    }

    private void OnHeaderClick(object? sender, RoutedEventArgs e)
    {
        // 只有分组自身的标题按钮参与折叠，子项点击继续向上冒泡给导航栏。
        if (e.Source is not Button button
            || !string.Equals(button.Name, "PART_Header", StringComparison.Ordinal))
        {
            return;
        }

        if (IsCollapsible)
        {
            IsExpanded = !IsExpanded;
        }

        e.Handled = true;
    }

    private void UpdateAccessibility() =>
        AutomationProperties.SetName(
            this,
            IsCompact ? string.Empty : Header?.ToString() ?? string.Empty);

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":expanded", IsExpanded);
        PseudoClasses.Set(":collapsed", !IsExpanded);
        PseudoClasses.Set(":compact", IsCompact);
    }

    /// <summary>
    /// 展开/折叠动画：内容高度在 200ms 内从 0 过渡到实际测量高度（或反向），
    /// 动画结束后释放为自动高度，避免后续子项增删被固定高度锁死。
    /// </summary>
    private void ApplyExpansionState(bool animate)
    {
        if (_itemsHost is null)
        {
            return;
        }

        _settleTimer?.Dispose();
        _settleTimer = null;

        if (IsExpanded)
        {
            _itemsHost.IsVisible = true;
            _itemsHost.Opacity = 1;
            var target = MeasureItemsHeight();
            if (!animate || target <= 0)
            {
                _itemsHost.Height = double.NaN;
                return;
            }

            _itemsHost.Height = 0;
            var host = _itemsHost;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (IsExpanded && ReferenceEquals(host, _itemsHost))
                    {
                        host.Height = target;
                    }
                },
                DispatcherPriority.Render);
            _settleTimer = DispatcherTimer.RunOnce(
                () =>
                {
                    if (IsExpanded && ReferenceEquals(host, _itemsHost))
                    {
                        host.Height = double.NaN;
                    }
                },
                ExpandDuration + TimeSpan.FromMilliseconds(40));
            return;
        }

        if (!animate)
        {
            _itemsHost.IsVisible = false;
            _itemsHost.Height = 0;
            _itemsHost.Opacity = 0;
            return;
        }

        var hostCollapsing = _itemsHost;
        hostCollapsing.Height = 0;
        hostCollapsing.Opacity = 0;
        _settleTimer = DispatcherTimer.RunOnce(
            () =>
            {
                if (!IsExpanded && ReferenceEquals(hostCollapsing, _itemsHost))
                {
                    hostCollapsing.IsVisible = false;
                }
            },
            ExpandDuration + TimeSpan.FromMilliseconds(40));
    }

    private double MeasureItemsHeight()
    {
        if (_itemsHost is null)
        {
            return 0;
        }

        var available = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        _itemsHost.Measure(new Size(available, double.PositiveInfinity));
        var measured = _itemsHost.DesiredSize.Height;
        return measured > 0 && !double.IsInfinity(measured) ? measured : 0;
    }
}
