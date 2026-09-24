using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OnePointUI.Avalonia.Base.Entry;

namespace OnePointUI.Avalonia.Styling.Controls.OnePointControls.Navigation.Breadcrumb;

public partial class BreadcrumbBar : UserControl
{
    private string _rootItem = "Item 1";

    public BreadcrumbBar()
    {
        InitializeComponent();
    }

    public Action? RootItemClick { get; set; }

    public string RootItem
    {
        get => _rootItem;
        set
        {
            _rootItem = value;
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        RootItemBox.Content = RootItem;
    }

    public void SetItems(List<BreadcrumbItemInfo> items)
    {
        ItemsBar.Children.Clear();

        foreach (var item in items)
        {
            var newTtem = new Button
            {
                Classes = { "NoBorder" },
                Content = item.ItemName,
                FontSize = 24,
                Margin = new Thickness(8, 0),
                Padding = new Thickness(8, 4)
            };

            var newItemIcon = new FontIcon
            {
                Glyph = "\uE76C",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 18
                // Foreground = GetFontColorResourceFromApp()
            };

            newTtem.Click += (_, __) =>
            {
                item.ItemClickAction?.Invoke(item);
            };

            ItemsBar.Children.Add(newItemIcon);
            ItemsBar.Children.Add(newTtem);
        }
    }

    private void RootItemBox_OnClick(object? sender, RoutedEventArgs e)
    {
        RootItemClick?.Invoke();
        ItemsBar.Children.Clear();
    }
}
