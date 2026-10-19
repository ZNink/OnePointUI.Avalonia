using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Adaptive;

/// <summary>
/// 动态样式下条目宽度的测量与无过渡写入。
/// </summary>
/// <remarks>
/// <para>为什么需要单独一套度量逻辑：</para>
/// <list type="bullet">
/// <item>宽度过渡（<c>DoubleTransition</c>）只有在「起止两端都是具体数值」时才会插值；
/// 自动宽度（<c>Width = NaN</c>）作为起点会让整段动画产出 NaN，表现为瞬间跳变
/// （与 <c>MenuEffects</c> 里高亮块 Height 必须先从 0 起相同的原因）。</item>
/// <item>因此首次进入动态样式时，必须先把每一条的自动宽度<b>固化为实测自然宽度</b>，
/// 之后 collapsed ↔ expanded 才是一段合法的数值动画。</item>
/// <item>固化赋值本身不能触发过渡，否则会播放一段「自然宽度 → 自然宽度」的空动画并
/// 抢占动画时钟；这里用「临时摘掉 Transitions → 赋值 → 装回」实现静默写入。</item>
/// </list>
/// </remarks>
internal static class AdaptiveItemMetrics
{
    /// <summary>
    /// 测量控件在无水平约束下的自然宽度（图标 + 间距 + 文本 + 内边距的完整宽度）。
    /// </summary>
    /// <param name="item">待测量的条目（ItemsControl 容器）。</param>
    /// <remarks>
    /// 为什么先整棵子树失效化再测量：Avalonia 的测量缓存以「约束相同」为命中条件，
    /// 而内容变化（本地化文案、Glyph）只让<b>内容自身</b>的测量失效，不会同步冒泡到
    /// 祖先；若直接用此前的同一约束再次 Measure，拿到的还是旧 DesiredSize
    /// （症状：切换语言后已展开项的宽度不增长，文本被旧宽度裁切）。
    /// </remarks>
    public static double MeasureNaturalWidth(Control item)
    {
        var height = item.Bounds.Height;
        if (!(height > 0))
        {
            // 首次附加到可视树时尚未布局：用最小高度兜底。
            // 文本不换行时自然宽度与高度无关，兜底值不影响宽度结果。
            height = item.MinHeight > 0 ? item.MinHeight : 36;
        }

        var previousWidth = item.Width;
        var previousTransitions = item.Transitions;
        item.Transitions = null;
        try
        {
            item.Width = double.NaN;
            InvalidateMeasureTree(item);
            item.Measure(new Size(double.PositiveInfinity, height));
            var natural = item.DesiredSize.Width;
            if (natural > 0)
            {
                return natural;
            }

            // 测量失败（尚未应用模板等）时保持原值，避免把宽度写成 0。
            return !double.IsNaN(previousWidth) ? previousWidth : 0;
        }
        finally
        {
            item.Width = previousWidth;
            item.Transitions = previousTransitions;
        }
    }

    /// <summary>递归失效子树测量，保证内容变化后重新测量拿到新尺寸。</summary>
    private static void InvalidateMeasureTree(Visual visual)
    {
        if (visual is Layoutable layoutable)
        {
            layoutable.InvalidateMeasure();
        }

        foreach (var child in visual.GetVisualChildren())
        {
            InvalidateMeasureTree(child);
        }
    }

    /// <summary>
    /// 无过渡地写入宽度：用于初始固化、内容变化后的自然宽度刷新，
    /// 以及切回正常样式前的最终状态同步。
    /// </summary>
    public static void SetWidthImmediate(Control item, double width)
    {
        var previousTransitions = item.Transitions;
        item.Transitions = null;
        try
        {
            item.Width = width;
        }
        finally
        {
            item.Transitions = previousTransitions;
        }
    }
}

/// <summary>
/// 正常样式 ↔ 动态样式的阈值判定（含滞回，两个控件共用同一套语义）。
/// </summary>
internal static class AdaptiveDisplayMath
{
    /// <summary>
    /// 计算动态样式是否应当生效。
    /// </summary>
    /// <param name="mode">控件声明的显示模式。</param>
    /// <param name="width">观测到的容器宽度（父级或自身）。</param>
    /// <param name="threshold">切换阈值；≤ 0 表示关闭自适应。</param>
    /// <param name="hysteresis">滞回宽度；从动态切回正常需要宽度 ≥ 阈值 + 滞回，防止边界抖动。</param>
    /// <param name="currentlyActive">当前是否已处于动态样式（滞回判定的基点）。</param>
    public static bool IsDynamicActive(
        AdaptiveDisplayMode mode,
        double width,
        double threshold,
        double hysteresis,
        bool currentlyActive)
    {
        switch (mode)
        {
            case AdaptiveDisplayMode.Dynamic:
                return true;
            case AdaptiveDisplayMode.Adaptive:
                // 宽度尚未就绪（0）时保持现状，不因布局竞态误切。
                if (!(width > 0) || !(threshold > 0))
                {
                    return false;
                }

                return currentlyActive
                    ? width < threshold + Math.Max(0, hysteresis)
                    : width < threshold;
            default:
                return false;
        }
    }
}
