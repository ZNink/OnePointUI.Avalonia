using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives; // 引入 TemplatedControl 相关的命名空间
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var rootBorder = e.NameScope.Find<Border>("PART_Root");
        if (rootBorder != null)
        {
            rootBorder.PointerPressed += RootBorder_PointerPressed;
            rootBorder.PointerReleased += RootBorder_PointerReleased;
            rootBorder.PointerCaptureLost += RootBorder_PointerCaptureLost;
        }

        var contentPresenter = e.NameScope.Find<ContentControl>("ActionContentControl");
        if (contentPresenter != null)
        {
            contentPresenter.PointerPressed += (sender, args) =>
            {
                args.Handled = true;
            };
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsFontIconProperty) SetValue(IsNotFontIconProperty, !(bool)change.NewValue!);
    }
    
    private void RootBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 触发 Click 事件，这样外部就可以像订阅 Button.Click 一样订阅 SettingCard.Click
        Click?.Invoke(this, new RoutedEventArgs());
        PseudoClasses.Set(":pressed", true);
    }

    private void RootBorder_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PseudoClasses.Set(":pressed", false);
    }

    private void RootBorder_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        PseudoClasses.Set(":pressed", false);
    }
}
