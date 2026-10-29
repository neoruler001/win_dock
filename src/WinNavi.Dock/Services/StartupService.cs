using Microsoft.Win32;

namespace WinNavi.Dock.Services;

// Registers/unregisters the currently running exe (installed copy or a portable copy run from any folder)
// in the per-user Run key. Path-aware so an installed copy and a portable copy don't clobber each other's
// registration: toggling "off" only removes the key if it still points at THIS process's exe.
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WinNaviDock";

    public static bool IsEnabled()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return false;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return PathEquals(key?.GetValue(ValueName) as string, exe);
    }

    public static void SetEnabled(bool enabled)
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else if (PathEquals(key.GetValue(ValueName) as string, exe))
        {
            key.DeleteValue(ValueName, false);
        }
        // else: the key points at a different copy (e.g. the installed exe, while running portable) -
        // leave that registration alone.
    }

    static bool PathEquals(string? registeredCommand, string exePath) =>
        registeredCommand != null &&
        string.Equals(registeredCommand.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
}
