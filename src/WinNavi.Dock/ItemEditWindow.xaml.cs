using System.IO;
using System.Windows;
using WinNavi.Dock.Models;

namespace WinNavi.Dock;

public partial class ItemEditWindow : Window
{
    public string ItemName { get; private set; } = "";
    public string ItemPath { get; private set; } = "";
    public string ItemArgs { get; private set; } = "";
    public bool RunAsAdmin { get; private set; }

    public ItemEditWindow(DockItem item)
    {
        InitializeComponent();
        NameBox.Text = item.Name;
        PathBox.Text = item.Path;
        ArgsBox.Text = item.Args;
        AdminBox.IsChecked = item.RunAsAdmin;
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "실행 파일 선택",
            Filter = "실행 파일 (*.exe;*.lnk)|*.exe;*.lnk|모든 파일 (*.*)|*.*",
        };
        if (PathBox.Text.Length > 0 && File.Exists(PathBox.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(PathBox.Text);
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FileName;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathBox.Text))
        {
            MessageBox.Show(this, "경로를 입력하세요.", "항목 편집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ItemPath = PathBox.Text.Trim();
        ItemName = string.IsNullOrWhiteSpace(NameBox.Text)
            ? Path.GetFileNameWithoutExtension(ItemPath)
            : NameBox.Text.Trim();
        ItemArgs = ArgsBox.Text.Trim();
        RunAsAdmin = AdminBox.IsChecked == true;
        DialogResult = true;
    }
}
