using Avalonia;
using Avalonia.Controls;
using OnePointUI.Avalonia.Styling.Icons;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.LeftSelectBar;

public class LeftSelectBarItem : ListBoxItem
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<LeftSelectBarItem, string>(nameof(Glyph), "");


    public static readonly StyledProperty<string> ItemTextProperty =
        AvaloniaProperty.Register<LeftSelectBarItem, string>(nameof(ItemText), "Item");

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

    /// <summary>
    /// 线稿图标，设置后优先于 <see cref="Glyph" />。用于字库里没有对应字形的产品级功能。
    /// </summary>
    public static readonly StyledProperty<ToolIconKind?> IconKindProperty =
        AvaloniaProperty.Register<LeftSelectBarItem, ToolIconKind?>(nameof(IconKind));

    public ToolIconKind? IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }
}