using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using OnePointUI.Avalonia.Styling.Icons;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

public class SettingCard : ContentControl
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<SettingCard, string>(nameof(Glyph), "");

    public static readonly StyledProperty<object> HeaderProperty =
        AvaloniaProperty.Register<SettingCard, object>(nameof(Header));

    public static readonly StyledProperty<object> DescriptionProperty =
        AvaloniaProperty.Register<SettingCard, object>(nameof(Description));

    public static readonly StyledProperty<bool> IsClickableProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(IsClickable));

    public static readonly StyledProperty<bool> IsShowActionIconProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(IsShowActionIcon), true);

    public static readonly StyledProperty<bool> IsFontIconProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(IsFontIcon), true);

    public static readonly StyledProperty<bool> IsNotFontIconProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(IsNotFontIcon));

    public static readonly StyledProperty<IImage?> ImageIconProperty =
        AvaloniaProperty.Register<SettingCard, IImage?>(nameof(ImageIcon));

    /// <summary>
    /// 线稿图标（<see cref="ToolIconKind" />）。设置后压过 <see cref="Glyph" /> 与 <see cref="ImageIcon" />，
    /// 三种图标互斥，模板只看 <see cref="ShowGeometryIcon" /> / <see cref="ShowFontIcon" /> / <see cref="ShowImageIcon" />。
    /// </summary>
    public static readonly StyledProperty<ToolIconKind?> IconKindProperty =
        AvaloniaProperty.Register<SettingCard, ToolIconKind?>(nameof(IconKind));

    /// <summary>图标区当前渲染字形图标。</summary>
    public static readonly StyledProperty<bool> ShowFontIconProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(ShowFontIcon), true);

    /// <summary>图标区当前渲染位图图标。</summary>
    public static readonly StyledProperty<bool> ShowImageIconProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(ShowImageIcon));

    /// <summary>图标区当前渲染线稿图标。</summary>
    public static readonly StyledProperty<bool> ShowGeometryIconProperty =
        AvaloniaProperty.Register<SettingCard, bool>(nameof(ShowGeometryIcon));

    public event EventHandler<RoutedEventArgs>? Click;

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsClickable
    {
        get => GetValue(IsClickableProperty);
        set => SetValue(IsClickableProperty, value);
    }

    public bool IsShowActionIcon
    {
        get => GetValue(IsShowActionIconProperty);
        set => SetValue(IsShowActionIconProperty, value);
    }

    public bool IsFontIcon
    {
        get => GetValue(IsFontIconProperty);
        set
        {
            SetValue(IsFontIconProperty, value);
            SetValue(IsNotFontIconProperty, !value);
        }
    }

    public bool IsNotFontIcon
    {
        get => GetValue(IsNotFontIconProperty);
        set => SetValue(IsNotFontIconProperty, value);
    }

    public IImage? ImageIcon
    {
        get => GetValue(ImageIconProperty);
        set => SetValue(ImageIconProperty, value);
    }

    public ToolIconKind? IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public bool ShowFontIcon
    {
        get => GetValue(ShowFontIconProperty);
        set => SetValue(ShowFontIconProperty, value);
    }

    public bool ShowImageIcon
    {
        get => GetValue(ShowImageIconProperty);
        set => SetValue(ShowImageIconProperty, value);
    }

    public bool ShowGeometryIcon
    {
        get => GetValue(ShowGeometryIconProperty);
        set => SetValue(ShowGeometryIconProperty, value);
    }

    private Border? _rootBorder;
    private bool _isPointerPressed;
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var rootBorder = e.NameScope.Find<Border>("PART_Root");
        if (rootBorder != null)
        {
            _rootBorder = rootBorder;
            _rootBorder.PointerPressed += RootBorder_PointerPressed;
            _rootBorder.PointerReleased += RootBorder_PointerReleased;
            _rootBorder.PointerCaptureLost += RootBorder_PointerCaptureLost;
        }

        var contentPresenter = e.NameScope.Find<ContentControl>("ActionContentControl");
        if (contentPresenter != null)
        {
            contentPresenter.PointerPressed += (sender, args) => { args.Handled = true; };
            contentPresenter.PointerReleased += (sender, args) => { args.Handled = true; };
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsFontIconProperty) SetValue(IsNotFontIconProperty, !(bool)change.NewValue!);

        if (change.Property == IsFontIconProperty
            || change.Property == IsNotFontIconProperty
            || change.Property == IconKindProperty
            || change.Property == ImageIconProperty)
        {
            SyncIconBranches();
        }
    }

    /// <summary>
    /// 三种图标互斥、线稿优先：改任一来源后立即收敛到恰好一个可见分支，
    /// 模板不再自己做组合判断。
    /// </summary>
    private void SyncIconBranches()
    {
        var geometry = IconKind is not null;
        SetValue(ShowGeometryIconProperty, geometry);
        SetValue(ShowFontIconProperty, !geometry && IsFontIcon);
        SetValue(ShowImageIconProperty, !geometry && IsNotFontIcon);
    }
    private void RootBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _isPointerPressed = true;
        e.Pointer.Capture(_rootBorder);
        PseudoClasses.Set(":pressed", true);
    }

    private void RootBorder_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerPressed)
            return;

        _isPointerPressed = false;

        var pos = e.GetPosition(this);
        var bounds = new Rect(Bounds.Size);
        if (!bounds.Contains(pos))
            return;

        if (!IsClickable)
            return;

        Click?.Invoke(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private void RootBorder_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPointerPressed = false;
        PseudoClasses.Set(":pressed", false);
    }
}
