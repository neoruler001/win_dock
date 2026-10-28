using WinForms = System.Windows.Forms;

namespace WinNavi.Dock.Services;

public sealed class TrayService : IDisposable
{
    readonly WinForms.NotifyIcon _icon;

    public TrayService(DockSettings settings, Action addApp, Action showSettings, Action<bool> setAutoHide,
        Action<bool> setReserve, Action<string> setMonitor, Action exit)
    {
        var menu = new WinForms.ContextMenuStrip();

        menu.Items.Add("앱 추가...", null, (_, _) => addApp());
        menu.Items.Add("설정...", null, (_, _) => showSettings());

        // The settings window can change these too, so the check marks are re-read each time the menu opens.
        var autoHide = new WinForms.ToolStripMenuItem("자동 숨김");
        autoHide.Click += (_, _) => setAutoHide(!settings.AutoHide);
        menu.Items.Add(autoHide);

        var reserve = new WinForms.ToolStripMenuItem("화면 영역 예약");
        reserve.Click += (_, _) => setReserve(!settings.ReserveSpace);
        menu.Items.Add(reserve);

        menu.Opening += (_, _) =>
        {
            autoHide.Checked = settings.AutoHide;
            reserve.Checked = settings.ReserveSpace;
        };

        var monitor = new WinForms.ToolStripMenuItem("모니터");
        monitor.DropDownOpening += (_, _) =>
        {
            monitor.DropDownItems.Clear();
            foreach (var s in WinForms.Screen.AllScreens)
            {
                var name = s.DeviceName;
                bool current = settings.Monitor == name || (settings.Monitor == "" && s.Primary);
                var item = new WinForms.ToolStripMenuItem(
                    $"{name.TrimStart('\\', '.')}{(s.Primary ? " (주 모니터)" : "")}  {s.Bounds.Width}x{s.Bounds.Height}")
                { Checked = current };
                item.Click += (_, _) => setMonitor(s.Primary ? "" : name);
                monitor.DropDownItems.Add(item);
            }
        };
        monitor.DropDownItems.Add("-"); // placeholder so the submenu arrow shows
        menu.Items.Add(monitor);

        var startup = new WinForms.ToolStripMenuItem("Windows 시작 시 실행")
        {
            CheckOnClick = true,
            Checked = StartupService.IsEnabled(),
        };
        startup.CheckedChanged += (_, _) => StartupService.SetEnabled(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "WinNavi Dock",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    // The exe carries Assets\app.ico as its icon.
    static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null)
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch (Exception) { }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
