using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

/// <summary>按键录制完成时抛出的路由事件参数。</summary>
public class KeyCapturedEventArgs : RoutedEventArgs
{
    public KeyCapturedEventArgs(RoutedEvent routedEvent, Key key)
        : base(routedEvent) => Key = key;

    /// <summary>录制到的按键。</summary>
    public Key Key { get; }
}

/// <summary>按下不接受的按键时抛出的路由事件参数。</summary>
public class KeyRejectedEventArgs : RoutedEventArgs
{
    public KeyRejectedEventArgs(RoutedEvent routedEvent, Key key)
        : base(routedEvent) => Key = key;

    /// <summary>被拒绝的按键。</summary>
    public Key Key { get; }
}

/// <summary>
/// 通用按键输入框：点击（或聚焦后按 Enter/Space）进入录制态，接下来的那个键
/// 就是绑定值；<see cref="CancelKey"/>（默认 ESC）取消。
/// </summary>
/// <remarks>
/// <para><b>为什么不用 TextBox 派生。</b>只读 TextBox 会先把指针事件标记为已处理
/// （文本选择/聚焦都是它自己做的），挂在外层的 <c>PointerPressed</c> 处理器根本收不到，
/// 于是「点一下开始录制」时灵时不灵。本控件自己是 <see cref="TemplatedControl"/>，
/// 不承载任何文本编辑，录制入口走 <see cref="OnPointerPressed"/>，不依赖处理器时序。</para>
///
/// <para><b>为什么录制期挂在 TopLevel 上。</b>录制态把处理器挂到
/// <see cref="TopLevel"/> 的隧道阶段并且 <c>handledEventsToo: true</c>：页面的快捷键、
/// 外层遮罩、ScrollViewer 的按键处理都抢在前面也不影响——录制期间任何位置的按键
/// 都归本控件解释。副作用是录制态下 Tab / Enter / Alt 不再触发各自的默认行为，
/// 这正是按键录入需要的（否则这几个键永远绑不上）。</para>
///
/// <para><b>取消条件。</b>按 <see cref="CancelKey"/>、点击控件外部、失焦、被禁用、
/// 从可视树摘除，任一发生即取消录制，<see cref="Value"/> 保持原值不变。</para>
///
/// <para>控件本身不含任何文案：<see cref="RecordingText"/> / <see cref="RejectedText"/> /
/// <see cref="PlaceholderText"/> 由宿主提供，便于本地化。</para>
/// </remarks>
public class KeyCaptureBox : TemplatedControl
{
    private const string RecordingPseudoClass = ":recording";
    private const string RejectedPseudoClass = ":rejected";
    private const string EmptyPseudoClass = ":empty";

    /// <summary>录制态的展示文本来源之一，见 <see cref="DisplayText"/>。</summary>
    public static readonly StyledProperty<Key?> ValueProperty =
        AvaloniaProperty.Register<KeyCaptureBox, Key?>(
            nameof(Value),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary><see cref="Value"/> 的展示名，为空时回落到 <see cref="Key"/> 的枚举名。</summary>
    public static readonly StyledProperty<string?> ValueTextProperty =
        AvaloniaProperty.Register<KeyCaptureBox, string?>(nameof(ValueText));

    /// <summary>未绑定任何按键时的占位文本。</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<KeyCaptureBox, string?>(nameof(PlaceholderText));

    /// <summary>录制态显示在框内的提示文本。</summary>
    public static readonly StyledProperty<string?> RecordingTextProperty =
        AvaloniaProperty.Register<KeyCaptureBox, string?>(nameof(RecordingText));

    /// <summary>按到不接受的键时显示在框内的提示文本。</summary>
    public static readonly StyledProperty<string?> RejectedTextProperty =
        AvaloniaProperty.Register<KeyCaptureBox, string?>(nameof(RejectedText));

    /// <summary>允许录制的按键；为空表示不限制。</summary>
    public static readonly StyledProperty<IReadOnlyList<Key>?> AllowedKeysProperty =
        AvaloniaProperty.Register<KeyCaptureBox, IReadOnlyList<Key>?>(nameof(AllowedKeys));

    /// <summary>取消键，默认 ESC；设为 <c>null</c> 表示录制态下 ESC 也可被绑定。</summary>
    public static readonly StyledProperty<Key?> CancelKeyProperty =
        AvaloniaProperty.Register<KeyCaptureBox, Key?>(nameof(CancelKey), Key.Escape);

    /// <summary>是否正在录制。写入即开始/取消录制。</summary>
    public static readonly StyledProperty<bool> IsRecordingProperty =
        AvaloniaProperty.Register<KeyCaptureBox, bool>(nameof(IsRecording));

    /// <summary>框内文字的对齐方式。</summary>
    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<KeyCaptureBox, TextAlignment>(
            nameof(TextAlignment),
            TextAlignment.Center);

    /// <summary>框内当前显示的文本，只读。模板用 <c>TemplateBinding</c> 取它。</summary>
    public static readonly DirectProperty<KeyCaptureBox, string> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<KeyCaptureBox, string>(
            nameof(DisplayText),
            box => box._displayText);

    /// <summary>录制到按键。</summary>
    public static readonly RoutedEvent<KeyCapturedEventArgs> KeyCapturedEvent =
        RoutedEvent.Register<KeyCaptureBox, KeyCapturedEventArgs>(
            nameof(KeyCaptured),
            RoutingStrategies.Bubble);

    /// <summary>按到不接受的按键。</summary>
    public static readonly RoutedEvent<KeyRejectedEventArgs> KeyRejectedEvent =
        RoutedEvent.Register<KeyCaptureBox, KeyRejectedEventArgs>(
            nameof(KeyRejected),
            RoutingStrategies.Bubble);

    /// <summary>录制被取消（ESC / 失焦 / 点击外部）。</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CaptureCanceledEvent =
        RoutedEvent.Register<KeyCaptureBox, RoutedEventArgs>(
            nameof(CaptureCanceled),
            RoutingStrategies.Bubble);

    private string _displayText = string.Empty;
    private TopLevel? _captureRoot;
    private bool _showRejected;

    static KeyCaptureBox()
    {
        ValueProperty.Changed.AddClassHandler<KeyCaptureBox>((box, _) => box.UpdateDisplayText());
        ValueTextProperty.Changed.AddClassHandler<KeyCaptureBox>((box, _) => box.UpdateDisplayText());
        PlaceholderTextProperty.Changed.AddClassHandler<KeyCaptureBox>((box, _) => box.UpdateDisplayText());
        RecordingTextProperty.Changed.AddClassHandler<KeyCaptureBox>((box, _) => box.UpdateDisplayText());
        RejectedTextProperty.Changed.AddClassHandler<KeyCaptureBox>((box, _) => box.UpdateDisplayText());
        IsRecordingProperty.Changed.AddClassHandler<KeyCaptureBox>((box, e) =>
            box.OnIsRecordingChanged(e.GetNewValue<bool>()));
        IsEnabledProperty.Changed.AddClassHandler<KeyCaptureBox>((box, e) =>
        {
            if (!e.GetNewValue<bool>())
            {
                box.CancelCapture();
            }
        });
    }

    /// <summary>当前绑定的按键。</summary>
    public Key? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>绑定按键的展示名（例如 "Numpad4"）。为空时用枚举名兜底。</summary>
    public string? ValueText
    {
        get => GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    /// <summary>未绑定时的占位文本。</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>录制态提示文本。</summary>
    public string? RecordingText
    {
        get => GetValue(RecordingTextProperty);
        set => SetValue(RecordingTextProperty, value);
    }

    /// <summary>按到不接受的键时的提示文本。</summary>
    public string? RejectedText
    {
        get => GetValue(RejectedTextProperty);
        set => SetValue(RejectedTextProperty, value);
    }

    /// <summary>允许录制的按键白名单；为空表示不限制。</summary>
    public IReadOnlyList<Key>? AllowedKeys
    {
        get => GetValue(AllowedKeysProperty);
        set => SetValue(AllowedKeysProperty, value);
    }

    /// <summary>取消键。</summary>
    public Key? CancelKey
    {
        get => GetValue(CancelKeyProperty);
        set => SetValue(CancelKeyProperty, value);
    }

    /// <summary>是否处于录制态。</summary>
    public bool IsRecording
    {
        get => GetValue(IsRecordingProperty);
        set => SetValue(IsRecordingProperty, value);
    }

    /// <summary>框内文字对齐方式。</summary>
    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>框内当前显示的文本（录制提示 / 拒绝提示 / 绑定名 / 占位）。</summary>
    public string DisplayText
    {
        get => _displayText;
        private set => SetAndRaise(DisplayTextProperty, ref _displayText, value);
    }

    /// <summary>录制到按键。</summary>
    public event EventHandler<KeyCapturedEventArgs>? KeyCaptured
    {
        add => AddHandler(KeyCapturedEvent, value);
        remove => RemoveHandler(KeyCapturedEvent, value);
    }

    /// <summary>按到不接受的按键。</summary>
    public event EventHandler<KeyRejectedEventArgs>? KeyRejected
    {
        add => AddHandler(KeyRejectedEvent, value);
        remove => RemoveHandler(KeyRejectedEvent, value);
    }

    /// <summary>录制被取消。</summary>
    public event EventHandler<RoutedEventArgs>? CaptureCanceled
    {
        add => AddHandler(CaptureCanceledEvent, value);
        remove => RemoveHandler(CaptureCanceledEvent, value);
    }

    /// <summary>开始录制。已在录制态或被禁用时无操作。</summary>
    public void BeginCapture()
    {
        if (IsRecording || !IsEffectivelyEnabled)
        {
            return;
        }

        _showRejected = false;
        SetCurrentValue(IsRecordingProperty, true);
    }

    /// <summary>取消录制，绑定值保持不变。</summary>
    public void CancelCapture()
    {
        if (!IsRecording)
        {
            return;
        }

        SetCurrentValue(IsRecordingProperty, false);
        RaiseEvent(new RoutedEventArgs(CaptureCanceledEvent));
    }

    /// <summary>按键是否可被录制。宿主可重写以做更细的判定。</summary>
    protected virtual bool IsKeyAllowed(Key key)
    {
        var allowed = AllowedKeys;
        if (allowed is null || allowed.Count == 0)
        {
            return true;
        }

        for (var index = 0; index < allowed.Count; index++)
        {
            if (allowed[index] == key)
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        // 自己吃掉这次按下：不让外层的列表/滚动容器把它当成拖拽或选中。
        e.Handled = true;
        Focus();
        BeginCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsRecording)
        {
            // TopLevel 上没挂到（例如宿主在弹出层里）时的兜底路径。
            HandleCaptureKey(e);
            return;
        }

        if (!IsEffectivelyEnabled || (e.Key != Key.Enter && e.Key != Key.Space))
        {
            return;
        }

        // 键盘用户不需要指针：聚焦后 Enter/Space 即开始录制。
        e.Handled = true;
        BeginCapture();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        CancelCapture();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateDisplayText();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // 页面被换掉时不留悬空处理器：TopLevel 比控件活得久。
        StopCapture();
    }

    private void OnIsRecordingChanged(bool recording)
    {
        if (recording)
        {
            _showRejected = false;
            StartCapture();
            Focus();
        }
        else
        {
            StopCapture();
        }

        UpdateDisplayText();
    }

    private void StartCapture()
    {
        var root = TopLevel.GetTopLevel(this);
        if (root is null)
        {
            return;
        }

        StopCapture();
        _captureRoot = root;

        // 隧道 + handledEventsToo：页面快捷键、外壳遮罩、TextBox 之类的
        // 先行处理都拦不住这次录制。
        root.AddHandler(KeyDownEvent, OnCaptureRootKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(KeyUpEvent, OnCaptureRootKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(PointerPressedEvent, OnCaptureRootPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void StopCapture()
    {
        var root = _captureRoot;
        _captureRoot = null;
        if (root is null)
        {
            return;
        }

        root.RemoveHandler(KeyDownEvent, OnCaptureRootKeyDown);
        root.RemoveHandler(KeyUpEvent, OnCaptureRootKeyUp);
        root.RemoveHandler(PointerPressedEvent, OnCaptureRootPointerPressed);
    }

    private void OnCaptureRootKeyDown(object? sender, KeyEventArgs e) => HandleCaptureKey(e);

    private void OnCaptureRootKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsRecording)
        {
            // 松键也要吞：否则外层会把这次按下当成完整的一次快捷键/导航。
            e.Handled = true;
        }
    }

    private void OnCaptureRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRecording)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (new Rect(Bounds.Size).Contains(point))
        {
            return;
        }

        // 点别处就是放弃这次录制；不标记 Handled，让点击照常落到目标控件上。
        CancelCapture();
    }

    private void HandleCaptureKey(KeyEventArgs e)
    {
        if (!IsRecording)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key;
        if (CancelKey is { } cancelKey && key == cancelKey)
        {
            CancelCapture();
            return;
        }

        if (!IsKeyAllowed(key))
        {
            _showRejected = true;
            UpdateDisplayText();
            RaiseEvent(new KeyRejectedEventArgs(KeyRejectedEvent, key));
            return;
        }

        _showRejected = false;
        SetCurrentValue(ValueProperty, (Key?)key);
        // 先退出录制态再抛事件：宿主在处理器里读到的状态已经是最终状态。
        SetCurrentValue(IsRecordingProperty, false);
        RaiseEvent(new KeyCapturedEventArgs(KeyCapturedEvent, key));
    }

    private void UpdateDisplayText()
    {
        string text;
        if (IsRecording)
        {
            text = _showRejected
                ? RejectedText ?? RecordingText ?? string.Empty
                : RecordingText ?? string.Empty;
        }
        else if (!string.IsNullOrEmpty(ValueText))
        {
            text = ValueText;
        }
        else if (Value is { } key)
        {
            text = key.ToString();
        }
        else
        {
            text = PlaceholderText ?? string.Empty;
        }

        DisplayText = text;
        PseudoClasses.Set(RecordingPseudoClass, IsRecording);
        PseudoClasses.Set(RejectedPseudoClass, IsRecording && _showRejected);
        // 宿主只给展示名、不写 Value 时也算"已绑定"，否则会被弱化成占位样式。
        PseudoClasses.Set(
            EmptyPseudoClass,
            !IsRecording && Value is null && string.IsNullOrEmpty(ValueText));
    }
}
