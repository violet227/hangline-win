using System.IO;
using System.Windows.Media.Imaging;

namespace Hangline.Core;

/// One screenshot hanging on the line.
internal sealed class Pegged
{
    private static readonly Random Rng = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; }
    public BitmapSource Thumb { get; set; }
    /// Every photo hangs a little crooked, like on a real line.
    public double Tilt { get; } = Rng.NextDouble() * 5 - 2.5;
    public bool Falling { get; set; }
    /// Still flying in from where it was captured; the card waits hidden.
    public bool Flying { get; set; }
    public bool IsRecording => Thumbnails.IsRecording(Path);

    public Pegged(string path, BitmapSource thumb)
    {
        Path = path;
        Thumb = thumb;
    }
}

/// Small helpers for the main thread, the counterpart of DispatchQueue.main.
internal static class Ui
{
    public static void After(double seconds, Action action)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    public static void Post(Action action) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    public static string UniquePath(string folder, string name)
    {
        var baseName = System.IO.Path.GetFileNameWithoutExtension(name);
        var ext = System.IO.Path.GetExtension(name);
        var candidate = System.IO.Path.Combine(folder, name);
        for (var n = 2; File.Exists(candidate); n++)
            candidate = System.IO.Path.Combine(folder, $"{baseName} {n}{ext}");
        return candidate;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\'), System.IO.Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsInside(string path, string folder) =>
        System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath(folder).TrimEnd('\\') + "\\",
            StringComparison.OrdinalIgnoreCase);
}
