namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Adaptive;

/// <summary>
/// 自适应控件的显示样式档位。
/// </summary>
public enum AdaptiveDisplayMode
{
    /// <summary>
    /// 正常样式：完整展开每一项的图标与文本，宽度不随容器收缩，
    /// 行为与改造前的控件完全一致（默认值，保证既有用法零影响）。
    /// </summary>
    Normal,

    /// <summary>
    /// 动态样式：恒定处于收缩形态 —— 主项（选中项 / 指定按钮）完整展开，
    /// 其余项仅保留图标（无图标时退化为名称首字符头像），悬停或聚焦时临时展开。
    /// </summary>
    Dynamic,

    /// <summary>
    /// 自适应样式：监听父级（或自身）宽度，低于阈值时自动从正常样式切换为
    /// 动态样式，宽度恢复后切换回正常样式，两种形态之间播放过渡动画。
    /// </summary>
    Adaptive
}

/// <summary>
/// 自适应阈值所参考的宽度来源。
/// </summary>
public enum AdaptiveWidthSource
{
    /// <summary>
    /// 父级控件宽度（默认）。控件自身伸缩不会反过来影响阈值判定，
    /// 避免「悬停展开 → 自身变宽 → 误判为宽容器 → 切回正常样式」的抖动。
    /// </summary>
    Parent,

    /// <summary>
    /// 控件自身宽度。适用于控件被显式限宽（MaxWidth / 固定宽度列）的场景。
    /// </summary>
    Self
}

/// <summary>
/// 动态样式下主项（默认展开的那一项）的指定方式。
/// </summary>
public enum AdaptivePrimaryItemMode
{
    /// <summary>
    /// 高亮样式的项（<see cref="ButtonClusterItem.IsHighlighted"/> 为 true 的项，
    /// 多个高亮时取第一个）。无高亮项时没有任何项默认展开。默认值。
    /// </summary>
    Highlighted,

    /// <summary>集群中的第一个项。</summary>
    First,

    /// <summary>不指定主项：动态样式下所有项默认均为收缩态，仅悬停展开。</summary>
    None
}
