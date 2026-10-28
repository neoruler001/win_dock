using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WinNavi.Dock.Services;

public record StartApp(string Name, string AppId);

// Lists Start-menu apps (classic and UWP) through PowerShell's Get-StartApps.
public static class StartAppsService
{
    const string Script =
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
        "@(Get-StartApps | Select-Object Name,AppID) | ConvertTo-Json -Compress";

    public static List<StartApp> Query()
    {
        var result = new List<StartApp>();
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", Script },
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return result;
            var json = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var name = e.GetProperty("Name").GetString();
                var id = e.GetProperty("AppID").GetString();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                    result.Add(new StartApp(name, id));
            }
        }
        catch (Exception) { }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
