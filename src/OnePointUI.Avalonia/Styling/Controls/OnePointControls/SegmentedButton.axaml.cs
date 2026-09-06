using Avalonia.Controls;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls;

public class SegmentedButton : ListBox
{
    protected override bool NeedsContainerOverride(
        object? item,
        int index,
        out object? recycleKey) =>
        NeedsContainer<SegmentedButtonItem>(item, out recycleKey);

    protected override Control CreateContainerForItemOverride(
        object? item,
        int index,
        object? recycleKey) =>
        new SegmentedButtonItem();
}

public class SegmentedButtonItem : ListBoxItem
{
}
