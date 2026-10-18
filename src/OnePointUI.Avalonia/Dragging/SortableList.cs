using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Dragging;

/// <summary>列表重排请求的参数。</summary>
public sealed class SortableListMoveEventArgs : RoutedEventArgs
{
    /// <summary>创建重排参数。</summary>
    public SortableListMoveEventArgs(RoutedEvent routedEvent, int from, int to)
        : base(routedEvent)
    {
        From = from;
        To = to;
    }

    /// <summary>原索引。</summary>
    public int From { get; }

    /// <summary>目标索引。</summary>
    public int To { get; }
}

/// <summary>
/// 让纵向列表的条目可以拖拽重排的附加行为。
/// </summary>
/// <remarks>
/// <para><b>只做视觉与命中，不改数据。</b>拖动过程中条目本身不移动（由
/// <see cref="DragSession"/> 的跟手预览代替），其余条目用 <c>RenderTransform</c> 平移让出位置，
/// 半透明占位符标出落点；真正改集合只在指针释放时通过
/// <see cref="ItemMoveRequestedEvent"/> 交给页面处理，因此不会在拖动途中触发持久化。</para>
/// <para><b>为什么要自己算落点。</b>列表里条目的高度可以不一致（例如路径行会换行），
/// 所以落点用"指针越过哪一条的中线"判定，让位距离用被拖条目的实际高度 + 实测间距，
/// 不假设固定行高。</para>
/// </remarks>
public static class SortableList
{
    private const string DefaultFormat = "sortable";

    private static readonly ConditionalWeakTable<ItemsControl, SortableState> States = new();

    /// <summary>开启/关闭列表重排。</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(SortableList));

    /// <summary>拖拽格式，默认 <c>sortable</c>；不同列表之间互不干扰。</summary>
    public static readonly AttachedProperty<string> DragFormatProperty =
        AvaloniaProperty.RegisterAttached<Control, string>(
            "DragFormat",
            typeof(SortableList),
            DefaultFormat);

    /// <summary>重排请求（指针释放时触发一次，由页面调用集合的 Move / Remove+Insert）。</summary>
    public static readonly RoutedEvent<SortableListMoveEventArgs> ItemMoveRequestedEvent =
        RoutedEvent.Register<SortableListMoveEventArgs>(
            "ItemMoveRequested",
            RoutingStrategies.Bubble,
            typeof(SortableList));

    static SortableList()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>(OnIsEnabledChanged);
    }

    /// <summary>读取 <see cref="IsEnabledProperty"/>。</summary>
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    /// <summary>设置 <see cref="IsEnabledProperty"/>。</summary>
    public static void SetIsEnabled(Control control, bool value) =>
        control.SetValue(IsEnabledProperty, value);

    /// <summary>读取 <see cref="DragFormatProperty"/>。</summary>
    public static string GetDragFormat(Control control) => control.GetValue(DragFormatProperty);

    /// <summary>设置 <see cref="DragFormatProperty"/>。</summary>
    public static void SetDragFormat(Control control, string value) =>
        control.SetValue(DragFormatProperty, value);

    /// <summary>订阅重排请求。</summary>
    public static void AddItemMoveRequestedHandler(
        Control control,
        EventHandler<SortableListMoveEventArgs> handler) =>
        control.AddHandler(ItemMoveRequestedEvent, handler);

    /// <summary>取消订阅重排请求。</summary>
    public static void RemoveItemMoveRequestedHandler(
        Control control,
        EventHandler<SortableListMoveEventArgs> handler) =>
        control.RemoveHandler(ItemMoveRequestedEvent, handler);

    private static void OnIsEnabledChanged(ItemsControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            States.GetOrCreateValue(control).Attach(control);
        }
        else if (States.TryGetValue(control, out var state))
        {
            state.Detach();
        }
    }

    private sealed class SortableState
    {
        private ItemsControl? _items;
        private Panel? _panel;
        private TopLevel? _topLevel;
        private Control? _dragged;
        private IPointer? _pointer;
        private Point _pressPoint;
        private Control? _candidate;
        private DragSession? _session;
        private int _sourceIndex;
        private int _targetIndex;
        private double _draggedHeight;
        private double _spacing;
        private double _sourceTop;

        /// <summary>
        /// 拖拽开始时的条目布局坐标快照。
        /// </summary>
        /// <remarks>
        /// <b>为什么必须快照，而不能拖动中途读 <c>TranslatePoint</c>。</b>
        /// <c>TranslatePoint</c> 会把 <c>RenderTransform</c> 算进去——而让位本身就是
        /// 靠 RenderTransform 实现的，还会带 220ms 过渡。拖动中途读到的坐标是
        /// 「布局位置 + 动画中间值」，在它上面再叠加目标位移等于位移算了两次，
        /// 指针一快、跨越多行，占位符就会叠到别的卡片上。快照在施加任何位移之前拍下，
        /// 落点计算全部基于原始布局坐标；面板自身（含滚动）的偏移在
        /// <see cref="ShowPlaceholder"/> 里换算成顶层坐标，不受影响。
        /// </remarks>
        private readonly Dictionary<Control, double> _layoutTops = new();

        public void Attach(ItemsControl items)
        {
            if (ReferenceEquals(_items, items))
            {
                return;
            }

            Detach();
            _items = items;
            items.AddHandler(
                InputElement.PointerPressedEvent,
                OnPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            items.AddHandler(
                InputElement.PointerMovedEvent,
                OnPointerMoved,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            items.AddHandler(
                InputElement.PointerReleasedEvent,
                OnPointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            items.AddHandler(
                InputElement.PointerCaptureLostEvent,
                OnPointerCaptureLost,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        public void Detach()
        {
            if (_items is not { } items)
            {
                return;
            }

            items.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            items.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            items.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            items.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
            _items = null;
            _candidate = null;
            _pointer = null;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_items is not { } items
                || _session is not null
                || !e.GetCurrentPoint(items).Properties.IsLeftButtonPressed
                || IsInteractiveSource(e.Source as Visual))
            {
                return;
            }

            _candidate = FindContainer(e.Source as Visual);
            _pointer = _candidate is null ? null : e.Pointer;
            _pressPoint = e.GetPosition(items);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_session is not null)
            {
                // 会话自己捕获了指针，落点由 Moved 事件驱动。
                return;
            }

            if (_items is not { } items
                || _candidate is null
                || _pointer is null
                || e.Pointer.Id != _pointer.Id)
            {
                return;
            }

            if (!e.GetCurrentPoint(items).Properties.IsLeftButtonPressed)
            {
                ClearCandidate();
                return;
            }

            var current = e.GetPosition(items);
            if (Math.Abs(current.X - _pressPoint.X) < 5 && Math.Abs(current.Y - _pressPoint.Y) < 5)
            {
                return;
            }

            StartDrag(e);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pointer is not null && e.Pointer.Id == _pointer.Id)
            {
                ClearCandidate();
            }
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearCandidate();

        private void ClearCandidate()
        {
            _candidate = null;
            _pointer = null;
        }

        private void StartDrag(PointerEventArgs e)
        {
            if (_items is not { } items || _candidate is not { } container)
            {
                ClearCandidate();
                return;
            }

            var topLevel = TopLevel.GetTopLevel(items);
            var panel = items.ItemsPanelRoot;
            var index = items.IndexFromContainer(container);
            if (topLevel is null || panel is null || index < 0)
            {
                ClearCandidate();
                return;
            }

            _panel = panel;
            _topLevel = topLevel;
            _dragged = container;
            _sourceIndex = index;
            _targetIndex = index;
            _draggedHeight = container.Bounds.Height;
            SnapshotLayoutTops();
            _sourceTop = TopOf(container);
            _spacing = MeasureSpacing(container);

            var payload = new DragPayload(
                GetDragFormat(items),
                container.DataContext,
                DragDropEffects.Move);

            var session = DragSession.Begin(
                container,
                payload,
                e,
                hideSource: true,
                ownsPlaceholder: false);
            _session = session;
            session.Moved += OnSessionMoved;
            session.Completed += OnSessionCompleted;

            ClearCandidate();
            ApplyShifts();
            ShowPlaceholder();
        }

        private void OnSessionMoved(object? sender, DragMovedEventArgs e)
        {
            if (_session is null || _items is null || _panel is null)
            {
                return;
            }

            var panelPoint = _session.GetPosition(_panel);
            var target = ComputeTargetIndex(panelPoint.Y);
            if (target == _targetIndex)
            {
                return;
            }

            _targetIndex = target;
            ApplyShifts();
            ShowPlaceholder();
        }

        private void OnSessionCompleted(object? sender, DragCompletedEventArgs e)
        {
            var session = _session;
            _session = null;
            if (session is not null)
            {
                session.Moved -= OnSessionMoved;
                session.Completed -= OnSessionCompleted;
            }

            var from = _sourceIndex;
            var to = _targetIndex;
            var shouldMove = e.Dropped
                             && !e.Handled
                             && from != to
                             && _items is { } items
                             && from >= 0
                             && from < items.ItemCount
                             && to >= 0
                             && to < items.ItemCount;

            ResetShifts();

            if (shouldMove && _items is { } target)
            {
                target.RaiseEvent(new SortableListMoveEventArgs(ItemMoveRequestedEvent, from, to));
            }

            _dragged = null;
            _panel = null;
            _topLevel = null;
            _draggedHeight = 0;
            _spacing = 0;
            _layoutTops.Clear();
        }

        private int ComputeTargetIndex(double pointerY)
        {
            if (_items is not { } items)
            {
                return _targetIndex;
            }

            var insertion = 0;
            for (var index = 0; index < items.ItemCount; index++)
            {
                if (index == _sourceIndex || items.ContainerFromIndex(index) is not { } container)
                {
                    continue;
                }

                if (pointerY < TopOf(container) + container.Bounds.Height / 2)
                {
                    return insertion;
                }

                insertion++;
            }

            return insertion;
        }

        private void ApplyShifts()
        {
            if (_items is not { } items)
            {
                return;
            }

            var amount = _draggedHeight + _spacing;
            for (var index = 0; index < items.ItemCount; index++)
            {
                if (index == _sourceIndex || items.ContainerFromIndex(index) is not { } container)
                {
                    continue;
                }

                double shift;
                if (_sourceIndex < _targetIndex && index > _sourceIndex && index <= _targetIndex)
                {
                    shift = -amount;
                }
                else if (_sourceIndex > _targetIndex && index >= _targetIndex && index < _sourceIndex)
                {
                    shift = amount;
                }
                else
                {
                    shift = 0;
                }

                SetShift(container, shift);
            }
        }

        private static void SetShift(Control container, double shift)
        {
            if (shift == 0)
            {
                container.RenderTransform = null;
                return;
            }

            if (container.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform
                {
                    Transitions = new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = TranslateTransform.YProperty,
                            Duration = TimeSpan.FromMilliseconds(220),
                            Easing = new CubicEaseOut()
                        }
                    }
                };
                container.RenderTransform = transform;
            }

            transform.Y = shift;
        }

        private void ResetShifts()
        {
            if (_items is not { } items)
            {
                return;
            }

            for (var index = 0; index < items.ItemCount; index++)
            {
                if (items.ContainerFromIndex(index) is { } container)
                {
                    container.RenderTransform = null;
                }
            }
        }

        private void ShowPlaceholder()
        {
            if (_session is null || _items is null || _panel is null || _topLevel is null || _dragged is null)
            {
                return;
            }

            var gapTop = GapTop();
            var gapOrigin = _panel.TranslatePoint(new Point(0, gapTop), _topLevel);
            var containerOrigin = _dragged.TranslatePoint(default, _topLevel);
            if (gapOrigin is null || containerOrigin is null)
            {
                return;
            }

            // 落点与卡片同宽同高，只沿纵向移动：横向永远对齐被拖条目的左边缘。
            var cornerRadius = DragPreviewFactory.ReadCornerRadius(_dragged).TopLeft;
            _session.ShowPlaceholder(
                new Rect(
                    containerOrigin.Value.X,
                    gapOrigin.Value.Y,
                    _dragged.Bounds.Width,
                    _draggedHeight),
                cornerRadius);
        }

        private double GapTop()
        {
            if (_items is not { } items)
            {
                return _sourceTop;
            }

            if (_targetIndex == _sourceIndex)
            {
                return _sourceTop;
            }

            var container = items.ContainerFromIndex(_targetIndex);
            if (container is null)
            {
                return _sourceTop;
            }

            if (_targetIndex < _sourceIndex)
            {
                // 向上拖：目标位上的条目会整体下移，空出的正是它原来的布局位置。
                // 不能叠加位移——被拖条目要落进去的坐标就是 TopOf(container)。
                return TopOf(container);
            }

            // 向下拖：目标位上的条目上移后，空隙紧贴它移动后的底部。
            return TopOf(container) + ComputeShift(_targetIndex) + container.Bounds.Height + _spacing;
        }

        /// <summary>在施加任何位移之前拍下全部条目的布局纵坐标。</summary>
        private void SnapshotLayoutTops()
        {
            _layoutTops.Clear();
            if (_items is not { } items || _panel is null)
            {
                return;
            }

            for (var index = 0; index < items.ItemCount; index++)
            {
                if (items.ContainerFromIndex(index) is not { } container)
                {
                    continue;
                }

                _layoutTops[container] = container.TranslatePoint(default, _panel)?.Y ?? container.Bounds.Y;
            }
        }

        private double ComputeShift(int index)
        {
            var amount = _draggedHeight + _spacing;
            if (_sourceIndex < _targetIndex && index > _sourceIndex && index <= _targetIndex)
            {
                return -amount;
            }

            if (_sourceIndex > _targetIndex && index >= _targetIndex && index < _sourceIndex)
            {
                return amount;
            }

            return 0;
        }

        /// <summary>
        /// 条目在面板里的布局纵坐标（不含让位位移）。
        /// </summary>
        /// <remarks>
        /// 快照里有就用快照——这是拖拽期间唯一可信的坐标；只有虚拟化后补建出来的
        /// 容器（还没被施加位移）才回退到实时测量。
        /// </remarks>
        private double TopOf(Control container) =>
            _layoutTops.TryGetValue(container, out var top)
                ? top
                : _panel is { } panel
                    ? container.TranslatePoint(default, panel)?.Y ?? 0
                    : 0;

        /// <summary>
        /// 实测被拖条目「让位一格」的距离：相邻条目顶部之间的节距减去被拖条目高度。
        /// </summary>
        /// <remarks>
        /// <b>必须量被拖条目自己的槽位，不能随便找两条不相邻的条目相减。</b>
        /// 旧实现跳过被拖容器后取前两个未拖动容器的间距——当被拖条目位于索引 1 时，
        /// 这两个容器是 0 和 2，量出的"间距"横跨了被拖条目的整个槽位（高度 + 上下两段间隙），
        /// 让位距离因此变成两格：原第 1 位的卡片被推到第 3 位下方，被拖条目原位留白。
        /// 改为量被拖条目与相邻条目的顶距（末位条目回退用前一个邻居），任何起点位置都正确。
        /// </remarks>
/// <summary>
        /// 实测被拖条目「让位一格」的距离：相邻条目顶部之间的节距减去被拖条目高度。
        /// </summary>
        /// <remarks>
        /// <b>必须量被拖条目自己的槽位，不能随便找两条不相邻的条目相减。</b>
        /// 旧实现跳过被拖容器后取前两个未拖动容器的间距——当被拖条目位于索引 1 时，
        /// 这两个容器是 0 和 2，量出的"间距"横跨了被拖条目的整个槽位（高度 + 上下两段间隙），
        /// 让位距离因此变成两格：原第 1 位的卡片被推到第 3 位下方，被拖条目原位留白。
        /// 改为量被拖条目与相邻条目的顶距（末位条目回退用前一个邻居），任何起点位置都正确。
        /// </remarks>
        private double MeasureSpacing(Control dragged)
        {
            if (_items is not { } items || _sourceIndex < 0)
            {
                return 0;
            }

            var next = items.ContainerFromIndex(_sourceIndex + 1);
            if (next is not null)
            {
                return Math.Max(0, TopOf(next) - TopOf(dragged) - dragged.Bounds.Height);
            }

            var previous = items.ContainerFromIndex(_sourceIndex - 1);
            if (previous is not null)
            {
                return Math.Max(0, TopOf(dragged) - TopOf(previous) - previous.Bounds.Height);
            }

            return 0;
        }

        private Control? FindContainer(Visual? source)
        {
            if (_items is not { } items || source is null)
            {
                return null;
            }

            foreach (var visual in source.GetSelfAndVisualAncestors())
            {
                if (visual is Control control && items.IndexFromContainer(control) >= 0)
                {
                    return control;
                }
            }

            return null;
        }

        private static bool IsInteractiveSource(Visual? source) =>
            source is not null
            && source.GetSelfAndVisualAncestors()
                .Any(visual => visual is Button or ToggleButton or TextBox or ScrollBar or Slider);
    }
}
