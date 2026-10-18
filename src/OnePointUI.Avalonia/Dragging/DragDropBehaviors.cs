using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Dragging;

/// <summary>
/// 拖拽相关的附加属性：把任意控件标记为拖拽源或投放目标。
/// </summary>
/// <remarks>
/// <para>投放目标只负责"被识别"和"接住投放"：悬停时由 <see cref="DragSession"/>
/// 自动在目标上画半透明占位符并加上 <c>:dragtarget</c> 伪类，页面可以据此做高亮。</para>
/// <para>需要自定义拖拽时机（例如先做业务校验、或区分左右键）时，不必挂
/// <see cref="IsDragSource"/>，直接在代码里调用 <see cref="DragSession.Begin"/> 即可，
/// 两者共用同一套视觉与投放识别。</para>
/// </remarks>
public static class DragDropBehaviors
{
    private const string DefaultFormat = "onepoint";

    private static readonly ConditionalWeakTable<Control, SourceState> Sources = new();

    /// <summary>标记控件可以接住投放。</summary>
    public static readonly AttachedProperty<bool> IsDropTargetProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsDropTarget", typeof(DragDropBehaviors));

    /// <summary>投放目标接受的格式；留空表示接受所有格式。</summary>
    public static readonly AttachedProperty<string?> DropFormatProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("DropFormat", typeof(DragDropBehaviors));

    /// <summary>占位符圆角，默认 8；胶囊形卡片设成高度的一半。</summary>
    public static readonly AttachedProperty<double> PlaceholderCornerRadiusProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "PlaceholderCornerRadius",
            typeof(DragDropBehaviors),
            8d);

    /// <summary>投放事件（冒泡）。</summary>
    public static readonly RoutedEvent<DragDropEventArgs> DropEvent =
        RoutedEvent.Register<DragDropEventArgs>(
            "Drop",
            RoutingStrategies.Bubble,
            typeof(DragDropBehaviors));

    /// <summary>标记控件可以发起拖拽。</summary>
    public static readonly AttachedProperty<bool> IsDragSourceProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsDragSource", typeof(DragDropBehaviors));

    /// <summary>拖拽数据；不设则取控件的 <c>DataContext</c>。</summary>
    public static readonly AttachedProperty<object?> DragPayloadProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("DragPayload", typeof(DragDropBehaviors));

    /// <summary>拖拽格式，默认 <c>onepoint</c>。</summary>
    public static readonly AttachedProperty<string> DragFormatProperty =
        AvaloniaProperty.RegisterAttached<Control, string>(
            "DragFormat",
            typeof(DragDropBehaviors),
            DefaultFormat);

    /// <summary>拖拽效果，默认 <see cref="DragDropEffects.Copy"/>。</summary>
    public static readonly AttachedProperty<DragDropEffects> DragEffectProperty =
        AvaloniaProperty.RegisterAttached<Control, DragDropEffects>(
            "DragEffect",
            typeof(DragDropBehaviors),
            DragDropEffects.Copy);

    static DragDropBehaviors()
    {
        IsDropTargetProperty.Changed.AddClassHandler<Control>(OnIsDropTargetChanged);
        IsDragSourceProperty.Changed.AddClassHandler<Control>(OnIsDragSourceChanged);
    }

    /// <summary>读取 <see cref="IsDropTargetProperty"/>。</summary>
    public static bool GetIsDropTarget(Control control) => control.GetValue(IsDropTargetProperty);

    /// <summary>设置 <see cref="IsDropTargetProperty"/>。</summary>
    public static void SetIsDropTarget(Control control, bool value) =>
        control.SetValue(IsDropTargetProperty, value);

    /// <summary>读取 <see cref="DropFormatProperty"/>。</summary>
    public static string? GetDropFormat(Control control) => control.GetValue(DropFormatProperty);

    /// <summary>设置 <see cref="DropFormatProperty"/>。</summary>
    public static void SetDropFormat(Control control, string? value) =>
        control.SetValue(DropFormatProperty, value);

    /// <summary>读取 <see cref="PlaceholderCornerRadiusProperty"/>。</summary>
    public static double GetPlaceholderCornerRadius(Control control) =>
        control.GetValue(PlaceholderCornerRadiusProperty);

    /// <summary>设置 <see cref="PlaceholderCornerRadiusProperty"/>。</summary>
    public static void SetPlaceholderCornerRadius(Control control, double value) =>
        control.SetValue(PlaceholderCornerRadiusProperty, value);

    /// <summary>订阅投放事件。</summary>
    public static void AddDropHandler(Control control, EventHandler<DragDropEventArgs> handler) =>
        control.AddHandler(DropEvent, handler);

    /// <summary>取消订阅投放事件。</summary>
    public static void RemoveDropHandler(Control control, EventHandler<DragDropEventArgs> handler) =>
        control.RemoveHandler(DropEvent, handler);

    /// <summary>读取 <see cref="IsDragSourceProperty"/>。</summary>
    public static bool GetIsDragSource(Control control) => control.GetValue(IsDragSourceProperty);

    /// <summary>设置 <see cref="IsDragSourceProperty"/>。</summary>
    public static void SetIsDragSource(Control control, bool value) =>
        control.SetValue(IsDragSourceProperty, value);

    /// <summary>读取 <see cref="DragPayloadProperty"/>。</summary>
    public static object? GetDragPayload(Control control) => control.GetValue(DragPayloadProperty);

    /// <summary>设置 <see cref="DragPayloadProperty"/>。</summary>
    public static void SetDragPayload(Control control, object? value) =>
        control.SetValue(DragPayloadProperty, value);

    /// <summary>读取 <see cref="DragFormatProperty"/>。</summary>
    public static string GetDragFormat(Control control) => control.GetValue(DragFormatProperty);

    /// <summary>设置 <see cref="DragFormatProperty"/>。</summary>
    public static void SetDragFormat(Control control, string value) =>
        control.SetValue(DragFormatProperty, value);

    /// <summary>读取 <see cref="DragEffectProperty"/>。</summary>
    public static DragDropEffects GetDragEffect(Control control) => control.GetValue(DragEffectProperty);

    /// <summary>设置 <see cref="DragEffectProperty"/>。</summary>
    public static void SetDragEffect(Control control, DragDropEffects value) =>
        control.SetValue(DragEffectProperty, value);

    private static void OnIsDropTargetChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // 目标不需要挂任何处理器：识别与占位符都由 DragSession 统一处理。
        // 这里只负责在关闭标记时清掉可能残留的高亮伪类。
        if (e.GetNewValue<bool>())
        {
            return;
        }

        ((IPseudoClasses)control.Classes).Set(":dragtarget", false);
    }

    private static void OnIsDragSourceChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            var state = Sources.GetOrCreateValue(control);
            state.Attach(control);
        }
        else if (Sources.TryGetValue(control, out var state))
        {
            state.Detach();
        }
    }

    /// <summary>拖拽源的手势状态：按下后超过阈值才真正开始拖，避免吃掉点击。</summary>
    private sealed class SourceState
    {
        private Control? _control;
        private IPointer? _pointer;
        private Point _start;

        public void Attach(Control control)
        {
            if (ReferenceEquals(_control, control))
            {
                return;
            }

            Detach();
            _control = control;
            control.AddHandler(
                InputElement.PointerPressedEvent,
                OnPointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            control.AddHandler(
                InputElement.PointerMovedEvent,
                OnPointerMoved,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            control.AddHandler(
                InputElement.PointerReleasedEvent,
                OnPointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            control.AddHandler(
                InputElement.PointerCaptureLostEvent,
                OnPointerCaptureLost,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        public void Detach()
        {
            if (_control is not { } control)
            {
                return;
            }

            control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            control.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
            _control = null;
            _pointer = null;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_control is not { } control
                || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed
                || IsInteractiveSource(e.Source))
            {
                return;
            }

            _pointer = e.Pointer;
            _start = e.GetPosition(control);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_control is not { } control
                || _pointer is null
                || e.Pointer.Id != _pointer.Id)
            {
                return;
            }

            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                _pointer = null;
                return;
            }

            var current = e.GetPosition(control);
            if (Math.Abs(current.X - _start.X) < 5 && Math.Abs(current.Y - _start.Y) < 5)
            {
                return;
            }

            var payload = GetDragPayload(control) ?? control.DataContext;
            DragSession.Begin(
                control,
                new DragPayload(GetDragFormat(control), payload, GetDragEffect(control)),
                e);
            _pointer = null;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pointer is not null && e.Pointer.Id == _pointer.Id)
            {
                _pointer = null;
            }
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _pointer = null;

        private static bool IsInteractiveSource(object? source) =>
            source is Visual visual
            && visual.GetSelfAndVisualAncestors()
                .Any(candidate => candidate is Button or ToggleButton or TextBox or ScrollBar or Slider);
    }
}
