using System.Windows;
using System.Windows.Controls;
using WinNavi.Dock.Models;
using WinNavi.Dock.Services;
using WinForms = System.Windows.Forms;

namespace WinNavi.Dock;

public partial class SettingsWindow : Window
{
    // A selectable monitor; DeviceName "" means "the primary monitor" (matches DockSettings.Monitor's convention).
    record MonitorOption(string Label, string DeviceName);

    readonly DockSettings _settings;
    readonly Action _changed;
    readonly Action _addSeparator;
    readonly Action<bool> _setAutoHide;
    readonly Action<bool> _setReserve;
    readonly Action<string> _setMonitor;
    readonly Action<string> _backup;
    readonly Func<string, string?> _restore; // returns an error message, or null on success
    bool _ready;

    public SettingsWindow(DockSettings settings, Action changed, Action addSeparator,
        Action<bool> setAutoHide, Action<bool> setReserve, Action<string> setMonitor,
        Action<string> backup, Func<string, string?> restore)
    {
        InitializeComponent();
        _settings = settings;
        _changed = changed;
        _addSeparator = addSeparator;
        _setAutoHide = setAutoHide;
        _setReserve = setReserve;
        _setMonitor = setMonitor;
        _backup = backup;
        _restore = restore;
        SizeSlider.Minimum = DockLayout.MinIconSize;
        SizeSlider.Maximum = DockLayout.MaxIconSize;
        Reload();
    }

    // Controls follow the settings object; only user edits after this count as changes.
    void Reload()
    {
        _ready = false;
        PopulateMonitors();
        CbAutoHide.IsChecked = _settings.AutoHide;
        CbReserve.IsChecked = _settings.ReserveSpace;
        (_settings.Position switch
        {
            DockEdge.Top => RbTop,
            DockEdge.Left => RbLeft,
            DockEdge.Right => RbRight,
            _ => RbBottom,
        }).IsChecked = true;
        SizeSlider.Value = DockLayout.ClampSize(_settings.IconSize);
        SizeLabel.Text = $"{Math.Round(SizeSlider.Value)} px";
        OpacitySlider.Value = _settings.BackgroundOpacity * 100;
        OpacityLabel.Text = $"{Math.Round(OpacitySlider.Value)}%";
        RemoveDistSlider.Value = _settings.RemoveDragDistance;
        RemoveDistLabel.Text = $"{Math.Round(RemoveDistSlider.Value)} px";
        _ready = true;
    }

    // Lists every connected monitor so the dock can be placed on a secondary one, not just the primary.
    void PopulateMonitors()
    {
        var options = WinForms.Screen.AllScreens.Select(s => new MonitorOption(
            $"{s.DeviceName.TrimStart('\\', '.')}{(s.Primary ? " (주 모니터)" : "")}  {s.Bounds.Width}x{s.Bounds.Height}",
            s.Primary ? "" : s.DeviceName)).ToList();
        MonitorCombo.ItemsSource = options;
        MonitorCombo.SelectedItem = options.FirstOrDefault(o => o.DeviceName == _settings.Monitor) ?? options[0];
    }

    void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || MonitorCombo.SelectedItem is not MonitorOption opt) return;
        _setMonitor(opt.DeviceName);
    }

    void Position_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.Position =
            sender == RbTop ? DockEdge.Top :
            sender == RbLeft ? DockEdge.Left :
            sender == RbRight ? DockEdge.Right : DockEdge.Bottom;
        _changed();
    }

    void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        SizeLabel.Text = $"{Math.Round(e.NewValue)} px";
        _settings.IconSize = Math.Round(e.NewValue);
        _changed();
    }

    void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        OpacityLabel.Text = $"{Math.Round(e.NewValue)}%";
        _settings.BackgroundOpacity = e.NewValue / 100;
        _changed();
    }

    void RemoveDist_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        RemoveDistLabel.Text = $"{Math.Round(e.NewValue)} px";
        _settings.RemoveDragDistance = Math.Round(e.NewValue);
        _changed();
    }

    void AutoHide_Click(object sender, RoutedEventArgs e) => _setAutoHide(CbAutoHide.IsChecked == true);
    void Reserve_Click(object sender, RoutedEventArgs e) => _setReserve(CbReserve.IsChecked == true);

    void AddSeparator_Click(object sender, RoutedEventArgs e) => _addSeparator();

    // ---- backup / restore ----

    const string FileFilter = "WinNavi 백업 파일 (*.json)|*.json|모든 파일 (*.*)|*.*";

    void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "설정 백업",
            Filter = FileFilter,
            DefaultExt = ".json",
            FileName = $"WinNavi-백업-{DateTime.Now:yyyyMMdd-HHmm}.json",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _backup(dlg.FileName);
            MessageBox.Show(this, $"백업했습니다.\n{dlg.FileName}", "백업",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"백업하지 못했습니다.\n{ex.Message}", "백업",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "설정 복원",
            Filter = FileFilter,
            DefaultExt = ".json",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        var ok = MessageBox.Show(this,
            "현재 등록된 아이콘과 설정이 백업 파일의 내용으로 바뀝니다.\n계속할까요?", "복원",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (ok != MessageBoxResult.Yes) return;

        var error = _restore(dlg.FileName);
        if (error != null)
        {
            MessageBox.Show(this, error, "복원", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Reload();
        MessageBox.Show(this, "복원했습니다.", "복원", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
