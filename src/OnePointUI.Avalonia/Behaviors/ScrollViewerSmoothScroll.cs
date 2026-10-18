using Avalonia;
using Avalonia.Controls;

namespace OnePointUI.Avalonia.Behaviors
{
    /// <summary>
    /// 平滑滚动的总开关。
    /// </summary>
    /// <remarks>
    /// <para>真正的滚动逻辑在 <see cref="OnePointUI.Avalonia.Controls.SmoothScrollContentPresenter"/>
    /// 里（模板内的内容呈现器），这个附加属性只负责把开关值传给呈现器。默认开启，
    /// 全局生效；需要某个容器瞬间滚动时单独关闭即可：</para>
    /// <code>
    /// &lt;ScrollViewer behaviors:ScrollViewerSmoothScroll.EnableSmoothScroll="False"&gt;
    /// </code>
    ///
    /// <para><b>历史注意。</b>早期版本在这里直接监听 <c>PointerWheelChanged</c> 并自己
    /// 驱动一个定时器。但滚轮事件在冒泡到 <see cref="ScrollViewer"/> 之前就已经被模板内的
    /// <see cref="Avalonia.Controls.Presenters.ScrollContentPresenter"/> 处理并置
    /// <c>Handled</c>，所以那段代码实际上一帧都没有执行过；更糟的是它在边界处会把
    /// 本应传给外层的滚轮事件吞掉，破坏嵌套滚动。改造后这里不再注册任何事件处理器。</para>
    /// </remarks>
    public static class ScrollViewerSmoothScroll
    {
        /// <summary>
        /// 是否对该 <see cref="ScrollViewer"/> 启用平滑滚动。默认 <c>true</c>。
        /// </summary>
        public static readonly AttachedProperty<bool> EnableSmoothScrollProperty =
            AvaloniaProperty.RegisterAttached<StyledElement, bool>(
                "EnableSmoothScroll",
                typeof(ScrollViewerSmoothScroll),
                defaultValue: true);

        public static void SetEnableSmoothScroll(StyledElement element, bool value)
            => element.SetValue(EnableSmoothScrollProperty, value);

        public static bool GetEnableSmoothScroll(StyledElement element)
            => element.GetValue(EnableSmoothScrollProperty);
    }
}
