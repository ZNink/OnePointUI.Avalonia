using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

/// <summary>搜索框尺寸档位。</summary>
public enum SearchBoxSize
{
    /// <summary>36px 高，用于工具栏等紧凑场景。</summary>
    Small,

    /// <summary>44px 高，用于页面主工具条。</summary>
    Large
}

/// <summary>
/// 可展开搜索框：收起时只显示一枚搜索图标按钮，指针悬停或焦点进入时展开成
/// 完整输入框；指针离开且焦点不在内部时重新收起。
/// </summary>
/// <remarks>
/// 展开条件（满足其一即展开）：指针在组件内、输入焦点在组件内。
/// 收起条件（必须同时满足）：指针已移出组件、焦点不在组件内。
/// 展开后的输入框在模板里复用 OnePointUI 的 <see cref="TextBox"/> 样式，
/// 因此外观与项目里现有的搜索框完全一致。
/// </remarks>
public class SearchBox : TemplatedControl
{
    private const string ExpandedPseudoClass = ":expanded";
    private const string HasTextPseudoClass = ":has-text";
    private const string LargeClass = "large";
    private const string SmallClass = "small";

    private Grid? _root;
    private TextBox? _input;
    private Button? _expandButton;
    private bool _syncingText;
    private bool _suppressTextChanged;
    private string? _lastNotifiedText;
    private DispatcherTimer? _inputHideTimer;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchBox, string?>(
            nameof(Text),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchBox, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<SearchBoxSize> SizeProperty =
        AvaloniaProperty.Register<SearchBox, SearchBoxSize>(nameof(Size), SearchBoxSize.Small);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SearchBox, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<double> ExpandedWidthProperty =
        AvaloniaProperty.Register<SearchBox, double>(nameof(ExpandedWidth), 220d);

    public static readonly StyledProperty<double> CollapsedWidthProperty =
        AvaloniaProperty.Register<SearchBox, double>(nameof(CollapsedWidth), 36d);

    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(Glyph), "\uE721");

    /// <summary>
    /// 与 <see cref="TextBox.TextChanged"/> 同签名的路由事件：内部输入框的内容
    /// 变化会以组件为源重新抛出，页面上的 <c>TextChanged="..."</c> 用法不变。
    /// </summary>
    public static readonly RoutedEvent<TextChangedEventArgs> TextChangedEvent =
        RoutedEvent.Register<SearchBox, TextChangedEventArgs>(
            nameof(TextChanged),
            RoutingStrategies.Bubble);

    /// <summary>输入内容变化。</summary>
    public event EventHandler<TextChangedEventArgs>? TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    static SearchBox()
    {
        TextProperty.Changed.AddClassHandler<SearchBox>((box, _) => box.SyncInputFromText());
        IsExpandedProperty.Changed.AddClassHandler<SearchBox>((box, _) => box.UpdateExpansionState());
        SizeProperty.Changed.AddClassHandler<SearchBox>((box, _) => box.UpdateSizeClass());
        ExpandedWidthProperty.Changed.AddClassHandler<SearchBox>((box, _) => box.UpdateRootWidth());
        CollapsedWidthProperty.Changed.AddClassHandler<SearchBox>((box, _) => box.UpdateRootWidth());
    }

    /// <summary>输入内容，双向绑定。</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>空内容时的占位文本。</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>尺寸档位，决定高度、圆角、字号与展开宽度。</summary>
    public SearchBoxSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>当前是否处于展开状态。</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>展开后的宽度，可在具体页面覆盖。</summary>
    public double ExpandedWidth
    {
        get => GetValue(ExpandedWidthProperty);
        set => SetValue(ExpandedWidthProperty, value);
    }

    /// <summary>收起后的宽度，等于图标按钮的边长。</summary>
    public double CollapsedWidth
    {
        get => GetValue(CollapsedWidthProperty);
        set => SetValue(CollapsedWidthProperty, value);
    }

    /// <summary>收起状态下显示的图标字形。</summary>
    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>展开并把输入焦点交给内部输入框。</summary>
    public void FocusInput()
    {
        Expand();
        _input?.Focus();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        DetachParts();
        base.OnApplyTemplate(e);

        _root = e.NameScope.Find<Grid>("PART_Root");
        _input = e.NameScope.Find<TextBox>("PART_Input");
        _expandButton = e.NameScope.Find<Button>("PART_SearchButton");

        if (_expandButton is not null)
        {
            _expandButton.Click += OnExpandButtonClick;
            // 收起态只有图标按钮可见，它是组件对辅助技术的唯一入口。
            _expandButton.Bind(
                AutomationProperties.NameProperty,
                this.GetObservable(AutomationProperties.NameProperty));
        }

        if (_input is not null)
        {
            _input.TextChanged += OnInputTextChanged;
            // 模板首次同步 Text 只是把控件初始化到页面的初始状态，
            // 不能当成一次用户输入通知页面（否则会在构造期触发一次搜索）。
            _suppressTextChanged = true;
            try
            {
                _input.Text = Text ?? string.Empty;
            }
            finally
            {
                _suppressTextChanged = false;
            }

            _lastNotifiedText = _input.Text ?? string.Empty;

            _input.Bind(
                AutomationProperties.NameProperty,
                this.GetObservable(AutomationProperties.NameProperty));
            _input.IsVisible = IsExpanded;
        }

        UpdateSizeClass();
        UpdateExpansionState();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Expand();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // IsPointerOver 在本轮指针处理结束后才落定，延后一拍再判定。
        Dispatcher.UIThread.Post(CollapseIfIdle, DispatcherPriority.Input);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Expand();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        // 焦点在组件内部移动（图标按钮 → 输入框）也会走到这里，延后一拍再判定。
        Dispatcher.UIThread.Post(CollapseIfIdle, DispatcherPriority.Input);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CancelInputHide();
    }

    private void OnExpandButtonClick(object? sender, RoutedEventArgs e)
    {
        Expand();
        _input?.Focus();
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        // 内部事件先标记处理：组件自己再抛一次，保证页面上的处理器只收到一次。
        e.Handled = true;

        if (_suppressTextChanged)
        {
            return;
        }

        // TextBox 在模板装载时也会抛一次 TextChanged（即使内容没有变化），
        // 这类噪声不能当成用户输入传给页面。
        var current = _input?.Text ?? string.Empty;
        var changed = !string.Equals(_lastNotifiedText, current, StringComparison.Ordinal);
        _lastNotifiedText = current;

        if (!_syncingText)
        {
            _syncingText = true;
            try
            {
                SetCurrentValue(TextProperty, _input?.Text);
            }
            finally
            {
                _syncingText = false;
            }
        }

        if (!changed)
        {
            return;
        }

        RaiseEvent(new TextChangedEventArgs(TextChangedEvent));
    }

    private void SyncInputFromText()
    {
        PseudoClasses.Set(HasTextPseudoClass, !string.IsNullOrEmpty(Text));

        if (_syncingText || _input is null)
        {
            return;
        }

        var text = Text ?? string.Empty;
        if (string.Equals(_input.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _syncingText = true;
        try
        {
            _input.Text = text;
        }
        finally
        {
            _syncingText = false;
        }
    }

    private void UpdateSizeClass()
    {
        SetClass(LargeClass, Size == SearchBoxSize.Large);
        SetClass(SmallClass, Size == SearchBoxSize.Small);
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

    private void UpdateExpansionState()
    {
        PseudoClasses.Set(ExpandedPseudoClass, IsExpanded);
        UpdateRootWidth();

        if (_input is null || _expandButton is null)
        {
            return;
        }

        _expandButton.IsVisible = !IsExpanded;
        if (IsExpanded)
        {
            CancelInputHide();
            _input.IsVisible = true;
        }
        else
        {
            // 收起动画期间保留输入框，避免内容在收缩过程中突然消失。
            ScheduleInputHide();
        }
    }

    private void UpdateRootWidth()
    {
        if (_root is null)
        {
            return;
        }

        _root.Width = IsExpanded ? ExpandedWidth : CollapsedWidth;
    }

    private void Expand()
    {
        if (!IsExpanded)
        {
            SetCurrentValue(IsExpandedProperty, true);
        }
    }

    private void CollapseIfIdle()
    {
        if (!IsExpanded || IsPointerOver || IsKeyboardFocusWithin)
        {
            return;
        }

        SetCurrentValue(IsExpandedProperty, false);
    }

    private void ScheduleInputHide()
    {
        if (_input is null)
        {
            return;
        }

        _inputHideTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(200),
            DispatcherPriority.Background,
            OnInputHideTimerTick);

        _inputHideTimer.Stop();
        _inputHideTimer.Start();
    }

    private void OnInputHideTimerTick(object? sender, EventArgs e)
    {
        CancelInputHide();

        if (!IsExpanded && _input is not null)
        {
            _input.IsVisible = false;
        }
    }

    private void CancelInputHide()
    {
        _inputHideTimer?.Stop();
    }

    private void DetachParts()
    {
        if (_expandButton is not null)
        {
            _expandButton.Click -= OnExpandButtonClick;
        }

        if (_input is not null)
        {
            _input.TextChanged -= OnInputTextChanged;
        }
    }
}
