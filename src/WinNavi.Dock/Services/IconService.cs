using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNavi.Dock.Native;

namespace WinNavi.Dock.Services;

public static class IconService
{
    const string ShellPrefix = "shell:";

    public static bool IsShellPath(string path) =>
        path.StartsWith(ShellPrefix, StringComparison.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string path)
    {
        try
        {
            var target = IsShellPath(path) ? path : Resolve(path);
            if (target == null) return null;
            return FromShellItem(target) ?? FromFileInfo(target);
        }
        catch (Exception) { return null; }
    }

    // 256px icon from the shell; also works for UWP apps ("shell:AppsFolder\<AppUserModelId>").
    static ImageSource? FromShellItem(string target)
    {
        Win32.IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(Win32.IShellItemImageFactory).GUID;
            Win32.SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out factory);
            int hr = factory.GetImage(new Win32.SIZE { cx = 256, cy = 256 }, Win32.SIIGBF_ICONONLY, out var hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return TrimTransparent(src);
            }
            finally { Win32.DeleteObject(hbm); }
        }
        catch (Exception) { return null; }
        finally { if (factory != null) Marshal.ReleaseComObject(factory); }
    }

    static ImageSource? FromFileInfo(string full)
    {
        var info = new Win32.SHFILEINFO();
        Win32.SHGetFileInfo(full, 0, ref info, (uint)Marshal.SizeOf<Win32.SHFILEINFO>(),
            Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return TrimTransparent(src);
        }
        finally { Win32.DestroyIcon(info.hIcon); }
    }

    // Icons come with different amounts of transparent padding (a 48px glyph centered in a 256px canvas, etc.),
    // so the same 48px slot shows them at different sizes. Cropping to the visible pixels makes them all fill it.
    // Some icons carry a faint (alpha < 128) layer over the whole 256px canvas around a 48px glyph, so the
    // cutoff has to be well above "any alpha"; antialiased edges below it are lost, which is harmless.
    const byte VisibleAlpha = 128;

    static BitmapSource TrimTransparent(BitmapSource src)
    {
        BitmapSource bgra = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * stride + x * 4 + 3] > VisibleAlpha)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

        if (maxX < 0) return src; // nothing visible; leave it alone
        int cw = maxX - minX + 1, ch = maxY - minY + 1;
        if (cw == w && ch == h) return src;

        var cropped = new CroppedBitmap(bgra, new Int32Rect(minX, minY, cw, ch));
        cropped.Freeze();
        return cropped;
    }

    // Bare names like "notepad.exe" are looked up on PATH. Shell paths have no file to resolve.
    public static string? Resolve(string path)
    {
        if (IsShellPath(path)) return null;
        if (File.Exists(path) || Directory.Exists(path)) return path;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = System.IO.Path.Combine(dir, path);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    static readonly Dictionary<string, string?> ShortcutTargetCache = new(StringComparer.OrdinalIgnoreCase);

    // The .lnk's own target exe, read via IShellLinkW. Shortcuts don't change target while the dock runs,
    // so results are cached by .lnk path.
    public static string? ResolveShortcutTarget(string lnkPath)
    {
        if (ShortcutTargetCache.TryGetValue(lnkPath, out var cached)) return cached;

        string? target = null;
        object? com = null;
        try
        {
            com = new Win32.ShellLink();
            ((Win32.IPersistFile)com).Load(lnkPath, 0 /* STGM_READ */);
            var sb = new StringBuilder(260);
            ((Win32.IShellLinkW)com).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            if (sb.Length > 0) target = sb.ToString();
        }
        catch (Exception) { }
        finally { if (com != null) Marshal.ReleaseComObject(com); }

        ShortcutTargetCache[lnkPath] = target;
        return target;
    }

    // Resolves a path to compare against a running process's exe path: a .lnk becomes its target exe,
    // bare names are looked up on PATH same as Resolve(). Icon extraction keeps using Resolve() directly,
    // since a shortcut's own icon can be customized to differ from its target's - this is only for the
    // "is this already running" check.
    public static string? ResolveRunMatchPath(string path)
    {
        var full = Resolve(path);
        if (full != null && full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return ResolveShortcutTarget(full) ?? full;
        return full;
    }
}
