using Avalonia;
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
/// <item>测量只碰模板里的<b>内容行</b>（<see cref="MeasureContentRowWidth"/>），绝不把
/// 条目的 <c>Width</c> 置成 NaN 再整体 Measure：NaN 赋值会让布局系统在恢复前观测到
/// 自动宽度，StackPanel 随即按自然宽度重排、命中测试错乱，悬停事件成对误触发，
/// 展开资格反复易主（实测复现：指针从按钮 3 移到按钮 2，展开态弹回按钮 1）。</item>
/// </list>
/// </remarks>
internal static class AdaptiveItemMetrics
{
    /// <summary>
    /// 测量展开态内容行（图标 + 文本）在无水平约束下的自然宽度。
    /// </summary>
    /// <param name="contentRow">条目模板里的内容行（<c>PART_FullRow</c>）。</param>
    /// <param name="height">测量约束高度；文本不换行时自然宽度与高度无关。</param>
    /// <remarks>
    /// 为什么先递归失效化再测量：Avalonia 的测量缓存以「约束相同」为命中条件，
    /// 而内容变化（本地化文案、Glyph）只让<b>内容自身</b>的测量失效，不会同步冒泡到
    /// 祖先；若直接用此前的同一约束再次 Measure，拿到的还是旧 DesiredSize
    /// （症状：切换语言后已展开项的宽度不增长，文本被旧宽度裁切）。
    /// 测量约束由调用方显式给定（无穷宽），不受条目 <c>Width</c> 钳制，
    /// 因此无需为了「自动宽度测量」而临时改写条目宽度。
    /// </remarks>
    public static double MeasureContentRowWidth(Control contentRow, double height)
    {
        InvalidateMeasureTree(contentRow);
        contentRow.Measure(new Size(double.PositiveInfinity, height));
        return contentRow.DesiredSize.Width;
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
    /// <param name="hysteresis">滞回宽度：从动态切回正常需要宽度 ≥ 阈值 + 滞回，防止边界抖动。</param>
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
