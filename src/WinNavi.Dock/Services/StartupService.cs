using Microsoft.Win32;

namespace WinNavi.Dock.Services;

public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WinNaviDock";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (exe != null) key.SetValue(ValueName, $"\"{exe}\"");
        }
        else key.DeleteValue(ValueName, false);
    }
}
