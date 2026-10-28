using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinNavi.Dock.Services;

namespace WinNavi.Dock;

public partial class AppPickerWindow : Window
{
    List<StartApp> _all = new();

    public StartApp? Selected { get; private set; }

    public AppPickerWindow()
    {
        InitializeComponent();
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _all = await Task.Run(StartAppsService.Query);
        Title = "앱 추가";
        ApplyFilter();
        Filter.Focus();
    }

    void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    void ApplyFilter()
    {
        var q = Filter.Text.Trim();
        List.ItemsSource = q.Length == 0
            ? _all
            : _all.Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    void List_DoubleClick(object sender, MouseButtonEventArgs e) => Accept();
    void Add_Click(object sender, RoutedEventArgs e) => Accept();

    void Accept()
    {
        if (List.SelectedItem is not StartApp app) return;
        Selected = app;
        DialogResult = true;
    }
}
