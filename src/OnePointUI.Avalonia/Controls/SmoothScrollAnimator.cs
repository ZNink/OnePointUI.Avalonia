using System;
using Avalonia;

namespace OnePointUI.Avalonia.Controls
{
    /// <summary>
    /// 平滑滚动的插值器：按帧把当前位置指数式地拉向目标位置。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用指数趋近而不是固定时长的缓动。</b>滚轮事件的到达节奏很不规律：
    /// 普通鼠标约每 100ms 一格，精密触控板可能每 8ms 一个很小的增量。固定时长的动画
    /// 每来一个新目标就要从头播放，连续输入时会出现一顿一顿的"重新起步"；指数趋近
    /// 则等价于一阶低通滤波，输入连续时速度平稳、输入停止时自然减速，两种设备的手感一致。</para>
    ///
    /// <para><b>为什么不会过冲。</b>每帧的落点都被夹在"当前位置"和"目标位置"之间
    /// （<see cref="Approach"/>），所以无论帧间隔多大、目标多近，都不会越过目标，
    /// 从而不会出现回弹或橡皮筋效果。相差小于 <see cref="SnapThreshold"/> 时直接
    /// 落到目标值，保证最终停在精确的边界上而不是差半个像素。</para>
    /// </remarks>
    public sealed class SmoothScrollAnimator
    {
        /// <summary>默认时间常数：约 55ms 走完 63%，约 150ms 视觉上到位。</summary>
        public const double DefaultSmoothingTime = 0.055;

        /// <summary>默认吸附阈值（像素）。小于该距离时直接落到目标。</summary>
        public const double DefaultSnapThreshold = 0.35;

        /// <summary>时间常数（秒）。越大越"绵"，越小越跟手。</summary>
        public double SmoothingTime { get; set; } = DefaultSmoothingTime;

        /// <summary>吸附阈值（像素）。距离目标小于该值时直接落到目标。</summary>
        public double SnapThreshold { get; set; } = DefaultSnapThreshold;

        /// <summary>
        /// 计算下一帧的位置。
        /// </summary>
        /// <param name="current">当前偏移。</param>
        /// <param name="target">目标偏移（调用方负责钳制到合法范围）。</param>
        /// <param name="elapsed">距上一帧的时间。</param>
        public Vector Advance(Vector current, Vector target, TimeSpan elapsed)
        {
            if (!IsFinite(current))
            {
                return target;
            }

            if (!IsFinite(target))
            {
                return current;
            }

            if (IsSettled(current, target))
            {
                // 已经到位：直接返回目标，避免无限逼近留下半个像素。
                return target;
            }

            if (elapsed <= TimeSpan.Zero)
            {
                return current;
            }

            var seconds = elapsed.TotalSeconds;
            var tau = Math.Max(SmoothingTime, 0.001);
            var factor = 1 - Math.Exp(-seconds / tau);

            return new Vector(
                Approach(current.X, target.X, current.X + ((target.X - current.X) * factor)),
                Approach(current.Y, target.Y, current.Y + ((target.Y - current.Y) * factor)));
        }

        /// <summary>当前偏移是否已经落在目标的吸附范围内。</summary>
        public bool IsSettled(Vector current, Vector target)
        {
            if (!IsFinite(current) || !IsFinite(target))
            {
                return true;
            }

            return Math.Abs(target.X - current.X) <= SnapThreshold
                   && Math.Abs(target.Y - current.Y) <= SnapThreshold;
        }

        /// <summary>把 <paramref name="value"/> 夹在 <paramref name="from"/> 与 <paramref name="to"/> 之间。</summary>
        private static double Approach(double from, double to, double value)
        {
            return from <= to
                ? Math.Min(Math.Max(value, from), to)
                : Math.Max(Math.Min(value, from), to);
        }

        private static bool IsFinite(Vector value) =>
            double.IsFinite(value.X) && double.IsFinite(value.Y);
    }
}
