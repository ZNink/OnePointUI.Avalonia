using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Branding;

/// <summary>
/// Pulsar 品牌标记（星球本体 + 前后半星环 + 底部装饰横线）。
/// </summary>
/// <remarks>
/// <para>图形来自 <c>Assets/Icons/PulsarBrand.svg</c> 的路径数据，用描边绘制而不是位图，
/// 因此任意尺寸都清晰，并随主题前景色自动换色。viewBox 0 0 200 200，
/// 控件按短边等比缩放并居中。</para>
/// <para>组件库内需要画品牌标识的地方（侧边栏品牌区、底栏品牌区、启动页）都用它，
/// 不要再各自贴图标字形 + 两行文字。</para>
/// </remarks>
public class PulsarBrandMark : Control
{
    private const double ViewBoxSize = 200d;
    private const double MarkCenter = 100d;
    private const double RingStrokeThickness = 12d;
    private const double PlanetStrokeThickness = 8d;
    private const double RingRotationDegrees = -30d;

    // ① 后半球星环：拆成两段，只保留星球圆外的可见部分。
    private static readonly Geometry BackRingStart =
        StreamGeometry.Parse("M 25 100 A 75 25 0 0 0 60.31 121.21");

    private static readonly Geometry BackRingEnd =
        StreamGeometry.Parse("M 139.69 121.21 A 75 25 0 0 0 175 100");

    // ② 星球本体：只保留描边。
    private static readonly Geometry PlanetOutline =
        new EllipseGeometry(new Rect(55, 55, 90, 90));

    // ③ 前半球星环：完整绘制，覆盖在星球上方。
    private static readonly Geometry FrontRing =
        StreamGeometry.Parse("M 25 100 A 75 25 0 0 1 175 100");

    // ④ 底部装饰横线。
    private static readonly RoundedRect BaseBar = new(new Rect(50, 150, 100, 12), 6);

    /// <summary>星环绕星球中心的旋转矩阵（SVG transform="rotate(-30 100 100)"）。</summary>
    private static readonly Matrix RingRotation =
        Matrix.CreateTranslation(-MarkCenter, -MarkCenter)
        * Matrix.CreateRotation(RingRotationDegrees * Math.PI / 180d)
        * Matrix.CreateTranslation(MarkCenter, MarkCenter);

    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<PulsarBrandMark, IBrush?>(nameof(MarkBrush));

    static PulsarBrandMark()
    {
        AffectsRender<PulsarBrandMark>(MarkBrushProperty);
    }

    /// <summary>标记描边画刷。默认由 <c>branding|PulsarBrandMark</c> 样式绑定到主题前景色。</summary>
    public IBrush? MarkBrush
    {
        get => GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Bounds.Size;
        var extent = Math.Min(size.Width, size.Height);
        var brush = MarkBrush;
        if (brush is null || extent <= 0 || !double.IsFinite(extent))
        {
            return;
        }

        var scale = extent / ViewBoxSize;
        var offsetX = (size.Width - extent) / 2d;
        var offsetY = (size.Height - extent) / 2d;
        using var viewBox = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY));

        var backRingPen = new Pen(brush, RingStrokeThickness, lineCap: PenLineCap.Flat);
        var frontRingPen = new Pen(brush, RingStrokeThickness, lineCap: PenLineCap.Round);
        var planetPen = new Pen(brush, PlanetStrokeThickness);

        using (context.PushTransform(RingRotation))
        {
            context.DrawGeometry(null, backRingPen, BackRingStart);
            context.DrawGeometry(null, backRingPen, BackRingEnd);
        }

        context.DrawGeometry(null, planetPen, PlanetOutline);

        using (context.PushTransform(RingRotation))
        {
            context.DrawGeometry(null, frontRingPen, FrontRing);
        }

        context.DrawRectangle(brush, null, BaseBar);
    }
}
