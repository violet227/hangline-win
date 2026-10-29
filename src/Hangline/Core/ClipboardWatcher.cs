using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Hangline.Core;

/// Hangs images copied to the clipboard, like a capture from Win+Shift+S.
/// Each one is saved as a PNG in Hangline's own folder, so on the line it
/// behaves like any screenshot.
///
/// Off until the user turns it on. Only a picture and nothing else counts:
/// text from a spreadsheet also comes as an image of itself, a file copied in
/// Explorer brings its file, and password managers mark private copies.
internal sealed class ClipboardWatcher : IDisposable
{
    private readonly Action<string> onImage;
    private HwndSource? source;
    private byte[]? lastHash;
    private string? lastPath;
    /// Set while Hangline itself writes the clipboard, so a photo copied from
    /// the line does not hang again.
    public static bool SelfCopy;

    private static readonly uint ExcludeFormat = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HistoryFormat = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint IgnoreFormat = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
    private const uint CF_UNICODETEXT = 13, CF_HDROP = 15;
    private const long MaxPixels = 80_000_000;

    public static bool IsEnabled
    {
        get => Settings.Current.ClipboardEnabled;
        set { Settings.Current.ClipboardEnabled = value; Settings.Current.Save(); }
    }

    public ClipboardWatcher(Action<string> onImage) => this.onImage = onImage;

    public void Start()
    {
        if (source != null) return;
        source = new HwndSource(new HwndSourceParameters("HanglineClipboard") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
        Native.AddClipboardFormatListener(source.Handle);
    }

    public void Stop()
    {
        if (source == null) return;
        Native.RemoveClipboardFormatListener(source.Handle);
        source.Dispose();
        source = null;
    }

    public void Dispose() => Stop();

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            // Read once the copying app has closed the clipboard.
            Ui.After(0.05, Check);
        }
        return IntPtr.Zero;
    }

    private void Check()
    {
        if (SelfCopy) return;
        if (Native.IsClipboardFormatAvailable(ExcludeFormat) || Native.IsClipboardFormatAvailable(HistoryFormat)
            || Native.IsClipboardFormatAvailable(IgnoreFormat) || Native.IsClipboardFormatAvailable(CF_UNICODETEXT)
            || Native.IsClipboardFormatAvailable(CF_HDROP)) return;
        BitmapSource? image;
        try
        {
            if (!Clipboard.ContainsImage()) return;
            image = ReadPng() ?? Clipboard.GetImage();
        }
        catch (Exception e)
        {
            Log.Write($"Could not read the clipboard: {e.Message}");
            return;
        }
        if (image == null || image.PixelWidth <= 0 || (long)image.PixelWidth * image.PixelHeight > MaxPixels) return;
        Save(Thumbnails.Opaque(new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0)));
    }

    /// Snipping Tool also puts a PNG, which keeps transparency and is exact.
    private static BitmapSource? ReadPng()
    {
        if (Clipboard.GetData("PNG") is not MemoryStream ms) return null;
        var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private void Save(BitmapSource image)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var png = ms.ToArray();
        var hash = SHA256.HashData(png);
        // Some apps put the same image on the clipboard again; it hangs once,
        // unless it has been taken down since.
        if (lastHash != null && hash.AsSpan().SequenceEqual(lastHash) && lastPath != null && File.Exists(lastPath)) return;
        try
        {
            Directory.CreateDirectory(Inbox.Folder);
            var path = Ui.UniquePath(Inbox.Folder, $"Clipboard {DateTime.Now:yyyy-MM-dd 'at' HH.mm.ss}.png");
            File.WriteAllBytes(path, png);
            lastHash = hash;
            lastPath = path;
            onImage(path);
        }
        catch (Exception e)
        {
            Log.Write($"Could not save a copied image: {e.Message}");
        }
    }
}
