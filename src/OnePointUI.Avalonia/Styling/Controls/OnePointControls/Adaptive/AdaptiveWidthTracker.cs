using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Adaptive;

/// <summary>
/// 监听「父级控件 / 自身」的布局宽度，把宽度变化通知给宿主控件。
/// </summary>
/// <remarks>
/// <para>宽度来源默认为父级（规范要求）：动态样式下控件自身会变窄，若用自身宽度做
/// 阈值判定，悬停展开把控件顶宽时可能误判为「容器恢复了」而切回正常样式。参考父级
/// 宽度可以隔断这层反馈。<see cref="AdaptiveWidthSource.Self"/> 留给被显式限宽的控件。</para>
/// <para>父级在首次布局前宽度为 0：此时照常订阅其 <see cref="Visual.BoundsProperty"/>，
/// 布局完成后自然收到首次有效宽度；宿主侧对宽度 0 跳过判定，不会因布局竞态误切。</para>
/// </remarks>
internal sealed class AdaptiveWidthTracker : IDisposable
{
    private const double WidthEpsilon = 0.5;

    private readonly Control _host;
    private readonly Func<bool> _useParentWidth;

    private Control? _observed;
    private IDisposable? _observedSubscription;
    private bool _started;
    private bool _disposed;

    /// <param name="host">使用本跟踪器的控件。</param>
    /// <param name="useParentWidth">为 true 时参考父级控件宽度，否则参考自身宽度。</param>
    public AdaptiveWidthTracker(Control host, Func<bool> useParentWidth)
    {
        _host = host;
        _useParentWidth = useParentWidth;
    }

    /// <summary>当前观测到的宽度；尚未布局完成时为 0。</summary>
    public double ObservedWidth { get; private set; }

    /// <summary>观测宽度发生变化（含首次获得有效宽度）时触发。</summary>
    public event EventHandler? WidthChanged;

    /// <summary>开始跟踪。幂等；重复调用只会重新解析一次来源。</summary>
    public void Start()
    {
        if (_started)
        {
            ResolveSource();
            return;
        }

        _started = true;
        _host.AttachedToVisualTree += OnHostAttached;
        _host.DetachedFromVisualTree += OnHostDetached;
        ResolveSource();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.AttachedToVisualTree -= OnHostAttached;
        _host.DetachedFromVisualTree -= OnHostDetached;
        _observedSubscription?.Dispose();
        _observedSubscription = null;
        _observed = null;
    }

    private void OnHostAttached(object? sender, VisualTreeAttachmentEventArgs e) => ResolveSource();

    private void OnHostDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _observedSubscription?.Dispose();
        _observedSubscription = null;
        _observed = null;
    }

    private void ResolveSource()
    {
        if (_disposed || !_started)
        {
            return;
        }

        var next = _useParentWidth() ? FindWidthSource() : _host;
        if (ReferenceEquals(next, _observed))
        {
            return;
        }

        _observedSubscription?.Dispose();
        _observedSubscription = null;
        _observed = next;

        if (next is null)
        {
            // 没有 Control 祖先（极少见）：退回自身宽度。
            UpdateWidth(_host.Bounds.Width);
            return;
        }

        _observedSubscription = next.GetObservable(Visual.BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ => UpdateWidth(next.Bounds.Width)));
        UpdateWidth(next.Bounds.Width);
    }

    /// <summary>
    /// 取最近的祖先控件：通常是直接父级（Grid / StackPanel / Border / ContentPresenter）。
    /// 它的宽度就是规范所说的「父级控件宽度」，不随本控件的伸缩而变化。
    /// </summary>
    private Control? FindWidthSource()
    {
        var current = _host.GetVisualParent();
        while (current is not null)
        {
            if (current is Control control)
            {
                return control;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private void UpdateWidth(double width)
    {
        if (Math.Abs(width - ObservedWidth) < WidthEpsilon)
        {
            return;
        }

        ObservedWidth = width;
        WidthChanged?.Invoke(this, EventArgs.Empty);
    }
}
