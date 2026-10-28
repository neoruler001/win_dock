using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinNavi.Dock.Models;
using WinNavi.Dock.Native;

namespace WinNavi.Dock.Services;

// Reserves a strip at the bottom edge of one monitor so maximized windows stop above the dock.
// Unregistered (auto-hide) it only reports where the dock would sit: bottom of the work area.
internal sealed class AppBarService : IDisposable
{
    readonly IntPtr _hwnd;
    readonly uint _callbackMsg;
    readonly HwndSource? _source;
    bool _registered;

    // Dock strip in physical pixels, valid after Update().
    public Win32.RECT Placement { get; private set; }

    public bool IsRegistered => _registered;

    // Raised when the system moved the reserved strip (taskbar change, resolution change...).
    public event Action? Changed;

    public AppBarService(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _callbackMsg = Win32.RegisterWindowMessage("WinNavi.Dock.AppBar");
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    public void Register()
    {
        if (_registered) return;
        var data = NewData();
        data.uCallbackMessage = _callbackMsg;
        Win32.SHAppBarMessage(Win32.ABM_NEW, ref data);
        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered) return;
        var data = NewData();
        Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref data);
        _registered = false;
    }

    // Screen edge the strip hugs. Changing it while registered needs Unregister() + Register().
    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    // thicknessPx: strip depth perpendicular to the edge.
    public void Update(Win32.RECT bounds, Win32.RECT workArea, int thicknessPx)
    {
        // Registered: strip hugs the full monitor edge and the system shifts it past other bars (taskbar).
        // Unregistered: nothing is reserved, so sit at the edge of the current work area.
        var area = _registered ? bounds : workArea;
        var rc = Strip(area, thicknessPx);
        if (!_registered)
        {
            Placement = rc;
            return;
        }

        var data = NewData();
        data.uEdge = EdgeCode();
        data.rc = rc;
        Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref data);
        data.rc = Strip(data.rc, thicknessPx); // QUERYPOS may have moved one side; restore the depth
        Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref data);
        Placement = data.rc;
    }

    // The strip of the given depth along the Edge side of r, anchored to that side.
    Win32.RECT Strip(Win32.RECT r, int t) => Edge switch
    {
        DockEdge.Top => r with { Bottom = r.Top + t },
        DockEdge.Left => r with { Right = r.Left + t },
        DockEdge.Right => r with { Left = r.Right - t },
        _ => r with { Top = r.Bottom - t },
    };

    uint EdgeCode() => Edge switch
    {
        DockEdge.Top => Win32.ABE_TOP,
        DockEdge.Left => Win32.ABE_LEFT,
        DockEdge.Right => Win32.ABE_RIGHT,
        _ => Win32.ABE_BOTTOM,
    };

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_registered) return IntPtr.Zero;

        if (msg == _callbackMsg)
        {
            if (wParam.ToInt32() == Win32.ABN_POSCHANGED)
                Changed?.Invoke();
        }
        else if (msg == Win32.WM_ACTIVATE)
        {
            var data = NewData();
            Win32.SHAppBarMessage(Win32.ABM_ACTIVATE, ref data);
        }
        else if (msg == Win32.WM_WINDOWPOSCHANGED)
        {
            var data = NewData();
            Win32.SHAppBarMessage(Win32.ABM_WINDOWPOSCHANGED, ref data);
        }
        return IntPtr.Zero;
    }

    Win32.APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32.APPBARDATA>(),
        hWnd = _hwnd,
    };

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
    }
}
