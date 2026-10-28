using System.Diagnostics;
using WinNavi.Dock.Models;

namespace WinNavi.Dock.Services;

public static class LaunchService
{
    public static void Launch(DockItem item)
    {
        try
        {
            var psi = new ProcessStartInfo(item.Path, item.Args) { UseShellExecute = true };
            if (item.RunAsAdmin) psi.Verb = "runas"; // triggers the UAC prompt
            Process.Start(psi);
        }
        catch (Exception) { } // includes the user declining the UAC prompt
    }
}
