using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OnePointUI.Avalonia.Dragging;

/// <summary>
/// 一次拖拽携带的数据。
/// </summary>
/// <remarks>
/// <see cref="Format"/> 用于匹配投放目标：只有格式一致的
/// <see cref="DragDropBehaviors.IsDropTarget"/> 才会接住这次投放，
/// 避免「把路径卡片丢进插槽」这类跨语义的误投。
/// </remarks>
public sealed class DragPayload
{
    /// <summary>创建一份拖拽数据。</summary>
    /// <param name="format">格式标识，例如 <c>replica</c> / <c>sortable</c>。</param>
    /// <param name="data">业务数据，由投放入口自行解释。</param>
    /// <param name="effects">允许的拖拽效果，决定是否需要按压点位移。</param>
    public DragPayload(string format, object? data, DragDropEffects effects = DragDropEffects.Move)
    {
        Format = format;
        Data = data;
        Effects = effects;
    }

    /// <summary>格式标识。</summary>
    public string Format { get; }

    /// <summary>业务数据。</summary>
    public object? Data { get; }

    /// <summary>允许的效果。</summary>
    public DragDropEffects Effects { get; }
}

/// <summary>
/// 投放事件参数。
/// </summary>
public sealed class DragDropEventArgs : RoutedEventArgs
{
    /// <summary>创建投放事件参数。</summary>
    public DragDropEventArgs(DragPayload payload, Control source)
        : base(DragDropBehaviors.DropEvent)
    {
        Payload = payload;
        Source = source;
    }

    /// <summary>拖拽数据。</summary>
    public DragPayload Payload { get; }

    /// <summary>发起拖拽的控件。</summary>
    public new Control Source { get; }
}

/// <summary>拖拽过程中指针移动的参数。</summary>
public sealed class DragMovedEventArgs(Point position) : EventArgs
{
    /// <summary>指针位置（顶层坐标）。</summary>
    public Point Position { get; } = position;
}

/// <summary>拖拽结束的参数。</summary>
public sealed class DragCompletedEventArgs(bool dropped, bool handled, Control? target) : EventArgs
{
    /// <summary>是否成功投放（<c>false</c> 表示取消或落在空白处）。</summary>
    public bool Dropped { get; } = dropped;

    /// <summary>投放目标是否处理了这次投放。</summary>
    public bool Handled { get; } = handled;

    /// <summary>接住投放的控件。</summary>
    public Control? Target { get; } = target;
}
