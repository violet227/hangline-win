using System.IO;

namespace Hangline.Core;

/// Watches the folder Windows saves screenshots to (Win+PrtScn and Snipping
/// Tool's automatic save) and reports new ones. Hangline never takes
/// screenshots itself: you keep your usual shortcut and the line picks them up.
internal sealed class ScreenshotWatcher : IDisposable
{
    public string Folder { get; }
    private readonly bool acceptsRecordings;
    private readonly Action<string> onNew;
    private readonly Action onChange;
    private FileSystemWatcher? watcher;
    private readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    public ScreenshotWatcher(string folder, bool acceptsRecordings, Action<string> onNew, Action onChange)
    {
        Folder = folder;
        this.acceptsRecordings = acceptsRecordings;
        this.onNew = onNew;
        this.onChange = onChange;
    }

    public static string ScreenshotFolder =>
        Native.KnownFolder(Native.FOLDERID_Screenshots)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// Windows 11's Snipping Tool saves recordings here.
    public static string RecordingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");

    public void Start()
    {
        if (watcher != null) return;
        try
        {
            Directory.CreateDirectory(Folder);
            // What is already there is old news.
            foreach (var f in Directory.EnumerateFiles(Folder)) seen.Add(f);
            var w = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            w.Created += (_, e) => Ui.Post(() => Arrived(e.FullPath));
            w.Renamed += (_, e) => Ui.Post(() => { Arrived(e.FullPath); onChange(); });
            w.Deleted += (_, _) => Ui.Post(onChange);
            w.Error += (_, e) => Log.Write($"Watcher error on {Folder}: {e.GetException().Message}");
            w.EnableRaisingEvents = true;
            watcher = w;
        }
        catch (Exception e)
        {
            Log.Write($"Cannot watch {Folder}: {e.Message}");
        }
    }

    public void Stop()
    {
        watcher?.Dispose();
        watcher = null;
    }

    public void Dispose() => Stop();

    private bool IsCandidate(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.Temporary)) != 0) return false;
        if (Thumbnails.IsRecording(path)) return acceptsRecordings;
        return Thumbnails.IsImage(path);
    }

    private void Arrived(string path)
    {
        if (!File.Exists(path) || seen.Contains(path)) return;
        try { if (!IsCandidate(path)) return; } catch { return; }
        seen.Add(path);
        WaitUntilWritten(path, 0);
    }

    /// The file is created before it is written. It counts once it can be
    /// opened and its size has settled.
    private void WaitUntilWritten(string path, int attempt, long lastSize = -1)
    {
        Ui.After(attempt == 0 ? 0.15 : 0.25, () =>
        {
            if (!File.Exists(path)) return;
            long size;
            try
            {
                using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                size = s.Length;
            }
            catch (IOException)
            {
                size = -2;
            }
            // Recordings take long to finish; give them up to a minute.
            var settled = size > 0 && size == lastSize;
            if (settled || (size > 0 && attempt >= 1 && !Thumbnails.IsRecording(path))) onNew(path);
            else if (attempt < 240) WaitUntilWritten(path, attempt + 1, size);
        });
    }
}

/// Inbox mode: Hangline takes over where screenshots end up.
///
/// Windows has no setting it would be safe to change, so instead each new
/// capture is moved from the Screenshots folder into Hangline's own folder.
/// Only what you drag out is kept; the cross sends the rest to the Recycle Bin.
internal static class Inbox
{
    public static string Folder { get; } = Path.Combine(Settings.DataFolder, "Screenshots");

    public static bool IsEnabled
    {
        get => Settings.Current.InboxEnabled;
        set { Settings.Current.InboxEnabled = value; Settings.Current.Save(); }
    }

    public static bool WasOffered
    {
        get => Settings.Current.InboxOffered;
        set { Settings.Current.InboxOffered = value; Settings.Current.Save(); }
    }

    /// Moves a capture into the inbox and returns where it is now. If it
    /// cannot be moved, it hangs from where it is.
    public static string Adopt(string path)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var target = Ui.UniquePath(Folder, Path.GetFileName(path));
            File.Move(path, target);
            return target;
        }
        catch (Exception e)
        {
            Log.Write($"Could not move {path} into the inbox: {e.Message}");
            return path;
        }
    }
}
