using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace WinNavi.Dock.Models;

public class DockItem : INotifyPropertyChanged
{
    string _name = "";
    string _path = "";
    string _args = "";
    bool _isSeparator;
    bool _runAsAdmin;
    bool _isRunning;
    ImageSource? _icon;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Path { get => _path; set => Set(ref _path, value); }

    // Launch options: extra command-line arguments.
    public string Args { get => _args; set => Set(ref _args, value); }

    // Launch options: run elevated (UAC prompt).
    public bool RunAsAdmin { get => _runAsAdmin; set => Set(ref _runAsAdmin, value); }

    // A separator is a divider line between sections; it has no Path and is never launched.
    public bool IsSeparator { get => _isSeparator; set => Set(ref _isSeparator, value); }

    // An icon the dock added for a currently running app that isn't pinned; never saved to dock.json,
    // and removed automatically once that app closes. See MainWindow.RefreshRunning.
    [JsonIgnore]
    public bool IsTemporary { get; set; }

    // Separators have nothing to edit; temporary (auto-shown running app) icons aren't persisted, so
    // editing them would be lost on the next refresh - pin them first.
    [JsonIgnore]
    public bool CanEdit => !IsSeparator && !IsTemporary;

    [JsonIgnore]
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    [JsonIgnore]
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
