using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation;

/// <summary>
/// 保留选择状态的 <see cref="ListBox"/> 基类。
/// </summary>
/// <remarks>
/// 页面导航会把旧页面从视觉树卸下，再把缓存页面挂回去。Avalonia 在这段生命周期
/// 里可能先把容器的 <see cref="ListBoxItem.IsSelected"/> 清零，而
/// <see cref="SelectingItemsControl.SelectedIndex"/> 仍保持原值；只依赖容器的
/// <c>:selected</c> 伪类就会出现所有选项都未选中的空白状态。这里保存最后一个
/// 有效索引，并在重新挂载和模板重新应用后再次同步容器。用户主动清空选择时，
/// 仍会在当前视觉树生命周期内清除保存值。
/// </remarks>
public abstract class SelectionStateListBox : ListBox
{
    private int _lastSelectedIndex = -1;
    private bool _isAttached;
    private bool _restoreQueued;
    private bool _restoring;

    protected SelectionStateListBox()
    {
        SelectionChanged += OnSelectionChanged;
        Items.CollectionChanged += (_, _) => QueueSelectionRestore();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SelectedIndex >= 0)
        {
            _lastSelectedIndex = SelectedIndex;
            return;
        }

        if (_restoring)
        {
            return;
        }

        // 容器回收引起的 -1 通常发生在 Detached 回调之前；延后判断可以区分
        // 它与用户真正把选择清空的操作。
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_isAttached && SelectedIndex < 0 && !_restoring)
                {
                    _lastSelectedIndex = -1;
                }
            },
            DispatcherPriority.Input);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        RememberCurrentSelection();
        QueueSelectionRestore();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        RememberCurrentSelection();
        _isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        QueueSelectionRestore();
    }

    private void RememberCurrentSelection()
    {
        if (SelectedIndex >= 0)
        {
            _lastSelectedIndex = SelectedIndex;
        }
    }

    private void QueueSelectionRestore()
    {
        if (_restoreQueued)
        {
            return;
        }

        _restoreQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _restoreQueued = false;
                RestoreSelection();
            },
            DispatcherPriority.Loaded);
    }

    private void RestoreSelection()
    {
        if (!_isAttached
            || _lastSelectedIndex < 0
            || _lastSelectedIndex >= ItemCount)
        {
            return;
        }

        var index = _lastSelectedIndex;
        _restoring = true;
        try
        {
            if (SelectedIndex != index)
            {
                SelectedIndex = index;
            }

            // SelectedIndex may already match while a recycled container still has
            // IsSelected=false. Reassert the item property so the standard ListBox
            // pseudo-class is rebuilt immediately after a page transition.
            if (ContainerFromIndex(index) is ListBoxItem item && !item.IsSelected)
            {
                item.IsSelected = true;
            }
        }
        finally
        {
            _restoring = false;
        }
    }
}
