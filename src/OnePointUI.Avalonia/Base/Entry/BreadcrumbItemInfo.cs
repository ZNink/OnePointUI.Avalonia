namespace OnePointUI.Avalonia.Base.Entry;

public class BreadcrumbItemInfo
{
    public string ItemName { get; set; } = string.Empty;
    public Action<BreadcrumbItemInfo>? ItemClickAction { get; set; }
}
