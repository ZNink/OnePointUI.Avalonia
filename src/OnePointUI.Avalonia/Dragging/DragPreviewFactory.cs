using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Dragging;

/// <summary>
/// 为拖拽生成「跟手预览」控件。
/// </summary>
/// <remarks>
/// <para><b>首选位图快照。</b>用 <see cref="RenderTargetBitmap"/> 把源控件按当前像素
/// 渲染成一张图，文本、图标、按钮、自绘面板全部原样，不需要为每种卡片单独适配。
/// 渲染发生在源控件被淡出之前，所以拿到的是完整外观。</para>
/// <para>快照不可用时（无头测试没有渲染接口、或源控件还没有尺寸）退回
/// <b>模板重建</b>：用源控件所在 <see cref="ContentPresenter"/> 的模板重建一份。
/// 最后才退化为半透明矩形，保证任何控件都能拖。</para>
/// </remarks>
internal static class DragPreviewFactory
{
    /// <summary>创建预览控件。</summary>
    public static Control Create(Control source, out CornerRadius cornerRadius)
    {
        if (TryCreateSnapshot(source, out var snapshot))
        {
            cornerRadius = ReadCornerRadius(source);
            return snapshot;
        }

        if (TryBuildTemplate(source, out var built))
        {
            cornerRadius = built is Border border
                ? border.CornerRadius
                : ReadCornerRadius(source);
            return built;
        }

        cornerRadius = ReadCornerRadius(source);
        return new Border
        {
            CornerRadius = cornerRadius,
            Background = new SolidColorBrush(Color.Parse("#2E8AB4FF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#7A8AB4FF")),
            BorderThickness = new Thickness(1)
        };
    }

    /// <summary>把源控件渲染成位图快照；无渲染接口（无头测试）或没有尺寸时返回 <c>false</c>。</summary>
    private static bool TryCreateSnapshot(Control source, [NotNullWhen(true)] out Image? snapshot)
    {
        snapshot = null;
        RenderTargetBitmap? bitmap = null;
        try
        {
            var width = source.Bounds.Width;
            var height = source.Bounds.Height;
            if (width < 1 || height < 1)
            {
                return false;
            }

            var scaling = TopLevel.GetTopLevel(source)?.RenderScaling ?? 1;
            bitmap = new RenderTargetBitmap(
                new PixelSize(
                    Math.Max(1, (int)Math.Ceiling(width * scaling)),
                    Math.Max(1, (int)Math.Ceiling(height * scaling))),
                new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(source);

            snapshot = new Image
            {
                Source = bitmap,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill
            };
            return true;
        }
        catch (Exception)
        {
            bitmap?.Dispose();
            snapshot = null;
            return false;
        }
    }

    /// <summary>用源控件所在 ContentPresenter 的模板重建一份卡片。</summary>
    private static bool TryBuildTemplate(Control source, [NotNullWhen(true)] out Control? built)
    {
        built = null;
        var presenter = source as ContentPresenter
                        ?? source.GetSelfAndVisualAncestors()
                            .OfType<ContentPresenter>()
                            .FirstOrDefault(candidate => candidate.ContentTemplate is not null);

        if (presenter is not { Content: { } content, ContentTemplate: { } template })
        {
            return false;
        }

        try
        {
            built = template.Build(content);
        }
        catch (Exception)
        {
            built = null;
            return false;
        }

        if (built is null)
        {
            return false;
        }

        // DataTemplate.Build 不一定会带上 DataContext，缺了它所有 {Binding} 都落空。
        if (built.DataContext is null)
        {
            built.DataContext = content;
        }

        return true;
    }

    /// <summary>从源控件（或它内部的第一层 Border）读出圆角，让占位符与卡片形状一致。</summary>
    public static CornerRadius ReadCornerRadius(Control source)
    {
        if (source is Border border && border.CornerRadius != default)
        {
            return border.CornerRadius;
        }

        var inner = source.GetSelfAndVisualAncestors()
            .OfType<Border>()
            .FirstOrDefault(candidate => candidate.CornerRadius != default);
        if (inner is not null)
        {
            return inner.CornerRadius;
        }

        // BorderCard 这类模板化控件的圆角是自己的 CornerRadius 样式属性，
        // 不在 Border 上，按属性名兜底读一次。
        if (source.GetType().GetProperty("CornerRadius")?.GetValue(source) is CornerRadius corner
            && corner != default)
        {
            return corner;
        }

        return new CornerRadius(8);
    }
}
