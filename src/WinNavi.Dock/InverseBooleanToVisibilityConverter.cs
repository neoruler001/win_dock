using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinNavi.Dock;

// Same as BooleanToVisibilityConverter but inverted: true -> Collapsed, false -> Visible.
// Used to show/hide context menu items based on a DockItem flag (e.g. hide "편집..." for separators).
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
