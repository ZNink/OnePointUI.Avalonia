using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Dragging;

/// <summary>
/// 一次应用内拖拽会话：跟手预览 + 半透明占位符 + 投放目标识别。
/// </summary>
/// <remarks>
/// <para><b>为什么不用平台自带的 <c>DragDrop.DoDragDropAsync</c>。</b>平台拖拽由系统绘制
/// 拖影，应用既拿不到指针的连续位置，也没法在目标处画出自己的占位符，更做不了跟手缓动。
/// 这里改成自己做：指针在顶层捕获，拖影和占位符都放在 <see cref="OverlayLayer"/>
/// 里（不会被滚动容器裁剪），每一帧把拖影按指数趋近推向指针，
/// 因此移动是"连贯且非线性"的，而不是硬跟。</para>
/// <para><b>行为契约。</b>指针按下阈值由调用方决定（列表要区分点击与拖拽，
/// 卡片可以按下即拖）；本类只负责从 <see cref="Begin"/> 开始到指针释放 / Esc /
/// 捕获丢失为止的完整生命周期，并在结束时把源控件恢复原样。</para>
/// </remarks>
public sealed class DragSession
{
    /// <summary>拖影追赶指针的时间常数（秒）。越大越"绵"，越小越跟手。</summary>
    private const double FollowTimeConstantSeconds = 0.05;

    /// <summary>单帧最大时间跨度，避免卡顿后一次跳很远。</summary>
    private const double MaxStepSeconds = 0.05;

    private const double SnapDistance = 0.2;

    /// <summary>窗口模板可以显式声明的拖拽覆盖层名字。</summary>
    internal const string DragOverlayHostName = "PART_DragOverlay";

    private static DragSession? _current;

    private readonly TopLevel _topLevel;
    private readonly Control _source;
    private readonly DragPayload _payload;
    private readonly bool _ownsPlaceholder;
    private readonly Panel? _host;
    private readonly Canvas _layer;
    private readonly Border _ghost;
    private readonly Rectangle _placeholder;
    private readonly IPointer _pointer;
    private readonly Vector _grabOffset;
    private readonly double _sourceOpacity;
    private readonly bool _hideSource;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer? _timer;

    private TimeSpan _lastStep;
    private Point _pointerPosition;
    private Point _ghostPosition;
    private bool _ghostPlaced;
    private Control? _hoverTarget;
    private bool _finished;

    /// <summary>指针移动（每帧都会触发）。</summary>
    public event EventHandler<DragMovedEventArgs>? Moved;

    /// <summary>拖拽结束（投放或取消）。</summary>
    public event EventHandler<DragCompletedEventArgs>? Completed;

    /// <summary>当前进行中的会话。</summary>
    public static DragSession? Current => _current;

    /// <summary>是否正在拖拽。</summary>
    public static bool IsDragging => _current is not null;

    private DragSession(
        TopLevel topLevel,
        Control source,
        DragPayload payload,
        PointerEventArgs trigger,
        bool hideSource,
        bool ownsPlaceholder)
    {
        _topLevel = topLevel;
        _source = source;
        _payload = payload;
        _hideSource = hideSource;
        _ownsPlaceholder = ownsPlaceholder;
        _pointer = trigger.Pointer;
        _host = ResolveOverlayHost(topLevel, source);

        var bounds = source.Bounds;
        var preview = DragPreviewFactory.Create(source, out var cornerRadius);

        _ghost = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            CornerRadius = cornerRadius,
            ClipToBounds = true,
            Background = Brushes.Transparent,
            Opacity = 0.94,
            Child = preview,
            IsHitTestVisible = false,
            BoxShadow = new BoxShadows(
                new BoxShadow
                {
                    OffsetX = 0,
                    OffsetY = 10,
                    Blur = 26,
                    Color = Color.Parse("#44000000")
                }),
            RenderTransform = new ScaleTransform(1.015, 1.015),
            RenderTransformOrigin = RelativePoint.Center
        };

        var accent = ResolveAccentColor();
        _placeholder = new Rectangle
        {
            Name = "PART_DragPlaceholder",
            IsVisible = false,
            Width = 0,
            Height = 0,
            IsHitTestVisible = false,
            RadiusX = 8,
            RadiusY = 8,
            Fill = new SolidColorBrush(accent, 0.14),
            Stroke = new SolidColorBrush(accent, 0.62),
            StrokeThickness = 1.5,
            StrokeDashArray = new AvaloniaList<double>(5, 3),
            Transitions = new Transitions
            {
                // 占位符换位也用缓动，直接跳会显得很生硬。
                new ThicknessTransition
                {
                    Property = Layoutable.MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(190),
                    Easing = new CubicEaseOut()
                },
                new DoubleTransition
                {
                    Property = Layoutable.WidthProperty,
                    Duration = TimeSpan.FromMilliseconds(190),
                    Easing = new CubicEaseOut()
                },
                new DoubleTransition
                {
                    Property = Layoutable.HeightProperty,
                    Duration = TimeSpan.FromMilliseconds(190),
                    Easing = new CubicEaseOut()
                }
            }
        };

        _layer = new Canvas
        {
            // 宿主可能不是画布（自绘窗口的根面板），显式贴左上角并铺满客户区，
            // 保证 Canvas.Left/Top 与顶层坐标一一对应。
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Width = topLevel.ClientSize.Width,
            Height = topLevel.ClientSize.Height
        };
        _layer.Children.Add(_placeholder);
        _layer.Children.Add(_ghost);
        if (_host is { } host)
        {
            if (host is Grid grid)
            {
                // 回退到某个 Grid 根面板时，默认会落进第 0 行/列被压扁，
                // 显式跨满所有行列才能铺满客户区。
                Grid.SetRow(_layer, 0);
                Grid.SetColumn(_layer, 0);
                Grid.SetRowSpan(_layer, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(_layer, Math.Max(1, grid.ColumnDefinitions.Count));
            }

            host.Children.Add(_layer);
        }

        _pointerPosition = trigger.GetPosition(topLevel);
        var sourceOrigin = source.TranslatePoint(default, topLevel) ?? default;
        _grabOffset = _pointerPosition - sourceOrigin;

        _sourceOpacity = source.Opacity;
        if (_hideSource)
        {
            source.Opacity = 0;
        }
        else
        {
            source.Opacity = Math.Min(source.Opacity, 0.45);
        }

        _pointer.Capture(topLevel);
        topLevel.AddHandler(
            InputElement.PointerMovedEvent,
            OnPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        topLevel.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        topLevel.AddHandler(
            InputElement.PointerCaptureLostEvent,
            OnPointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        topLevel.AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        // 无头会话里 DispatcherTimer 不会触发，逐帧推进另外暴露给测试驱动。
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnFrame);

        Step();

        _current = this;
        _timer.Start();
    }

    /// <summary>拖拽数据。</summary>
    public DragPayload Payload => _payload;

    /// <summary>拖拽源控件。</summary>
    public Control Source => _source;

    /// <summary>发起拖拽的指针。</summary>
    public IPointer Pointer => _pointer;

    /// <summary>指针当前位置（顶层坐标）。</summary>
    public Point PointerPosition => _pointerPosition;

    /// <summary>拖影当前尺寸。</summary>
    public Size GhostSize => new(_ghost.Width, _ghost.Height);

    /// <summary>拖影当前位置（顶层坐标），用于诊断跟随是否平滑。</summary>
    public Point GhostPosition => _ghostPosition;

    /// <summary>当前悬停的投放目标。</summary>
    public Control? HoverTarget => _hoverTarget;

    /// <summary>占位符最近一次被摆放到的矩形（顶层坐标）；未显示时为 <c>null</c>。</summary>
    /// <remarks>
    /// 视觉上的位移/缩放是带缓动的，属性值则立即生效，测试与业务判断都以这里为准。
    /// </remarks>
    public Rect? PlaceholderBounds { get; private set; }

    /// <summary>
    /// 找出承载拖影/占位符的覆盖层宿主。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不直接用 <see cref="OverlayLayer.GetOverlayLayer"/>。</b>它是按
    /// <c>VisualLayerManager</c> 找层的，而这层只在默认 <see cref="Window"/> 模板里存在。
    /// 自绘窗口通常没有：Pulsar 的 <c>PulsarWindowsWindow</c> 甚至把 <c>Window.Content</c>
    /// 整个换成了自己的 shell，于是 <see cref="OverlayLayer.GetOverlayLayer"/> 返回 <c>null</c>——
    /// 真机上第一次拖动就抛异常；而单元测试用的是默认模板的 <see cref="Window"/>，
    /// 层一直拿得到，所以测试全绿也挡不住这个崩溃。</para>
    /// <para>因此按下面的顺序退让：窗口里显式声明的 <c>PART_DragOverlay</c> →
    /// 标准 <see cref="OverlayLayer"/> → 铺满客户区的根面板 → 都没有就返回 <c>null</c>
    /// （会话降级为"没有拖影"，落点计算和投放事件仍然照常工作，绝不让拖拽炸掉整个窗口）。</para>
    /// </remarks>
    private static Panel? ResolveOverlayHost(TopLevel topLevel, Control source)
    {
        var clientSize = topLevel.ClientSize;

        if (topLevel.GetVisualDescendants()
                .OfType<Panel>()
                .FirstOrDefault(panel => panel.Name == DragOverlayHostName) is { } declared)
        {
            return declared;
        }

        if (OverlayLayer.GetOverlayLayer(source) is { } standard)
        {
            return standard;
        }

        // 自绘窗口：挑一个尺寸与客户区一致的面板，把覆盖层叠在它最后一个子级上。
        foreach (var panel in topLevel.GetVisualDescendants().OfType<Panel>())
        {
            if (panel.Bounds.Width >= clientSize.Width - 1 && panel.Bounds.Height >= clientSize.Height - 1)
            {
                return panel;
            }
        }

        return null;
    }

    /// <summary>开始一次拖拽。</summary>
    /// <param name="source">拖拽源控件，必须已在可视树中。</param>
    /// <param name="payload">拖拽数据。</param>
    /// <param name="trigger">触发拖拽的指针事件（用于捕获指针和取初始位置）。</param>
    /// <param name="hideSource">是否把源控件完全隐藏（列表重排用：让位给占位符）。</param>
    /// <param name="ownsPlaceholder">占位符是否由本会话按投放目标自动摆放；<c>false</c> 时由调用方调用 <see cref="ShowPlaceholder"/>。</param>
    public static DragSession Begin(
        Control source,
        DragPayload payload,
        PointerEventArgs trigger,
        bool hideSource = false,
        bool ownsPlaceholder = true)
    {
        if (TopLevel.GetTopLevel(source) is not { } topLevel)
        {
            throw new InvalidOperationException("拖拽源不在可视树中。");
        }

        _current?.Cancel();
        return new DragSession(topLevel, source, payload, trigger, hideSource, ownsPlaceholder);
    }

    /// <summary>把顶层坐标转换成指定控件的坐标。</summary>
    public Point GetPosition(Visual relativeTo) =>
        _topLevel.TranslatePoint(_pointerPosition, relativeTo) ?? _pointerPosition;

    /// <summary>
    /// 在顶层坐标里摆放半透明占位符。
    /// </summary>
    /// <param name="rect">目标矩形（顶层坐标）。</param>
    /// <param name="cornerRadius">圆角，跟随目标卡片的形状。</param>
    public void ShowPlaceholder(Rect rect, double cornerRadius = 8)
    {
        if (_finished)
        {
            return;
        }

        var origin = _topLevel.TranslatePoint(rect.TopLeft, _layer) ?? rect.TopLeft;
        PlaceholderBounds = rect;
        _placeholder.RadiusX = cornerRadius;
        _placeholder.RadiusY = cornerRadius;
        _placeholder.Width = Math.Max(0, rect.Width);
        _placeholder.Height = Math.Max(0, rect.Height);
        _placeholder.Margin = new Thickness(origin.X, origin.Y, 0, 0);
        _placeholder.IsVisible = true;
    }

    /// <summary>隐藏占位符。</summary>
    public void HidePlaceholder()
    {
        PlaceholderBounds = null;
        if (!_finished)
        {
            _placeholder.IsVisible = false;
        }
    }

    /// <summary>主动结束（等价于取消）。</summary>
    public void Cancel() => Finish(cancelled: true);

    /// <summary>
    /// 手动推进一帧动画。
    /// </summary>
    /// <remarks>运行时由内部定时器调用；无头测试里定时器不触发，用它逐帧驱动。</remarks>
    public void AdvanceAnimation(TimeSpan elapsed)
    {
        if (_finished)
        {
            return;
        }

        Step(Math.Max(0, elapsed.TotalSeconds));
    }

    private void OnFrame(object? sender, EventArgs e) => Step();

    private void Step() => Step(-1);

    private void Step(double overrideSeconds)
    {
        if (_finished)
        {
            return;
        }

        _layer.Width = _topLevel.ClientSize.Width;
        _layer.Height = _topLevel.ClientSize.Height;

        var now = _clock.Elapsed;
        var seconds = overrideSeconds >= 0 ? overrideSeconds : (now - _lastStep).TotalSeconds;
        _lastStep = now;
        seconds = Math.Min(seconds, MaxStepSeconds);

        var target = new Point(
            _pointerPosition.X - _grabOffset.X,
            _pointerPosition.Y - _grabOffset.Y);

        if (!_ghostPlaced)
        {
            _ghostPosition = target;
            _ghostPlaced = true;
        }
        else
        {
            _ghostPosition = new Point(
                Approach(_ghostPosition.X, target.X, seconds),
                Approach(_ghostPosition.Y, target.Y, seconds));
        }

        Canvas.SetLeft(_ghost, _ghostPosition.X);
        Canvas.SetTop(_ghost, _ghostPosition.Y);
    }

    /// <summary>指数趋近：每帧只走剩余距离的一部分，天然非线性、不过冲。</summary>
    private static double Approach(double current, double target, double seconds)
    {
        var distance = target - current;
        if (seconds <= 0 || Math.Abs(distance) <= SnapDistance)
        {
            return target;
        }

        var factor = 1 - Math.Exp(-seconds / FollowTimeConstantSeconds);
        return current + distance * factor;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_finished || e.Pointer.Id != _pointer.Id)
        {
            return;
        }

        _pointerPosition = e.GetPosition(_topLevel);
        Step();

        if (_ownsPlaceholder)
        {
            UpdateHoverTarget();
        }

        Moved?.Invoke(this, new DragMovedEventArgs(_pointerPosition));
        e.Handled = true;
    }

    private void UpdateHoverTarget()
    {
        var target = FindDropTarget(_pointerPosition);
        if (ReferenceEquals(target, _hoverTarget))
        {
            return;
        }

        SetTargetState(_hoverTarget, false);
        _hoverTarget = target;
        SetTargetState(_hoverTarget, true);

        if (_hoverTarget is null)
        {
            HidePlaceholder();
            return;
        }

        var origin = _hoverTarget.TranslatePoint(default, _topLevel) ?? default;
        ShowPlaceholder(
            new Rect(origin, _hoverTarget.Bounds.Size),
            DragDropBehaviors.GetPlaceholderCornerRadius(_hoverTarget));
    }

    private static void SetTargetState(Control? control, bool isTarget)
    {
        if (control is null)
        {
            return;
        }

        ((IPseudoClasses)control.Classes).Set(":dragtarget", isTarget);
    }

    private Control? FindDropTarget(Point point)
    {
        if (_topLevel.InputHitTest(point) is not Visual hit)
        {
            return null;
        }

        foreach (var visual in hit.GetSelfAndVisualAncestors())
        {
            if (visual is not Control control || !DragDropBehaviors.GetIsDropTarget(control))
            {
                continue;
            }

            var format = DragDropBehaviors.GetDropFormat(control);
            if (string.IsNullOrEmpty(format)
                || string.Equals(format, _payload.Format, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_finished || e.Pointer.Id != _pointer.Id)
        {
            return;
        }

        _pointerPosition = e.GetPosition(_topLevel);
        Step();

        var target = _ownsPlaceholder ? FindDropTarget(_pointerPosition) : null;
        var handled = false;

        if (target is not null)
        {
            var args = new DragDropEventArgs(_payload, _source);
            target.RaiseEvent(args);
            handled = args.Handled;
        }

        Finish(cancelled: false, handled, target);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_finished)
        {
            Finish(cancelled: true);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_finished && e.Key == Key.Escape)
        {
            Finish(cancelled: true);
            e.Handled = true;
        }
    }

    private void Finish(bool cancelled, bool handled = false, Control? target = null)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _timer?.Stop();

        _topLevel.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _topLevel.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _topLevel.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
        _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

        SetTargetState(_hoverTarget, false);
        _hoverTarget = null;

        _source.Opacity = _sourceOpacity;

        if (_layer.Parent is Panel host)
        {
            host.Children.Remove(_layer);
        }

        // 拖影若来自 DragPreviewFactory 的位图快照，会话结束即释放。
        if (_ghost.Child is Image { Source: IDisposable disposable })
        {
            _ghost.Child = null;
            disposable.Dispose();
        }

        _pointer.Capture(null);

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        Completed?.Invoke(
            this,
            new DragCompletedEventArgs(!cancelled, handled, target));
    }

    private static Color ResolveAccentColor()
    {
        if (Application.Current is { } application
            && application.TryGetResource(
                "AccentBackgroundBrush",
                application.ActualThemeVariant,
                out var value)
            && value is ISolidColorBrush brush)
        {
            return brush.Color;
        }

        return Color.Parse("#4C8DFF");
    }
}
