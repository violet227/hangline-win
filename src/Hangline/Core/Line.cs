using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Hangline.UI;

namespace Hangline.Core;

/// The line itself: what hangs on it and what you can do with each item.
/// The files never move. The line is only a view onto them.
internal sealed class Line
{
    private static readonly Random Rng = new();

    private readonly List<Pegged> items = new();
    public IReadOnlyList<Pegged> Items => items;

    /// Anything about the items changed: added, falling, removed, new thumbnail.
    public event Action? Changed;
    /// A little wind moves the line.
    public event Action? Gust;
    /// A single photo's state changed: copied, pressed, dragging.
    public event Action<Guid>? StateChanged;
    /// Called just before a photo starts falling, so the fall can be drawn
    /// over the whole screen.
    public Action<Pegged>? OnFall;

    public Guid? CopiedId { get; private set; }
    public Guid? DraggingId { get; private set; }
    public Guid? PressedId { get; private set; }

    public int MaxItems = 8;
    /// How many photos fit across the screen. With Keep on line set, the
    /// line can hold more, and the rest are reached by scrolling.
    public int VisibleCount = 8;
    /// How far the line is scrolled towards older photos; 0 shows the newest.
    public double Scroll { get; private set; }

    public static readonly int[] KeepChoices = { 25, 50, 100 };

    public bool SoundOn
    {
        get => !Settings.Current.SoundOff;
        set { Settings.Current.SoundOff = !value; Settings.Current.Save(); }
    }

    public int LiveCount => items.Count(i => !i.Falling);
    private IEnumerable<Pegged> Live => items.Where(i => !i.Falling);

    public Line()
    {
        Restore();
        ScheduleGust();
    }

    public Pegged? Find(Guid id) => items.FirstOrDefault(i => i.Id == id);

    public void NotifyChanged() => Changed?.Invoke();

    // MARK: Hanging and dropping

    public Guid? Hang(string path, bool quietly = false, bool flying = false)
    {
        if (Live.Any(i => Ui.SamePath(i.Path, path))) return null;
        var thumb = Thumbnails.Make(path);
        if (thumb == null) return null;
        var item = new Pegged(path, thumb) { Flying = flying };
        items.Add(item);
        Scroll = 0;
        // A full line lets the oldest photo fall off the far end. Only one.
        if (LiveCount > MaxItems && Live.FirstOrDefault() is { } oldest) LetGo(oldest.Id);
        Save();
        if (!quietly) Sounds.Play(Sounds.Tink, SoundOn);
        Changed?.Invoke();
        return item.Id;
    }

    /// The capture has reached the line: the real card takes over.
    public void Land(Guid id)
    {
        if (Find(id) is not { } item) return;
        item.Flying = false;
        Changed?.Invoke();
    }

    public void Drop(Guid id, bool quietly = false)
    {
        if (Find(id) is not { Falling: false } item) return;
        OnFall?.Invoke(item);
        item.Falling = true;
        Save();
        if (!quietly) Sounds.Play(Sounds.Pop, SoundOn);
        Changed?.Invoke();
        Ui.After(0.6, () =>
        {
            items.RemoveAll(i => i.Id == id);
            Changed?.Invoke();
        });
    }

    /// "Take everything down": every photo goes the way of its corner cross.
    public void Clear() => TakeDown(Live.ToList());

    /// The photos hung before (left) or after (right) this one.
    public List<Pegged> Neighbours(Guid id, bool toTheLeft)
    {
        var live = Live.ToList();
        var i = live.FindIndex(p => p.Id == id);
        if (i < 0) return new();
        return toTheLeft ? live.Take(i).ToList() : live.Skip(i + 1).ToList();
    }

    public void TakeDown(Guid id, bool toTheLeft) => TakeDown(Neighbours(id, toTheLeft));

    /// One after another, and only the first makes a sound.
    private void TakeDown(List<Pegged> live)
    {
        for (var n = 0; n < live.Count; n++)
        {
            var item = live[n];
            var quiet = n > 0;
            if (n == 0) Discard(item.Id, quiet);
            else Ui.After(0.06 * n, () => Discard(item.Id, quiet));
        }
    }

    /// Photos whose file was deleted or moved away fall off by themselves.
    public void Prune()
    {
        foreach (var item in Live.ToList())
            if (!File.Exists(item.Path)) Drop(item.Id, quietly: true);
    }

    // MARK: Actions on one photo

    public void Copy(Guid id)
    {
        if (Find(id) is not { } item) return;
        try
        {
            var data = new DataObject();
            // A recording is copied as the file, which apps paste as the video.
            if (!item.IsRecording && Thumbnails.LoadFull(item.Path) is { } full)
            {
                data.SetImage(full);
                var png = new MemoryStream();
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(full));
                enc.Save(png);
                data.SetData("PNG", png);
            }
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { item.Path });
            ClipboardWatcher.SelfCopy = true;
            Ui.After(0.4, () => ClipboardWatcher.SelfCopy = false);
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception e)
        {
            Log.Write($"Could not copy: {e.Message}");
            return;
        }

        // Only one photo says Copied at a time: the one before lets go of it.
        var previous = CopiedId;
        CopiedId = id;
        if (previous is { } p && p != id) StateChanged?.Invoke(p);
        StateChanged?.Invoke(id);
        Ui.After(1.2, () =>
        {
            if (CopiedId != id) return;
            CopiedId = null;
            StateChanged?.Invoke(id);
        });
    }

    public void SetDragging(Guid? id)
    {
        var was = DraggingId;
        DraggingId = id;
        if (was is { } w) StateChanged?.Invoke(w);
        if (id is { } n) StateChanged?.Invoke(n);
    }

    public void SetPressed(Guid? id)
    {
        var was = PressedId;
        if (was == id) return;
        PressedId = id;
        if (was is { } w) StateChanged?.Invoke(w);
        if (id is { } n) StateChanged?.Invoke(n);
    }

    public void Open(Guid id)
    {
        if (Find(id) is not { } item) return;
        Shell.Open(item.Path);
    }

    /// Sends the file to the Recycle Bin and takes the photo off the line.
    public bool Trash(Guid id, bool quietly = false)
    {
        if (Find(id) is not { } item) return false;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Log.Write($"Trashed {System.IO.Path.GetFileName(item.Path)}");
            if (!quietly) Sounds.Play(Sounds.Crumple, SoundOn);
            Drop(id, quietly: true);
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"Could not trash {item.Path}: {e.Message}");
            if (!quietly) System.Media.SystemSounds.Beep.Play();
            // Already gone is as good as trashed.
            if (!File.Exists(item.Path)) { Drop(id, quietly: true); return true; }
            return false;
        }
    }

    /// Whether the file lives in Hangline's own folder. Those are discarded
    /// to the Recycle Bin, or the folder would fill up with forgotten
    /// screenshots. Files anywhere else stay where they are.
    public bool IsInInbox(Guid id) => Find(id) is { } item && Ui.IsInside(item.Path, Inbox.Folder);

    /// The corner cross and "Take down" both end up here.
    public void Discard(Guid id, bool quietly = false)
    {
        if (IsInInbox(id)) Trash(id, quietly); else Drop(id, quietly);
    }

    /// Moves along a line that holds more than fit, from the mouse wheel.
    public void ScrollBy(double delta)
    {
        var most = Math.Max(0, items.Count - VisibleCount) * Layout.Spacing;
        var next = Math.Min(most, Math.Max(0, Scroll + delta));
        if (next == Scroll) return;
        Scroll = next;
        Changed?.Invoke();
    }

    public void ResetScroll()
    {
        if (Scroll == 0) return;
        Scroll = 0;
        Changed?.Invoke();
    }

    /// After Keep on line is lowered, the oldest photos past it go the way
    /// of a full line.
    public void Trim()
    {
        while (LiveCount > MaxItems && Live.FirstOrDefault() is { } oldest) LetGo(oldest.Id);
        ResetScroll();
    }

    /// Like the cross, a file from Hangline's folder goes to the Recycle Bin,
    /// or nothing would ever take it out of that folder.
    private void LetGo(Guid id)
    {
        if (IsInInbox(id) && Trash(id, quietly: true)) return;
        Drop(id, quietly: true);
    }

    /// Inbox mode: keep a screenshot by moving it to the Desktop.
    public void SaveToDesktop(Guid id)
    {
        if (Find(id) is not { } item) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try
        {
            File.Move(item.Path, Ui.UniquePath(desktop, System.IO.Path.GetFileName(item.Path)));
            Drop(id, quietly: true);
        }
        catch (Exception e)
        {
            Log.Write($"Could not save to Desktop: {e.Message}");
            System.Media.SystemSounds.Beep.Play();
        }
    }

    /// Long press: open the photo in an editor. A recording opens in the
    /// default player instead.
    public void Markup(Guid id)
    {
        if (Find(id) is not { } item) return;
        if (item.IsRecording) Shell.Open(item.Path);
        else Core.Markup.Edit(item.Path, ReloadThumbnail);
    }

    /// After editing, the photo on the line shows the new version.
    public void ReloadThumbnail(string path)
    {
        var item = Live.FirstOrDefault(i => Ui.SamePath(i.Path, path));
        if (item == null || Thumbnails.Make(path) is not { } thumb) return;
        item.Thumb = thumb;
        Changed?.Invoke();
    }

    /// A quick look at this photo, with the rest of the line a key press away.
    public void QuickLook(Guid id)
    {
        var live = Live.ToList();
        var index = live.FindIndex(i => i.Id == id);
        if (index < 0) return;
        PreviewWindow.Show(live.Select(i => i.Path).ToList(), index);
    }

    /// After a change of size, every photo is redrawn sharp at the new one.
    public void ReloadThumbnails()
    {
        foreach (var item in Live)
            if (Thumbnails.Make(item.Path) is { } thumb) item.Thumb = thumb;
        Changed?.Invoke();
    }

    public void Reveal(Guid id)
    {
        if (Find(id) is not { } item) return;
        Shell.ShowInExplorer(item.Path);
    }

    // MARK: Breeze

    /// Every so often a little wind moves the line. It is the detail that
    /// makes it feel like an object and not a widget.
    private void ScheduleGust()
    {
        Ui.After(7 + Rng.NextDouble() * 9, () =>
        {
            if (items.Count > 0 && DraggingId == null) Gust?.Invoke();
            ScheduleGust();
        });
    }

    // MARK: Persistence

    private void Save()
    {
        Settings.Current.Pegged = Live.Select(i => i.Path).ToList();
        Settings.Current.Save();
    }

    /// Everything comes back as it was, without going through Hang, so a full
    /// line does not let photos fall at every launch.
    private void Restore()
    {
        foreach (var path in Settings.Current.Pegged)
        {
            if (!File.Exists(path) || items.Any(i => Ui.SamePath(i.Path, path))) continue;
            if (Thumbnails.Make(path) is { } thumb) items.Add(new Pegged(path, thumb));
        }
    }
}
