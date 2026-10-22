using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using OnePointUI.Avalonia.Styling.Icons;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

/// <summary>
/// 线稿工具图标：按 <see cref="Kind" /> 取一套 24x24 几何，用统一描边渲染。
/// </summary>
/// <remarks>
/// 与 <see cref="FontIcon" /> 的分工：字形图标走 Segoe Fluent Icons，只能填色、尺寸跟随字号；
/// 这里走自绘几何，笔画粗细可调、尺寸跟随 <see cref="Size" />，用于图标库里没有对应字形的产品级功能。
/// 几何数据集中在 <c>ToolIconGeometries</c>，改图形请改 <c>Assets/Icons/Tools/*.svg</c> 后重新生成。
/// </remarks>
public class ToolIcon : TemplatedControl
{
    /// <summary>图标种类；为 <c>null</c> 时不绘制任何图形。</summary>
    public static readonly StyledProperty<ToolIconKind?> KindProperty =
        AvaloniaProperty.Register<ToolIcon, ToolIconKind?>(nameof(Kind));

    /// <summary>图标外框边长（px）。描边按 24 网格等比缩放，因此小尺寸下线条会同步变细。</summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ToolIcon, double>(nameof(Size), 20d);

    /// <summary>描边宽度，以 24 网格为单位；默认 1.8。</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ToolIcon, double>(nameof(StrokeThickness), 1.8d);

    /// <summary>描边与点缀色。默认由样式指向 <c>PrimaryForegroundBrush</c>。</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ToolIcon, IBrush?>(nameof(Stroke));

    private ShapePath? _strokePath;
    private ShapePath? _fillPath;

    static ToolIcon()
    {
        AffectsMeasure<ToolIcon>(SizeProperty);
    }

    public ToolIconKind? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _strokePath = e.NameScope.Find<ShapePath>("PART_Stroke");
        _fillPath = e.NameScope.Find<ShapePath>("PART_Fill");
        UpdateGeometry();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            UpdateGeometry();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        var size = Math.Max(0d, Size);
        return new Size(size, size);
    }

    private void UpdateGeometry()
    {
        var geometry = Kind is { } kind ? ToolIconGeometries.Get(kind) : null;
        if (_strokePath is not null)
        {
            _strokePath.Data = geometry?.Stroke;
        }

        if (_fillPath is not null)
        {
            _fillPath.Data = geometry?.Fill;
        }
    }
}
