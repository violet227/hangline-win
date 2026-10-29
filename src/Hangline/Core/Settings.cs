using System.IO;
using System.Text.Json;

namespace Hangline.Core;

/// Everything the app remembers, in one small JSON file. The counterpart of
/// UserDefaults in the original.
internal sealed class Settings
{
    public const string AppName = "Hangline";

    /// HANGLINE_DATA points it elsewhere, for trying the app without touching real settings.
    public static string DataFolder { get; } = Environment.GetEnvironmentVariable("HANGLINE_DATA") is { Length: > 0 } d ? d :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    public static Settings Current { get; } = Load();

    // What hangs on the line, oldest first.
    public List<string> Pegged { get; set; } = new();
    public string LineSize { get; set; } = "medium";
    /// Keep on line: 0 for as many as fit, otherwise 25, 50 or 100.
    public int KeepOnLine { get; set; }
    public bool SoundOff { get; set; }
    public bool MenuBarRevealOff { get; set; }
    public bool QuietHang { get; set; }
    public bool ShowOverFullScreen { get; set; }
    public bool InboxEnabled { get; set; }
    public bool InboxOffered { get; set; }
    public bool ClipboardEnabled { get; set; }
    public bool Welcomed { get; set; }
    public List<string> ExtraFolders { get; set; } = new();
    /// Null fields mean the default Ctrl+Alt+T; HotKeyVk 0 means none.
    public uint? HotKeyMods { get; set; }
    public uint? HotKeyVk { get; set; }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e)
        {
            Log.Write($"Could not read settings: {e.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception e)
        {
            Log.Write($"Could not save settings: {e.Message}");
        }
    }
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.DataFolder);
                File.AppendAllText(Path.Combine(Settings.DataFolder, "hangline.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
