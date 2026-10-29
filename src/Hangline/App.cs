using System.IO;
using System.Windows;
using System.Windows.Threading;
using Hangline.Core;
using Hangline.UI;
using Forms = System.Windows.Forms;

namespace Hangline;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.Run();
    }
}

/// The tray icon, the shortcut, and revealing and tucking away the line.
/// A port of the original's AppDelegate: the same states, delays and rules.
internal sealed class App : Application
{
    private Line line = null!;
    private LineWindow panel = null!;
    private Forms.NotifyIcon tray = null!;
    private ScreenshotWatcher? watcher, recordingsWatcher;
    private readonly List<ScreenshotWatcher> folderWatchers = new();
    private ClipboardWatcher clipboardWatcher = null!;
    private HotKey? hotKey;
    private DispatcherTimer? mouseTimer;
    private Mutex? single;

    /// Whether the panel is in at all. It can be in and still tucked away.
    private bool isPresent;
    private bool IsRevealed => panel.Revealed;
    /// Opened on purpose with the shortcut or the menu: it stays down until
    /// the cursor has visited it and left, or the shortcut is pressed again.
    private bool pinned;
    /// A new screenshot shows itself for a moment, then tucks away.
    private DateTime peekUntil = DateTime.MinValue;
    private DateTime? hotZoneSince, awaySince;
    /// After a click in the top band the line stays up there hidden until
    /// the pointer leaves the band.
    private bool topSuppressed;
    private bool wasLeftDown;
    /// Whether the line should be up, if nothing prevents it.
    private bool wanted;
    /// Set when you open the line on purpose, so it stays up while empty.
    private bool keepOpen;
    /// Opened on purpose, which counts over a full screen app too.
    private bool openedOnPurpose;

    /// How long the cursor rests against the top edge before the line comes
    /// down. Short enough to feel instant, long enough that a quick trip to
    /// a title bar does not trigger it.
    private const double RevealDelay = 0.25;
    /// How long the cursor is away before the line tucks back up.
    private const double RetractDelay = 0.5;
    /// The band along the top edge that stands in for the menu bar, in DIPs.
    private const double TopBand = 3;
    private const int MaxExtraFolders = 3;

    private static bool OpensFromTop
    {
        get => !Settings.Current.MenuBarRevealOff;
        set { Settings.Current.MenuBarRevealOff = !value; Settings.Current.Save(); }
    }

    private static bool PeeksAtNew
    {
        get => !Settings.Current.QuietHang;
        set { Settings.Current.QuietHang = !value; Settings.Current.Save(); }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // One per data folder, so a trial run with HANGLINE_DATA can sit beside the real one.
        var mutexName = "Hangline.SingleInstance." + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Settings.DataFolder.ToLowerInvariant())))[..16];
        single = new Mutex(true, mutexName, out var first);
        if (!first)
        {
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"Unhandled: {ex.Exception}");
            ex.Handled = true;
        };
        Directory.CreateDirectory(Inbox.Folder);

        line = new Line();
        panel = new LineWindow(line);
        panel.EnsureHandle();
        panel.Place(Native.MonitorUnderCursor());
        UpdateCapacity();

        StartWatchers();
        clipboardWatcher = new ClipboardWatcher(HangCapture);
        if (ClipboardWatcher.IsEnabled) clipboardWatcher.Start();

        RegisterShortcut();
        SetUpTray();

        panel.OnScroll = d => line.ScrollBy(d);
        line.OnFall = Fall;
        line.Changed += ItemsChanged;
        // Draws the photos restored from last time.
        line.NotifyChanged();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            panel.Place(panel.Monitor);
            UpdateCapacity();
        });

        // Photos kept from last time show themselves for a moment.
        if (line.LiveCount > 0) ComeDown(IntPtr.Zero);

        if (!Inbox.WasOffered) Ui.After(1.2, OfferInbox);

        if (!Settings.Current.Welcomed)
        {
            Settings.Current.Welcomed = true;
            Settings.Current.Save();
            keepOpen = true;
            wanted = true;
            Refresh();
            Reveal(pinned: true);
            Ui.After(5, () =>
            {
                if (line.LiveCount != 0) return;
                keepOpen = false;
                wanted = false;
                Refresh();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hotKey?.Dispose();
        clipboardWatcher?.Dispose();
        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        base.OnExit(e);
    }

    // MARK: Watching

    private void StartWatchers()
    {
        watcher?.Stop();
        recordingsWatcher?.Stop();
        recordingsWatcher = null;
        foreach (var w in folderWatchers) w.Stop();
        folderWatchers.Clear();

        watcher = new ScreenshotWatcher(ScreenshotWatcher.ScreenshotFolder, acceptsRecordings: true, NewScreenshot, line.Prune);
        watcher.Start();
        if (Directory.Exists(ScreenshotWatcher.RecordingsFolder))
        {
            recordingsWatcher = new ScreenshotWatcher(ScreenshotWatcher.RecordingsFolder, acceptsRecordings: true, NewScreenshot, line.Prune);
            recordingsWatcher.Start();
        }
        // The inbox itself, so a file that leaves it also leaves the line.
        var inbox = new ScreenshotWatcher(Inbox.Folder, acceptsRecordings: false, _ => { }, line.Prune);
        inbox.Start();
        folderWatchers.Add(inbox);
        foreach (var folder in ExtraFolders)
        {
            var extra = new ScreenshotWatcher(folder, acceptsRecordings: false, HangCapture, line.Prune);
            extra.Start();
            folderWatchers.Add(extra);
        }
    }

    /// A new capture in the Screenshots folder. With Handle screenshots on,
    /// it moves into Hangline's own folder first.
    private void NewScreenshot(string path)
    {
        if (Inbox.IsEnabled) path = Inbox.Adopt(path);
        HangCapture(path, fromScreen: true);
    }

    private static List<string> ExtraFolders
    {
        get => Settings.Current.ExtraFolders.Where(Directory.Exists).ToList();
        set { Settings.Current.ExtraFolders = value; Settings.Current.Save(); }
    }

    private void ChooseExtraFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = Strings.L("New images saved to this folder will hang on the line."),
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        var folders = ExtraFolders;
        if (folders.Count >= MaxExtraFolders || folders.Any(f => Ui.SamePath(f, dialog.SelectedPath))) return;
        folders.Add(dialog.SelectedPath);
        ExtraFolders = folders;
        StartWatchers();
    }

    /// Asked once. Taking over the user's files is their call, never ours.
    private void OfferInbox()
    {
        Inbox.WasOffered = true;
        var result = MessageBox.Show(
            Strings.L("Screenshots will hang on the line the instant you take them, and will not pile up in your Screenshots folder. Drag one to a folder to keep it, or discard it with the cross. You can turn this off from the tray icon."),
            Strings.L("Let Hangline handle your screenshots?"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) Inbox.IsEnabled = true;
    }

    private void SetClipboard(bool on)
    {
        ClipboardWatcher.IsEnabled = on;
        if (on) clipboardWatcher.Start(); else clipboardWatcher.Stop();
        UpdateTrayIcon();
    }

    // MARK: Showing and hiding

    /// An empty line goes away, unless it was opened on purpose.
    private void ItemsChanged()
    {
        UpdateTrayIcon();
        if (line.LiveCount != 0 || keepOpen) return;
        Ui.After(0.7, () =>
        {
            if (line.LiveCount != 0 || keepOpen) return;
            wanted = false;
            Refresh();
        });
    }

    /// The line comes down on that monitor for a moment, then tucks away.
    private void ComeDown(IntPtr monitor)
    {
        panel.Place(monitor == IntPtr.Zero ? Native.MonitorUnderCursor() : monitor);
        UpdateCapacity();
        wanted = true;
        Refresh();
        if (PeeksAtNew) Reveal(peekFor: 2.5);
    }

    private void HangCapture(string path) => HangCapture(path, fromScreen: false);

    /// A new screenshot lifts off from the screen it was taken on and flies
    /// to its place on the line. Windows does not record the captured area,
    /// so a full screen capture starts from the whole monitor, and anything
    /// else from its own shape in the middle of the screen.
    private void HangCapture(string path, bool fromScreen)
    {
        var monitor = Native.MonitorUnderCursor();
        ComeDown(monitor);
        var id = line.Hang(path, flying: IsRevealed);
        if (id == null)
        {
            // Nothing hung after all, so an empty line goes away again.
            ItemsChanged();
            return;
        }
        if (!IsRevealed) return;
        // Let the line come down and lay out before measuring the landing spot.
        Ui.After(0.03, () => Fly(id.Value, monitor, fromScreen));
    }

    private void Fly(Guid id, IntPtr monitor, bool fromScreen)
    {
        if (!isPresent || !IsRevealed || Flight.InProgress >= 2 || panel.CardFrame(id) is not { } to
            || line.Find(id) is not { } item)
        {
            line.Land(id);
            return;
        }
        var m = Native.MonitorInfo(monitor).rcMonitor;
        var full = Thumbnails.LoadFull(item.Path);
        double pw = full?.PixelWidth ?? item.Thumb.PixelWidth, ph = full?.PixelHeight ?? item.Thumb.PixelHeight;
        Rect from;
        if (fromScreen && Math.Abs(pw - m.Width) <= 2 && Math.Abs(ph - m.Height) <= 2)
        {
            from = new Rect(m.Left, m.Top, m.Width, m.Height);
        }
        else
        {
            // Its own size, at most 60% of the screen, centred.
            var s = Math.Min(1, Math.Min(m.Width * 0.6 / pw, m.Height * 0.6 / ph));
            double w = pw * s, h = ph * s;
            from = new Rect(m.Left + (m.Width - w) / 2, m.Top + (m.Height - h) / 2, w, h);
        }
        var pixels = (int)Math.Max(from.Width, from.Height);
        var image = Thumbnails.Make(item.Path, Math.Min(1500, Math.Max(400, pixels))) ?? item.Thumb;
        Flight.Fly(image, from, to, item.Tilt, panel.Monitor, () => line.Land(id));
    }

    /// A discarded card falls over the whole screen, from where it hangs.
    private void Fall(Pegged item)
    {
        if (!isPresent || !IsRevealed || item.Flying || panel.CardFrame(item.Id) is not { } card) return;
        Flight.Fall(item.Thumb, card, item.Tilt, panel.Monitor);
    }

    /// Decides whether the panel is in at all: something to show, and no
    /// full screen app on that monitor unless you asked for the line.
    private void Refresh()
    {
        var blocked = !openedOnPurpose && FullScreen.BlocksLine(panel.Monitor);
        if (wanted && !blocked) Present(); else Dismiss();
        // The cursor is watched while there is a line, even tucked away, to
        // notice it pushing against the top edge.
        if (wanted) StartMouseTracking(); else StopMouseTracking();
    }

    private void Present() => isPresent = true;

    private void Dismiss()
    {
        if (!isPresent) return;
        isPresent = false;
        SetRevealed(false);
    }

    private void Reveal(bool pinned = false, double peekFor = 0)
    {
        if (!isPresent) return;
        if (pinned) this.pinned = true;
        if (peekFor > 0) peekUntil = DateTime.Now.AddSeconds(peekFor);
        awaySince = null;
        SetRevealed(true);
    }

    private void SetRevealed(bool on)
    {
        if (on == IsRevealed) return;
        // It always comes down showing the newest.
        if (on) line.ResetScroll();
        panel.SetRevealed(on);
        if (on) return;
        pinned = false;
        peekUntil = DateTime.MinValue;
        // Opened on purpose lasts until the line tucks away, however that
        // happens: an empty line then goes, as with the shortcut.
        if (keepOpen || openedOnPurpose)
        {
            keepOpen = false;
            openedOnPurpose = false;
            if (line.LiveCount == 0) wanted = false;
            Refresh();
        }
    }

    private void Toggle()
    {
        if (IsRevealed)
        {
            SetRevealed(false);
            return;
        }
        keepOpen = true;
        wanted = true;
        openedOnPurpose = true;
        panel.Place(Native.MonitorUnderCursor());
        UpdateCapacity();
        Refresh();
        Reveal(pinned: true);
    }

    private void StartMouseTracking()
    {
        if (mouseTimer != null) return;
        mouseTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
        mouseTimer.Tick += (_, _) => Tick();
        mouseTimer.Start();
    }

    private void StopMouseTracking()
    {
        mouseTimer?.Stop();
        mouseTimer = null;
        panel.SetPassThrough(true);
    }

    /// The top few pixels of a monitor stand in for the macOS menu bar. With
    /// the taskbar along the top, the band sits just under it.
    private static bool InTopBand(Native.POINT p, out IntPtr monitor)
    {
        monitor = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        var m = Native.MonitorInfo(monitor);
        var band = (int)Math.Ceiling(TopBand * Native.Scale(monitor));
        return p.X >= m.rcMonitor.Left && p.X < m.rcMonitor.Right && p.Y >= m.rcMonitor.Top && p.Y < m.rcWork.Top + band;
    }

    private void Tick()
    {
        var mouse = Native.Cursor;
        var now = DateTime.Now;
        var inTop = InTopBand(mouse, out var monitor);
        if (!inTop) topSuppressed = false;

        // A click up there, on a title bar or a tab, puts the line away and
        // keeps it away until the pointer leaves the band.
        var leftDown = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
        var clicked = leftDown && !wasLeftDown;
        wasLeftDown = leftDown;
        if (clicked && inTop && !(IsRevealed && panel.IsOverPhoto(mouse)))
        {
            topSuppressed = true;
            hotZoneSince = null;
            if (IsRevealed) { pinned = false; SetRevealed(false); }
            return;
        }

        if (!IsRevealed)
        {
            // Resting at the top edge brings the line down on that monitor.
            // An empty line stays up there: only the shortcut and the menu
            // bring it down on purpose.
            if (line.LiveCount > 0 && OpensFromTop && inTop && !topSuppressed && !leftDown && !FullScreen.BlocksLine(monitor))
            {
                hotZoneSince ??= now;
                if ((now - hotZoneSince.Value).TotalSeconds >= RevealDelay)
                {
                    hotZoneSince = null;
                    if (panel.Monitor != monitor)
                    {
                        panel.Place(monitor);
                        UpdateCapacity();
                    }
                    Refresh();
                    Reveal();
                }
            }
            else hotZoneSince = null;
            return;
        }

        UpdateMousePassThrough(mouse);

        // The line's zone runs from its lowest point up to the top of the
        // monitor, so moving up never hides it.
        var frame = panel.Frame;
        var mon = Native.MonitorInfo(panel.Monitor).rcMonitor;
        var zone = new Native.RECT { Left = frame.Left, Right = frame.Right, Top = mon.Top, Bottom = frame.Bottom };
        var inside = zone.Contains(mouse);
        if (inside && pinned) pinned = false;

        var busy = pinned || PeggedCard.IsDragging || PeggedCard.IsShowingMenu || line.PressedId != null || now < peekUntil;
        if (inside || busy)
        {
            awaySince = null;
        }
        else
        {
            awaySince ??= now;
            if ((now - awaySince.Value).TotalSeconds >= RetractDelay)
            {
                awaySince = null;
                SetRevealed(false);
            }
        }
    }

    /// The panel spans the whole width of the monitor, so it only takes the
    /// mouse while the cursor is over a photo. Everywhere else, clicks go to
    /// whatever is underneath.
    private void UpdateMousePassThrough(Native.POINT mouse)
    {
        if (PeggedCard.IsDragging) return;
        panel.SetPassThrough(!panel.IsOverPhoto(mouse));
    }

    // MARK: Shortcut

    private void RegisterShortcut()
    {
        hotKey?.Dispose();
        hotKey = null;
        if (Shortcut.Current is not { } s) return;
        var hk = new HotKey(s, Toggle);
        if (hk.Registered) hotKey = hk; else hk.Dispose();
    }

    private void ChangeShortcut()
    {
        // Off while recording, so pressing the current one records it.
        hotKey?.Dispose();
        hotKey = null;
        ShortcutRecorder.Record((result, changed) =>
        {
            if (changed)
            {
                var previous = Shortcut.Current;
                Shortcut.Current = result;
                RegisterShortcut();
                // Taken by another app: keep the one that worked.
                if (result != null && hotKey == null)
                {
                    MessageBox.Show(Strings.L("That shortcut is taken by another app."), Settings.AppName);
                    Shortcut.Current = previous;
                }
            }
            RegisterShortcut();
        });
    }

    private void SetKeep(int n)
    {
        if (n == Settings.Current.KeepOnLine) return;
        Settings.Current.KeepOnLine = n;
        Settings.Current.Save();
        UpdateCapacity();
        line.Trim();
    }

    private void SetSize(Layout.Size size)
    {
        if (size == Layout.Current) return;
        Layout.Current = size;
        panel.Place(panel.Monitor, force: true);
        UpdateCapacity();
        line.ReloadThumbnails();
    }

    private void UpdateCapacity()
    {
        var usable = panel.WidthDip - 200;
        var fits = Math.Max(3, Math.Min(12, (int)(usable / Layout.Spacing)));
        line.VisibleCount = fits;
        line.MaxItems = Math.Max(fits, Settings.Current.KeepOnLine == 0 ? fits : Settings.Current.KeepOnLine);
    }

    // MARK: Tray

    private void SetUpTray()
    {
        tray = new Forms.NotifyIcon { Visible = true };
        UpdateTrayIcon();
        // A click shows or hides the line; the menu is on the right button.
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Toggle();
            else if (e.Button == Forms.MouseButtons.Right) ShowTrayMenu();
        };
    }

    /// The shirt fills in while copied images are hung, and is dimmed while
    /// the line is empty, so seeing nothing come down does not look broken.
    private void UpdateTrayIcon()
    {
        var on = ClipboardWatcher.IsEnabled;
        var count = line.LiveCount;
        var old = tray.Icon;
        tray.Icon = TrayIcon.Make(filled: on, dimmed: count == 0, dark: Theme.TaskbarIsDark);
        old?.Dispose();
        var description = on ? Strings.L("Hangline, hanging copied images") : Settings.AppName;
        var state = count == 0 ? Strings.L("The line is empty") : string.Format(Strings.L("{0} on the line"), count);
        var tip = $"{description}\n{state}";
        tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    private void ShowTrayMenu()
    {
        var menu = Menus.New();
        Forms.ToolStripMenuItem Item(string title, Action a, bool enabled = true, bool check = false) =>
            Menus.Item(Strings.L(title), a, enabled, check);

        var toggle = Item(IsRevealed ? "Hide line" : "Show line", Toggle);
        if (Shortcut.Current is { } s) toggle.ShortcutKeyDisplayString = s.Display;
        menu.Items.Add(toggle);
        menu.Items.Add(Item("Take everything down", line.Clear, line.LiveCount > 0));
        menu.Items.Add(Item("Handle screenshots", () => Inbox.IsEnabled = !Inbox.IsEnabled, check: Inbox.IsEnabled));
        menu.Items.Add(Item("Hang copied images", () => SetClipboard(!ClipboardWatcher.IsEnabled), check: ClipboardWatcher.IsEnabled));

        menu.Items.Add(Menus.Sub(Strings.L("Bring the line down"),
            Item("When the pointer rests at the top edge", () => OpensFromTop = !OpensFromTop, check: OpensFromTop),
            Item("When something new hangs", () => PeeksAtNew = !PeeksAtNew, check: PeeksAtNew),
            Item("Over full screen apps", () => { FullScreen.ShowLineOver = !FullScreen.ShowLineOver; Refresh(); }, check: FullScreen.ShowLineOver)));

        var folders = ExtraFolders;
        var folderItems = new List<Forms.ToolStripItem>();
        foreach (var f in folders)
            folderItems.Add(Menus.Item(FolderName(f), () => Shell.OpenFolder(f), check: true));
        if (folders.Count > 0) folderItems.Add(new Forms.ToolStripSeparator());
        folderItems.Add(Item("Add Folder…", ChooseExtraFolder, folders.Count < MaxExtraFolders));
        if (folders.Count > 0)
            folderItems.Add(Menus.Sub(Strings.L("Stop Watching"), folders.Select(f => (Forms.ToolStripItem)Menus.Item(FolderName(f), () =>
            {
                ExtraFolders = ExtraFolders.Where(x => !Ui.SamePath(x, f)).ToList();
                StartWatchers();
            })).ToArray()));
        menu.Items.Add(Menus.Sub(Strings.L("Also watch folders"), folderItems.ToArray()));

        var keeps = new List<Forms.ToolStripItem> { Item("As many as fit", () => SetKeep(0), check: Settings.Current.KeepOnLine == 0) };
        foreach (var n in Line.KeepChoices)
            keeps.Add(Menus.Item(string.Format(Strings.L("{0} photos"), n), () => SetKeep(n), check: Settings.Current.KeepOnLine == n));
        menu.Items.Add(Menus.Sub(Strings.L("Keep on line"), keeps.ToArray()));

        menu.Items.Add(Menus.Sub(Strings.L("Size"),
            Item("Small", () => SetSize(Layout.Size.Small), check: Layout.Current == Layout.Size.Small),
            Item("Medium", () => SetSize(Layout.Size.Medium), check: Layout.Current == Layout.Size.Medium),
            Item("Large", () => SetSize(Layout.Size.Large), check: Layout.Current == Layout.Size.Large)));

        menu.Items.Add(Menus.Sub(Strings.L("Shortcut"),
            Menus.Item(Shortcut.Current?.Display ?? Strings.L("None"), () => { }, enabled: false),
            Item("Change Shortcut…", ChangeShortcut)));

        menu.Items.Add(Item("Open screenshots folder", () =>
            Shell.OpenFolder(Inbox.IsEnabled ? Inbox.Folder : ScreenshotWatcher.ScreenshotFolder)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("Sounds", () => line.SoundOn = !line.SoundOn, check: line.SoundOn));
        menu.Items.Add(Item("Open at login", () => Core.Startup.IsEnabled = !Core.Startup.IsEnabled, check: Core.Startup.IsEnabled));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("Quit Hangline", Shutdown));

        TrayIcon.ShowMenu(menu);
    }

    private static string FolderName(string path) => Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
}
