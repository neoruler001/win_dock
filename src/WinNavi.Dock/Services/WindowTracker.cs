using System.Text;
using WinNavi.Dock.Native;

namespace WinNavi.Dock.Services;

// Finds which executables currently own a visible top-level app window, and can bring one to the front.
public static class WindowTracker
{
    public static HashSet<string> GetRunningExePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pids = new HashSet<uint>();

        Win32.EnumWindows((hWnd, _) =>
        {
            if (IsUserWindow(hWnd))
            {
                Win32.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != 0) pids.Add(pid);
            }
            return true;
        }, IntPtr.Zero);

        foreach (var pid in pids)
        {
            var path = GetImagePath(pid);
            if (path != null) paths.Add(path);
        }
        return paths;
    }

    // Brings an existing window of the given exe to the foreground instead of starting a new instance.
    // Used for the dock's auto-shown "currently running" icons. Returns false if no window was found.
    public static bool TryActivate(string exePath)
    {
        IntPtr found = IntPtr.Zero;
        Win32.EnumWindows((hWnd, _) =>
        {
            if (!IsUserWindow(hWnd)) return true;
            Win32.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;
            var path = GetImagePath(pid);
            if (path == null || !string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)) return true;
            found = hWnd;
            return false; // stop enumerating
        }, IntPtr.Zero);

        if (found == IntPtr.Zero) return false;
        if (Win32.IsIconic(found)) Win32.ShowWindow(found, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(found);
        return true;
    }

    // Visible top-level window with a title, not a tool window, not owned by another window, not cloaked
    // (a suspended UWP window stays "visible" but cloaked).
    static bool IsUserWindow(IntPtr hWnd)
    {
        if (!Win32.IsWindowVisible(hWnd)) return false;
        if (Win32.GetWindowTextLength(hWnd) == 0) return false;
        if ((Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0) return false;
        if (Win32.GetWindow(hWnd, Win32.GW_OWNER) != IntPtr.Zero) return false;
        if (Win32.DwmGetWindowAttribute(hWnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        return true;
    }

    static string? GetImagePath(uint pid)
    {
        var h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return Win32.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { Win32.CloseHandle(h); }
    }
}
