using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;

namespace OnePointUI.Avalonia.Controls
{
    /// <summary>
    /// 把滚轮滚动从"瞬间跳变"改成平滑过渡的内容呈现器。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须换掉呈现器。</b>Avalonia 的滚轮滚动由模板内的
    /// <see cref="ScrollContentPresenter"/> 自己处理：它在冒泡阶段就把 <c>Offset</c>
    /// 一步到位地改完并置 <c>Handled = true</c>。任何挂在 <see cref="ScrollViewer"/>
    /// 上的处理器都晚于它，看到的永远是"已经滚完"的事件，最多只能再补一段动画
    /// —— 而这会先闪一下再回退。所以平滑滚动必须做在呈现器内部，替换原生实现，
    /// 而不是在 ScrollViewer 层拦截。</para>
    ///
    /// <para><b>边界与滚动链。</b>滚轮只在"本控件确实还能朝该方向移动"时才消费事件：
    /// 目标偏移按 <c>[0, Extent - Viewport]</c> 精确钳制，到达边界后再滚同一方向，
    /// 目标不变，事件保持未处理并冒泡给外层（<c>IsScrollChainingEnabled</c> 为 false
    /// 时仍然吞掉）。这与原生实现逐条对应，因此嵌套滚动、列表滚到底后继续滚页面
    /// 的行为不变，也不会出现越界或回弹。</para>
    /// </remarks>
    public class SmoothScrollContentPresenter : ScrollContentPresenter
    {
        /// <summary>一格滚轮的基准像素数，与原生实现保持一致。</summary>
        private const double WheelStep = 50;

        /// <summary>判定"外部改动了偏移"的容差（像素）。</summary>
        private const double ExternalChangeTolerance = 0.5;

        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

        /// <summary>
        /// 是否启用平滑滚动。默认开启；在 <see cref="ScrollViewer"/> 上设置
        /// <c>behaviors:ScrollViewerSmoothScroll.EnableSmoothScroll="False"</c> 可单点关闭。
        /// </summary>
        public static readonly StyledProperty<bool> IsSmoothScrollingEnabledProperty =
            AvaloniaProperty.Register<SmoothScrollContentPresenter, bool>(
                nameof(IsSmoothScrollingEnabled),
                defaultValue: true);

        private readonly SmoothScrollAnimator _animator = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private DispatcherTimer? _timer;
        private TimeSpan _lastFrame;
        private Vector _target;
        private Vector _applied;
        private bool _isAnimating;

        /// <summary>是否启用平滑滚动。</summary>
        public bool IsSmoothScrollingEnabled
        {
            get => GetValue(IsSmoothScrollingEnabledProperty);
            set => SetValue(IsSmoothScrollingEnabledProperty, value);
        }

        /// <summary>是否正在播放滚动动画。</summary>
        public bool IsScrollAnimating => _isAnimating;

        /// <summary>
        /// 当前动画的目标偏移；未播放动画时等于当前偏移。
        /// </summary>
        public Vector ScrollTarget => _isAnimating ? _target : Offset;

        /// <summary>
        /// 处理滚轮：只更新动画目标，实际偏移由帧定时器逐帧逼近。
        /// </summary>
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if (!IsSmoothScrollingEnabled)
            {
                base.OnPointerWheelChanged(e);
                return;
            }

            var maximum = MaxOffset();
            if (maximum.X <= 0 && maximum.Y <= 0)
            {
                // 没有可滚动空间：保持未处理，让外层滚动容器接管。
                return;
            }

            // 上一次滚动之后偏移被外部改过（例如正在拖动滚动条）：先作废旧动画，
            // 再以当前偏移为基准重算，否则新的一格会把内容拉回旧目标。
            if (_isAnimating && !NearlyEquals(Offset, _applied, ExternalChangeTolerance))
            {
                StopAnimation();
                _target = Offset;
            }

            var delta = e.Delta;

            // Shift + 滚轮 = 横向滚动，与原生实现一致。
            if (e.KeyModifiers == KeyModifiers.Shift && Math.Abs(delta.X) < 0.001)
            {
                delta = new Vector(delta.Y, delta.X);
            }
            else if (FlowDirection == global::Avalonia.Media.FlowDirection.RightToLeft)
            {
                delta = new Vector(-delta.X, delta.Y);
            }

            var step = GetWheelStep();
            var pending = _isAnimating ? _target : Offset;

            var nextX = ResolveTarget(Offset.X, pending.X, -delta.X * step.X, maximum.X);
            var nextY = ResolveTarget(Offset.Y, pending.Y, -delta.Y * step.Y, maximum.Y);
            var next = new Vector(nextX, nextY);

            if (NearlyEquals(next, pending))
            {
                // 已经在边界：本控件无法再朝该方向移动。开启滚动链时把事件交给外层。
                e.Handled = !IsScrollChainingEnabled;
                return;
            }

            _target = next;
            _applied = Offset;
            _lastFrame = _clock.Elapsed;
            _isAnimating = true;
            EnsureTimer();
            e.Handled = true;
        }

        /// <inheritdoc />
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsSmoothScrollingEnabledProperty
                && change.GetNewValue<bool>() == false)
            {
                // 运行中关闭：直接落到目标，避免停在半路上。
                Finish();
            }
        }

        /// <inheritdoc />
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _isAnimating = false;
            _timer?.Stop();
            _timer = null;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// 计算某一轴的新目标。方向反转时以当前位置为起点，立刻掉头，
        /// 而不是先沿旧方向把没走完的距离滑完。
        /// </summary>
        private double ResolveTarget(double current, double pending, double delta, double maximum)
        {
            var origin = pending;

            if (_isAnimating && (pending - current) * delta < 0)
            {
                origin = current;
            }

            var value = origin + delta;
            if (!double.IsFinite(value))
            {
                value = current;
            }

            var max = Math.Max(0, maximum);
            return Math.Clamp(value, 0, max);
        }

        /// <summary>取一格滚轮对应的像素步长；逻辑滚动（虚拟化列表）沿用其 ScrollSize。</summary>
        private (double X, double Y) GetWheelStep()
        {
            if (Child is ILogicalScrollable { IsLogicalScrollEnabled: true } logical)
            {
                var size = logical.ScrollSize;
                return (size.Width > 0 ? size.Width : WheelStep,
                        size.Height > 0 ? size.Height : WheelStep);
            }

            return (WheelStep, WheelStep);
        }

        private void EnsureTimer()
        {
            if (_timer is null)
            {
                _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, OnAnimationFrame);
                _timer.Start();
                return;
            }

            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        private void OnAnimationFrame(object? sender, EventArgs e)
        {
            if (!_isAnimating)
            {
                _timer?.Stop();
                return;
            }

            var now = _clock.Elapsed;
            var elapsed = now - _lastFrame;
            _lastFrame = now;

            AdvanceScrollAnimation(elapsed);
        }

        /// <summary>
        /// 推进一帧滚动动画。正常运行时由帧定时器驱动；无头测试或自带帧循环的宿主
        /// 可以直接调用它，逐帧决定推进的时间跨度。
        /// </summary>
        public void AdvanceScrollAnimation(TimeSpan elapsed)
        {
            if (!_isAnimating)
            {
                return;
            }

            // 偏移被第三方改动（拖动滚动条、键盘翻页、ScrollIntoView、触摸手势…）：
            // 立刻停止动画，把控制权让给用户，避免两边互相拉扯。
            if (!NearlyEquals(Offset, _applied, ExternalChangeTolerance))
            {
                StopAnimation();
                return;
            }

            var maximum = MaxOffset();
            _target = new Vector(
                Math.Clamp(_target.X, 0, maximum.X),
                Math.Clamp(_target.Y, 0, maximum.Y));

            if (_animator.IsSettled(Offset, _target))
            {
                // 精确落到目标（通常是 0 或 Extent - Viewport）：边界上不多滚一个像素。
                ApplyOffset(_target);
                StopAnimation();
                return;
            }

            ApplyOffset(_animator.Advance(Offset, _target, elapsed));
        }

        private void ApplyOffset(Vector value)
        {
            SetCurrentValue(OffsetProperty, value);

            // 读回被强制转换后的真实值：下一帧据此判断是否有人从外部改了偏移。
            _applied = Offset;
        }

        private void Finish()
        {
            if (!_isAnimating)
            {
                return;
            }

            ApplyOffset(_target);
            StopAnimation();
        }

        private void StopAnimation()
        {
            _isAnimating = false;
            _timer?.Stop();
        }

        private Vector MaxOffset()
        {
            var extent = Extent;
            var viewport = Viewport;

            return new Vector(
                Math.Max(0, extent.Width - viewport.Width),
                Math.Max(0, extent.Height - viewport.Height));
        }

        private static bool NearlyEquals(Vector left, Vector right, double tolerance = 0.01) =>
            Math.Abs(left.X - right.X) <= tolerance && Math.Abs(left.Y - right.Y) <= tolerance;
    }
}
