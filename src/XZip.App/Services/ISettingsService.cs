using Microsoft.UI.Xaml;

using Windows.Storage;

using XZip.Core.Abstractions;

namespace XZip.App.Services;

public enum BackdropKind { Mica, Acrylic, None }

public interface ISettingsService
{
    ElementTheme Theme { get; set; }
    BackdropKind Backdrop { get; set; }
    ArchiveFormat DefaultFormat { get; set; }
    string Language { get; set; }
    IReadOnlyList<string> RecentArchives { get; }
    void AddRecent(string path);
    void ClearRecent();
}

/// <summary>
/// Settings persisted via either ApplicationData (packaged) or a JSON file in LocalAppData (unpackaged).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const int MaxRecent = 16;
    private readonly Dictionary<string, object> _values;
    private readonly bool _packaged;
    private readonly string _filePath;
    private readonly List<string> _recent = new();

    public SettingsService()
    {
        _packaged = IsPackaged();
        _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XZip", "settings.json");
        Load();
    }

    public ElementTheme Theme
    {
        get => Enum.TryParse((string?)Get(nameof(Theme), null) ?? "Default", out ElementTheme t) ? t : ElementTheme.Default;
        set => Set(nameof(Theme), value.ToString());
    }

    public BackdropKind Backdrop
    {
        get => Enum.TryParse((string?)Get(nameof(Backdrop), null) ?? "Mica", out BackdropKind b) ? b : BackdropKind.Mica;
        set => Set(nameof(Backdrop), value.ToString());
    }

    public ArchiveFormat DefaultFormat
    {
        get => Enum.TryParse((string?)Get(nameof(DefaultFormat), null) ?? "Zip", out ArchiveFormat f) ? f : ArchiveFormat.Zip;
        set => Set(nameof(DefaultFormat), value.ToString());
    }

    public string Language
    {
        get => (string?)Get(nameof(Language), null) ?? "system";
        set => Set(nameof(Language), value);
    }

    public IReadOnlyList<string> RecentArchives => _recent.AsReadOnly();

    public void AddRecent(string path)
    {
        _recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, path);
        if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        Set("Recent", string.Join("|", _recent));
    }

    public void ClearRecent()
    {
        _recent.Clear();
        Set("Recent", string.Empty);
    }

    private object? Get(string key, object? fallback)
    {
        if (_packaged)
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            return s.TryGetValue(key, out var pv) ? pv : fallback;
        }
        return _values.TryGetValue(key, out var v) ? v : fallback;
    }

    private void Set(string key, object value)
    {
        if (_packaged)
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        else
        {
            _values[key] = value;
            Save();
        }
    }

    private void Load()
    {
        if (_packaged)
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            if (s.TryGetValue("Recent", out var v) && v is string str && !string.IsNullOrEmpty(str))
                _recent.AddRange(str.Split('|'));
            return;
        }

        try
        {
            if (!File.Exists(_filePath)) return;
            var lines = File.ReadAllLines(_filePath);
            foreach (var line in lines)
            {
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                _values[line[..idx]] = line[(idx + 1)..];
            }
            if (_values.TryGetValue("Recent", out var v) && v is string str && !string.IsNullOrEmpty(str))
                _recent.AddRange(str.Split('|'));
        }
        catch { /* ignore corrupted settings */ }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            using var writer = new StreamWriter(_filePath, append: false);
            foreach (var kv in _values)
            {
                writer.WriteLine($"{kv.Key}={kv.Value}");
            }
        }
        catch { /* ignore IO errors when persisting */ }
    }

    private static bool IsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch
        {
            return false;
        }
    }
}
