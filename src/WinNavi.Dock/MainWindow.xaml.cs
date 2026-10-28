using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinNavi.Dock.Models;
using WinNavi.Dock.Native;
using WinNavi.Dock.Services;
using WinForms = System.Windows.Forms;

namespace WinNavi.Dock;

public partial class MainWindow : Window
{
    const double MaxZoom = 1.8;
    const double EdgeMargin = 8;      // gap between the dock bar and the screen edge
    const double TriggerDepth = 16;    // auto-hide: hover strip depth at the dock's edge, in DIPs
    const int HideDelayMs = 400;

    public static readonly DependencyProperty LayoutInfoProperty = DependencyProperty.Register(
        nameof(LayoutInfo), typeof(DockLayout), typeof(MainWindow),
        new PropertyMetadata(DockLayout.For(DockEdge.Bottom, DockLayout.DefaultIconSize)));

    public DockLayout LayoutInfo
    {
        get => (DockLayout)GetValue(LayoutInfoProperty);
        private set => SetValue(LayoutInfoProperty, value);
    }

    readonly DockConfig _config = ConfigService.Load();
    readonly ObservableCollection<DockItem> _items;
    readonly Dictionary<DockItem, (ScaleTransform S, TranslateTransform T)> _xf = new();
    // Running apps shown automatically (not pinned by the user), keyed by their exe's full path.
    readonly Dictionary<string, DockItem> _transient = new(StringComparer.OrdinalIgnoreCase);
    double[] _target = [];
    double[] _cur = [];
    bool _rendering;

    IntPtr _hwnd;
    AppBarService? _appBar;
    TrayService? _tray;
    SettingsWindow? _settings;
    DispatcherTimer? _runTimer;
    DispatcherTimer? _edgeTimer;

    // Monitor the dock currently sits on; refreshed by Reposition().
    WinForms.Screen? _screen;
    double _scale = 1; // monitor DPI / 96
    bool _repositioning;

    // Auto-hide slide: 0 = shown, 1 = fully outside the screen edge.
    double _slide;
    double _slideTarget;
    bool _sliding;
    DateTime? _outSince;
    int _menuOpen;

    Point _dragStart;
    DockItem? _dragItem;
    bool _dragged;

    double IconSize => DockLayout.ClampSize(_config.Settings.IconSize);
    double Slot => IconSize + 12;                  // icon + 6px margin on each side along the main axis
    double Radius => IconSize * 3;                 // how far from the pointer icons still grow
    double Headroom => IconSize * (MaxZoom - 1) + 6; // transparent room on the inner side for zoomed icons

    DockEdge Edge => _config.Settings.Position;
    bool Vertical => Edge is DockEdge.Left or DockEdge.Right;
    bool AutoHide => _config.Settings.AutoHide;
    // Overlay by default: only reserve space (shrinking other windows) when asked, and never while auto-hiding.
    bool ShouldReserve => _config.Settings.ReserveSpace && !AutoHide;

    public MainWindow()
    {
        LayoutInfo = DockLayout.For(Edge, IconSize);
        InitializeComponent();
        _items = new ObservableCollection<DockItem>(_config.Items);
        foreach (var item in _items)
            if (!item.IsSeparator) item.Icon = IconService.GetIcon(item.Path);
        ResetZoom();
        Items.ItemsSource = _items;
        _items.CollectionChanged += Items_Changed;
        _slide = _slideTarget = AutoHide ? 1 : 0;
        _appliedEdge = Edge;
        _appliedSize = IconSize;
        ApplyEdgeLayout();
        ApplyAppearance();
        Closed += Window_Closed;
    }

    void Items_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null && e.Action == NotifyCollectionChangedAction.Remove)
            foreach (DockItem old in e.OldItems) _xf.Remove(old);
        ResetZoom();
        SaveConfig();
    }

    void SaveConfig()
    {
        _config.Items = _items.Where(i => !i.IsTemporary).ToList();
        ConfigService.Save(_config);
    }

    void ResetZoom()
    {
        _target = Enumerable.Repeat(1.0, _items.Count).ToArray();
        _cur = Enumerable.Repeat(1.0, _items.Count).ToArray();
    }

    // ---- lifetime ----

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        _appBar = new AppBarService(this) { Edge = Edge };
        _appBar.Changed += Reposition;
        if (ShouldReserve) _appBar.Register();
        Reposition();

        _tray = new TrayService(_config.Settings, ShowAppPicker, ShowSettings, SetAutoHide, SetReserve,
            SetMonitor, () => Application.Current.Shutdown());

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _runTimer.Tick += (_, _) => RefreshRunning();
        _runTimer.Start();
        RefreshRunning();

        _edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _edgeTimer.Tick += (_, _) => EdgeTick();
        if (AutoHide) _edgeTimer.Start();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    void Window_Closed(object? sender, EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _runTimer?.Stop();
        _edgeTimer?.Stop();
        _settings?.Close();
        _tray?.Dispose();
        _appBar?.Dispose();
    }

    void OnDisplayChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Reposition));

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    // ---- edge layout / appearance ----

    // Margins: EdgeMargin on the screen side, Headroom on the inner side (icons grow into it).
    void ApplyEdgeLayout()
    {
        LayoutInfo = DockLayout.For(Edge, IconSize);
        const double m = EdgeMargin;
        double headroom = Headroom;
        DockBar.Margin = Edge switch
        {
            DockEdge.Top => new Thickness(m, m, m, headroom),
            DockEdge.Left => new Thickness(m, m, headroom, m),
            DockEdge.Right => new Thickness(headroom, m, m, m),
            _ => new Thickness(m, headroom, m, m),
        };
        DockBar.Padding = Vertical ? new Thickness(8, 10, 8, 10) : new Thickness(10, 8, 10, 8);
    }

    void ApplyAppearance()
    {
        double o = Math.Clamp(_config.Settings.BackgroundOpacity, 0, 1);
        // Alpha stays >= 1: fully transparent pixels of a layered window let the mouse fall through.
        byte bg = (byte)Math.Max(1, Math.Round(255 * o));
        byte line = (byte)Math.Round(0x55 * o);
        DockBar.Background = new SolidColorBrush(Color.FromArgb(bg, 0x20, 0x20, 0x20));
        DockBar.BorderBrush = new SolidColorBrush(Color.FromArgb(line, 0xFF, 0xFF, 0xFF));
    }

    // ---- settings window ----

    void ShowSettings()
    {
        if (_settings != null)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_config.Settings, OnSettingsChanged, () => AddSeparatorAt(_items.Count),
            SetAutoHide, SetReserve, BackupTo, RestoreFrom);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    DockEdge _appliedEdge;
    double _appliedSize;

    void OnSettingsChanged()
    {
        ApplyAppearance();
        bool edgeChanged = _appliedEdge != Edge;
        bool sizeChanged = _appliedSize != IconSize;
        if (edgeChanged || sizeChanged)
        {
            _appliedEdge = Edge;
            _appliedSize = IconSize;
            ApplyEdgeLayout();
            if (edgeChanged && _appBar != null)
            {
                _appBar.Edge = Edge;
                if (_appBar.IsRegistered)
                {
                    _appBar.Unregister(); // the registered edge can't change in place
                    if (ShouldReserve) _appBar.Register();
                }
            }
            UpdateLayout();
            Reposition();
            SettleReposition();
        }
        SaveConfig();
    }

    // ---- backup / restore ----

    void BackupTo(string path)
    {
        _config.Items = _items.Where(i => !i.IsTemporary).ToList();
        ConfigService.Export(_config, path);
    }

    // Replaces icons and settings with the backup's. Returns an error message, or null on success.
    string? RestoreFrom(string path)
    {
        var error = ConfigService.TryRead(path, out var cfg);
        if (error != null || cfg == null) return error ?? "올바른 백업 파일이 아닙니다.";
        ApplyConfig(cfg);
        return null;
    }

    // Copies values into the existing settings object (the settings window holds a reference to it).
    void ApplyConfig(DockConfig cfg)
    {
        var s = _config.Settings;
        var n = cfg.Settings;
        s.Position = n.Position;
        s.BackgroundOpacity = n.BackgroundOpacity;
        s.IconSize = n.IconSize;
        s.Monitor = n.Monitor;
        s.AutoHide = n.AutoHide;
        s.ReserveSpace = n.ReserveSpace;

        _items.CollectionChanged -= Items_Changed;
        _items.Clear();
        _xf.Clear();
        _transient.Clear();
        foreach (var item in cfg.Items)
        {
            if (!item.IsSeparator) item.Icon = IconService.GetIcon(item.Path);
            _items.Add(item);
        }
        _items.CollectionChanged += Items_Changed;
        ResetZoom();

        if (AutoHide) _edgeTimer?.Start();
        else
        {
            _edgeTimer?.Stop();
            _outSince = null;
            SlideTo(0);
        }

        ApplyAppearance();
        _appliedEdge = Edge;
        _appliedSize = IconSize;
        ApplyEdgeLayout();
        if (_appBar != null)
        {
            _appBar.Edge = Edge;
            _appBar.Unregister(); // edge may differ; ApplyReserve registers again if needed
        }
        UpdateLayout();
        ApplyReserve();
        SaveConfig();
        RefreshRunning();
    }

    void Gear_Click(object sender, MouseButtonEventArgs e) => ShowSettings();
    void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    // ---- placement ----

    void Window_SizeChanged(object sender, SizeChangedEventArgs e) => Reposition();

    WinForms.Screen GetScreen() =>
        WinForms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _config.Settings.Monitor)
        ?? WinForms.Screen.PrimaryScreen
        ?? WinForms.Screen.AllScreens[0];

    double MonitorScale(WinForms.Screen s)
    {
        var b = s.Bounds;
        var pt = new Win32.POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 };
        var mon = Win32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero && Win32.GetDpiForMonitor(mon, 0, out uint dpiX, out _) == 0)
            return dpiX / 96.0;
        return VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    static Win32.RECT ToRect(System.Drawing.Rectangle r) =>
        new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    // Reserved strip depth = dock bar + the gap to the screen edge; the transparent zoom headroom is not reserved.
    void Reposition()
    {
        if (_appBar == null || _repositioning || ActualHeight == 0) return;
        _repositioning = true;
        try
        {
            _screen = GetScreen();
            _scale = MonitorScale(_screen);
            double barDepth = Vertical ? DockBar.ActualWidth : DockBar.ActualHeight;
            int tPx = (int)Math.Ceiling((barDepth + EdgeMargin) * _scale);
            _appBar.Update(ToRect(_screen.Bounds), ToRect(_screen.WorkingArea), tPx);
            Place();
        }
        finally { _repositioning = false; }
    }

    // The work area updates asynchronously after ABM_REMOVE/NEW; settle once more.
    void SettleReposition()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) => { t.Stop(); Reposition(); };
        t.Start();
    }

    // Window position in physical pixels when shown (X,Y) and when slid out (HX,HY), plus its pixel size.
    (int X, int Y, int HX, int HY, int W, int H) Geometry()
    {
        var r = _appBar!.Placement;
        var b = _screen!.Bounds;
        int w = (int)Math.Ceiling(ActualWidth * _scale);
        int h = (int)Math.Ceiling(ActualHeight * _scale);
        int cx = r.Left + ((r.Right - r.Left) - w) / 2; // centered along the edge
        int cy = r.Top + ((r.Bottom - r.Top) - h) / 2;
        return Edge switch
        {
            DockEdge.Top => (cx, r.Top, cx, b.Top - h, w, h),
            DockEdge.Left => (r.Left, cy, b.Left - w, cy, w, h),
            DockEdge.Right => (r.Right - w, cy, b.Right, cy, w, h),
            _ => (cx, r.Bottom - h, cx, b.Bottom, w, h),
        };
    }

    // Window is placed in physical pixels so it lands right on monitors with different DPI.
    void Place()
    {
        if (_appBar == null || _screen == null || _hwnd == IntPtr.Zero) return;
        var g = Geometry();
        int x = (int)Math.Round(g.X + _slide * (g.HX - g.X));
        int y = (int)Math.Round(g.Y + _slide * (g.HY - g.Y));
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    // ---- reserve / auto-hide / monitor ----

    void SetReserve(bool on)
    {
        _config.Settings.ReserveSpace = on;
        SaveConfig();
        ApplyReserve();
    }

    void ApplyReserve()
    {
        if (ShouldReserve) _appBar?.Register();
        else _appBar?.Unregister(); // give the strip back to other windows
        Reposition();
        SettleReposition();
    }

    void SetAutoHide(bool on)
    {
        _config.Settings.AutoHide = on;
        SaveConfig();
        if (on)
        {
            _edgeTimer?.Start();
        }
        else
        {
            _edgeTimer?.Stop();
            _outSince = null;
            SlideTo(0);
        }
        ApplyReserve();
    }

    void SetMonitor(string deviceName)
    {
        _config.Settings.Monitor = deviceName;
        SaveConfig();
        Reposition();
    }

    // Auto-hide: hovering the strip where the dock sits slides it in; leaving it slides it out.
    void EdgeTick()
    {
        if (_screen == null || _appBar == null || !Win32.GetCursorPos(out var pt)) return;
        bool inZone = InTriggerZone(pt);

        if (_slideTarget >= 1)
        {
            if (inZone) SlideTo(0);
            return;
        }

        if (inZone || _menuOpen > 0 || IsOverDock(pt))
        {
            _outSince = null;
            return;
        }
        _outSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - _outSince.Value).TotalMilliseconds > HideDelayMs)
        {
            SlideTo(1);
            _outSince = null;
        }
    }

    // Thin strip at the dock's edge, as long as the dock bar is.
    bool InTriggerZone(Win32.POINT pt)
    {
        var g = Geometry();
        var r = _appBar!.Placement;
        double k = _scale;
        double barL = g.X + DockBar.Margin.Left * k;
        double barT = g.Y + DockBar.Margin.Top * k;
        double barR = barL + DockBar.ActualWidth * k;
        double barB = barT + DockBar.ActualHeight * k;
        int d = (int)Math.Ceiling(TriggerDepth * k);
        bool alongX = pt.X >= barL && pt.X <= barR;
        bool alongY = pt.Y >= barT && pt.Y <= barB;
        return Edge switch
        {
            DockEdge.Top => alongX && pt.Y >= r.Top && pt.Y <= r.Top + d,
            DockEdge.Left => alongY && pt.X >= r.Left && pt.X <= r.Left + d,
            DockEdge.Right => alongY && pt.X >= r.Right - d && pt.X <= r.Right,
            _ => alongX && pt.Y >= r.Bottom - d && pt.Y <= r.Bottom,
        };
    }

    bool IsOverDock(Win32.POINT pt)
    {
        const double pad = 8;
        var tl = DockBar.PointToScreen(new Point(0, 0));
        double w = DockBar.ActualWidth * _scale;
        double h = DockBar.ActualHeight * _scale;
        return pt.X >= tl.X - pad && pt.X <= tl.X + w + pad &&
               pt.Y >= tl.Y - pad && pt.Y <= tl.Y + h + pad;
    }

    void SlideTo(double target)
    {
        _slideTarget = target;
        if (_sliding) return;
        _sliding = true;
        CompositionTarget.Rendering += OnSlideRender;
    }

    void OnSlideRender(object? sender, EventArgs e)
    {
        double d = _slideTarget - _slide;
        if (Math.Abs(d) > 0.005) _slide += d * 0.25;
        else _slide = _slideTarget;
        Place();
        if (_slide == _slideTarget)
        {
            CompositionTarget.Rendering -= OnSlideRender;
            _sliding = false;
        }
    }

    void Menu_Opened(object sender, RoutedEventArgs e) => _menuOpen++;
    void Menu_Closed(object sender, RoutedEventArgs e) => _menuOpen = Math.Max(0, _menuOpen - 1);

    // ---- running indicator + auto-shown running apps ----

    // Pinned items get the running dot when their exe matches a running app. Running apps with no pinned
    // item get a temporary icon added to the dock; it's removed again as soon as that app closes.
    void RefreshRunning()
    {
        var running = WindowTracker.GetRunningExePaths();
        var names = new HashSet<string>(
            running.Select(p => System.IO.Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase);
        var pinnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _items)
        {
            if (item.IsSeparator || item.IsTemporary) continue;
            var full = IconService.ResolveRunMatchPath(item.Path);
            bool bare = item.Path.IndexOfAny(['\\', '/']) < 0;
            bool isRunning = full != null && (running.Contains(full) ||
                (bare && names.Contains(System.IO.Path.GetFileName(full))));
            item.IsRunning = isRunning;
            if (isRunning) pinnedPaths.Add(full!);
        }

        foreach (var path in _transient.Keys.Where(p => !running.Contains(p)).ToList())
            RemoveTransient(path);

        foreach (var path in running)
        {
            if (pinnedPaths.Contains(path) || _transient.ContainsKey(path)) continue;
            AddTransient(path);
        }
    }

    void AddTransient(string path)
    {
        var item = new DockItem
        {
            Name = FriendlyName(path),
            Path = path,
            IsTemporary = true,
            IsRunning = true,
            Icon = IconService.GetIcon(path),
        };
        _transient[path] = item;
        _items.CollectionChanged -= Items_Changed;
        _items.Add(item);
        _items.CollectionChanged += Items_Changed;
        ResetZoom();
    }

    void RemoveTransient(string path)
    {
        if (!_transient.Remove(path, out var item)) return;
        _items.CollectionChanged -= Items_Changed;
        _items.Remove(item);
        _items.CollectionChanged += Items_Changed;
        _xf.Remove(item);
        ResetZoom();
    }

    static string FriendlyName(string exePath)
    {
        try
        {
            var desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(desc)) return desc;
        }
        catch (Exception) { }
        return System.IO.Path.GetFileNameWithoutExtension(exePath);
    }

    // ---- magnify ----

    double Len(DockItem item) => item.IsSeparator ? DockLayout.SepSlot : Slot;

    // Item index under a position measured along the dock's main axis from the start of the item list.
    int IndexAtPos(double p)
    {
        double start = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            start += Len(_items[i]);
            if (p < start) return i;
        }
        return Math.Max(0, _items.Count - 1);
    }

    void Item_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not DockItem item) return;
        var s = new ScaleTransform();
        var t = new TranslateTransform();
        var g = new TransformGroup();
        g.Children.Add(s);
        g.Children.Add(t);
        el.RenderTransform = g;
        _xf[item] = (s, t);
        Apply();
    }

    void Dock_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = Mouse.GetPosition(Items);
        double p = Vertical ? pos.Y : pos.X;
        double start = 0;
        for (int i = 0; i < _target.Length; i++)
        {
            double len = Len(_items[i]);
            double center = start + len / 2;
            start += len;
            if (_items[i].IsSeparator) { _target[i] = 1; continue; }
            double d = Math.Abs(p - center);
            _target[i] = d < Radius
                ? 1 + (MaxZoom - 1) * 0.5 * (1 + Math.Cos(Math.PI * d / Radius))
                : 1;
        }
        StartRendering();
    }

    void Dock_MouseLeave(object sender, MouseEventArgs e)
    {
        Array.Fill(_target, 1.0);
        StartRendering();
    }

    void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        CompositionTarget.Rendering += OnRender;
    }

    void OnRender(object? sender, EventArgs e)
    {
        bool moving = false;
        for (int i = 0; i < _cur.Length; i++)
        {
            double d = _target[i] - _cur[i];
            if (Math.Abs(d) > 0.002) { _cur[i] += d * 0.25; moving = true; }
            else _cur[i] = _target[i];
        }
        Apply();
        if (!moving)
        {
            CompositionTarget.Rendering -= OnRender;
            _rendering = false;
        }
    }

    // Scale each icon about its edge-side center and shift neighbors along the main axis so grown icons don't overlap.
    void Apply()
    {
        bool vertical = Vertical;
        double total = 0;
        foreach (var s in _cur) total += (s - 1) * IconSize;
        double before = 0;
        for (int i = 0; i < _items.Count && i < _cur.Length; i++)
        {
            double extra = (_cur[i] - 1) * IconSize;
            if (_xf.TryGetValue(_items[i], out var x))
            {
                x.S.ScaleX = x.S.ScaleY = _cur[i];
                double shift = before + extra / 2 - total / 2;
                x.T.X = vertical ? 0 : shift;
                x.T.Y = vertical ? shift : 0;
            }
            before += extra;
        }
    }

    // ---- launch / reorder ----

    void Item_Down(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragItem = (sender as FrameworkElement)?.DataContext as DockItem;
        _dragged = false;
    }

    void Item_Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragItem;
        _dragItem = null;
        _dragged = true;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(DockItem), item), DragDropEffects.Move);
    }

    void Item_Click(object sender, MouseButtonEventArgs e)
    {
        _dragItem = null;
        if (_dragged) { _dragged = false; return; }
        if (((FrameworkElement)sender).DataContext is not DockItem { IsSeparator: false } item) return;
        // Auto-shown running-app icons bring the existing window forward rather than starting a new instance.
        if (item.IsTemporary && WindowTracker.TryActivate(item.Path)) return;
        LaunchService.Launch(item);
    }

    // ---- drop: reorder or add files ----

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DockItem)) ? DragDropEffects.Move
            : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DockItem)) is DockItem moved)
        {
            var pos = e.GetPosition(Items);
            int from = _items.IndexOf(moved);
            int to = Math.Clamp(IndexAtPos(Vertical ? pos.Y : pos.X), 0, _items.Count - 1);
            if (from >= 0 && to != from) _items.Move(from, to);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files) AddItem(file);
        }
        e.Handled = true;
    }

    void AddItem(string path, string? name = null)
    {
        if (_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        name ??= System.IO.Path.GetFileNameWithoutExtension(path);
        if (name.Length == 0) name = System.IO.Path.GetFileName(path);
        _items.Add(new DockItem { Name = name, Path = path, Icon = IconService.GetIcon(path) });
    }

    void AddSeparatorAt(int index) =>
        _items.Insert(Math.Clamp(index, 0, _items.Count), new DockItem { IsSeparator = true, Name = "---" });

    // Start-menu app (classic or UWP) launched as shell:AppsFolder\<AppUserModelId>.
    void ShowAppPicker()
    {
        var dlg = new AppPickerWindow();
        if (dlg.ShowDialog() == true && dlg.Selected is { } app)
            AddItem(@"shell:AppsFolder\" + app.AppId, app.Name);
    }

    // ---- menu ----

    void AddSeparatorAfter_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DockItem item)
            AddSeparatorAt(_items.IndexOf(item) + 1);
    }

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DockItem { CanEdit: true } item) return;
        var dlg = new ItemEditWindow(item) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        item.Name = dlg.ItemName;
        item.Path = dlg.ItemPath;
        item.Args = dlg.ItemArgs;
        item.RunAsAdmin = dlg.RunAsAdmin;
        item.Icon = IconService.GetIcon(item.Path);
        SaveConfig();
        RefreshRunning();
    }

    // Promotes an auto-shown running app to a regular, saved dock entry.
    void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DockItem { IsTemporary: true } item) return;
        _transient.Remove(item.Path);
        item.IsTemporary = false;
        SaveConfig();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is DockItem item)
            _items.Remove(item);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
