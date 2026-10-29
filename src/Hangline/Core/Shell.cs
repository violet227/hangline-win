using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Hangline.Core;

internal static class Shell
{
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write($"Could not open {path}: {e.Message}"); }
    }

    public static void ShowInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception e) { Log.Write($"Could not reveal {path}: {e.Message}"); }
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Open(folder);
    }
}

/// Long press opens a photo in Snipping Tool's editor, the window Windows
/// shows when you click the notification of a screenshot you just took.
/// Without it, Paint. When the file is saved over, the line shows the new
/// version.
internal static class Markup
{
    private static FileSystemWatcher? watcher;

    public static void Edit(string path, Action<string> onSaved)
    {
        Watch(path, onSaved);
        var uri = $"ms-screensketch:edit?source=Hangline&isTemporary=false&filePath={Uri.EscapeDataString(path)}";
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("mspaint.exe", $"\"{path}\"") { UseShellExecute = true }); }
            catch (Exception e)
            {
                Log.Write($"No editor: {e.Message}");
                Shell.Open(path);
            }
        }
    }

    private static void Watch(string path, Action<string> onSaved)
    {
        watcher?.Dispose();
        var dir = Path.GetDirectoryName(path);
        if (dir == null) return;
        var w = new FileSystemWatcher(dir, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        var pending = false;
        void Saved()
        {
            if (pending) return;
            pending = true;
            // Editors write in several steps; read it once they are done.
            Ui.After(0.5, () => { pending = false; onSaved(path); });
        }
        w.Changed += (_, _) => Ui.Post(Saved);
        w.Renamed += (_, _) => Ui.Post(Saved);
        w.EnableRaisingEvents = true;
        watcher = w;
    }
}

/// Open at login, through the current user's Run key.
internal static class Startup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Settings.AppName) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue(Settings.AppName, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Settings.AppName, false);
        }
    }
}

/// Knows when to stay hidden: a full screen app, a game or a presentation
/// on the monitor the line would come down on.
internal static class FullScreen
{
    public static bool ShowLineOver
    {
        get => Settings.Current.ShowOverFullScreen;
        set { Settings.Current.ShowOverFullScreen = value; Settings.Current.Save(); }
    }

    public static bool BlocksLine(IntPtr monitor) => !ShowLineOver && IsActive(monitor);

    public static bool IsActive(IntPtr monitor)
    {
        // QUNS_BUSY 2, QUNS_RUNNING_D3D_FULL_SCREEN 3, QUNS_PRESENTATION_MODE 4.
        if (Native.SHQueryUserNotificationState(out var state) == 0 && state is 3 or 4)
            return true;
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
        var cls = new System.Text.StringBuilder(64);
        Native.GetClassName(fg, cls, cls.Capacity);
        // The desktop itself covers the screen and is not full screen.
        if (cls.ToString() is "WorkerW" or "Progman") return false;
        if (Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST) != monitor) return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        var m = Native.MonitorInfo(monitor).rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
