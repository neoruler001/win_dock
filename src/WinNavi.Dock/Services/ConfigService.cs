using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinNavi.Dock.Models;

namespace WinNavi.Dock.Services;

public class DockSettings
{
    public bool AutoHide { get; set; }

    // true = reserve a screen strip (AppBar) so other windows don't overlap the dock; ignored while AutoHide.
    public bool ReserveSpace { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DockEdge Position { get; set; } = DockEdge.Bottom;

    // Icon edge length in DIPs, DockLayout.MinIconSize..MaxIconSize.
    public double IconSize { get; set; } = DockLayout.DefaultIconSize;

    // 0 (invisible bar, icons only) .. 1 (opaque).
    public double BackgroundOpacity { get; set; } = 0.6;

    // Screen.DeviceName of the monitor to dock on; empty = primary.
    public string Monitor { get; set; } = "";
}

public class DockConfig
{
    public List<DockItem> Items { get; set; } = new();
    public DockSettings Settings { get; set; } = new();
}

public static class ConfigService
{
    // Portable mode: a "portable.txt" next to the exe keeps settings in <exe folder>\data instead of %AppData%,
    // so the whole folder can be carried on a USB stick.
    public static bool IsPortable { get; } =
        File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    static readonly string FilePath = System.IO.Path.Combine(
        IsPortable
            ? System.IO.Path.Combine(AppContext.BaseDirectory, "data")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNavi"),
        "dock.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static DockConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = Parse(File.ReadAllText(FilePath));
                if (cfg != null) return cfg;
            }
        }
        catch (Exception) { }
        return new DockConfig { Items = Defaults() };
    }

    public static void Save(DockConfig config)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Read-only media (portable on a locked drive etc.): keep running, settings just won't persist.
        }
    }

    // Backup file = the same JSON as dock.json (icons + all settings).
    public static void Export(DockConfig config, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));

    // Returns an error message (Korean, shown to the user) or null on success.
    public static string? TryRead(string path, out DockConfig? config)
    {
        config = null;
        try
        {
            config = Parse(File.ReadAllText(path));
            return config == null ? "올바른 백업 파일이 아닙니다." : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return e is JsonException ? "올바른 백업 파일이 아닙니다." : $"파일을 읽지 못했습니다.\n{e.Message}";
        }
    }

    static DockConfig? Parse(string text)
    {
        DockConfig? cfg;
        // Phase 1-3 format: bare array of items.
        if (text.TrimStart().StartsWith('['))
            cfg = new DockConfig { Items = JsonSerializer.Deserialize<List<DockItem>>(text) ?? new() };
        else
            cfg = JsonSerializer.Deserialize<DockConfig>(text);
        if (cfg == null) return null;
        cfg.Items ??= new();
        cfg.Settings ??= new();
        cfg.Items.RemoveAll(i => !i.IsSeparator && string.IsNullOrWhiteSpace(i.Path));
        return cfg;
    }

    static List<DockItem> Defaults() => new()
    {
        new() { Name = "Explorer", Path = "explorer.exe" },
        new() { Name = "Notepad", Path = "notepad.exe" },
        new() { Name = "Calculator", Path = "calc.exe" },
    };
}
