using System.Windows;
using System.Windows.Controls;
using WinNavi.Dock.Models;

namespace WinNavi.Dock;

public class DockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Item { get; set; }
    public DataTemplate? Separator { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is DockItem { IsSeparator: true } ? Separator : Item;
}
