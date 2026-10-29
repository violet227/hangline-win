using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Hangline.UI;

namespace Hangline.Core;

/// Thumbnails come from the shell, which handles images and the first frame
/// of recordings alike.
internal static class Thumbnails
{
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic" };

    public static bool IsRecording(string path) =>
        new[] { ".mp4", ".mov", ".mkv", ".wmv" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// The default size covers a card at twice its size, as on a high-dpi
    /// screen, and no more: every photo on the line keeps one in memory.
    public static BitmapSource? Make(string path, int? maxPixels = null)
    {
        var max = maxPixels ?? (int)(320 * Layout.Scale);
        if (!File.Exists(path)) return null;
        if (!IsRecording(path) && Decode(path, max) is { } decoded) return decoded;
        return FromShell(path, max);
    }

    /// The full image, for copying.
    public static BitmapSource? LoadFull(string path) => Decode(path, null);

    /// Many apps, Snipaste among them, put a 32-bit bitmap on the clipboard
    /// whose alpha bytes are all zero: the colours are there, but read as is
    /// the picture is fully transparent. Such an image is made opaque; one
    /// with real transparency in it is kept as it is.
    public static BitmapSource Opaque(BitmapSource image)
    {
        if (!image.Format.Equals(System.Windows.Media.PixelFormats.Bgra32)
            && !image.Format.Equals(System.Windows.Media.PixelFormats.Pbgra32)) return image;
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) return image;
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var opaque = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96,
            System.Windows.Media.PixelFormats.Bgr32, null, pixels, stride);
        opaque.Freeze();
        return opaque;
    }

    private static BitmapSource? Decode(string path, int? max)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            // Made opaque before scaling: the scaler works premultiplied, and
            // would turn the colours under a zero alpha into black.
            BitmapSource result = Opaque(new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0));
            if (max is { } m && Math.Max(frame.PixelWidth, frame.PixelHeight) > m)
            {
                var s = (double)m / Math.Max(frame.PixelWidth, frame.PixelHeight);
                result = new TransformedBitmap(result, new System.Windows.Media.ScaleTransform(s, s));
            }
            // A fixed 96 dpi, so its size in DIPs is its size in pixels.
            var converted = new FormatConvertedBitmap(result, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var bmp = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception e)
        {
            Log.Write($"Could not decode {path}: {e.Message}");
            return null;
        }
    }

    private static BitmapSource? FromShell(string path, int max)
    {
        try
        {
            Native.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(Native.IShellItemImageFactory).GUID, out var factory);
            // SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY would refuse icons; take thumbnails or icons.
            var hr = factory.GetImage(new Native.SIZE { cx = max, cy = max }, 0x1, out var hbm);
            Marshal.ReleaseComObject(factory);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                var bmp = new WriteableBitmap(new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgr32, null, 0));
                var frozen = BitmapSource.Create(bmp.PixelWidth, bmp.PixelHeight, 96, 96, bmp.Format, null,
                    Pixels(bmp), bmp.PixelWidth * 4);
                frozen.Freeze();
                return frozen;
            }
            finally
            {
                Native.DeleteObject(hbm);
            }
        }
        catch (Exception e)
        {
            Log.Write($"No shell thumbnail for {path}: {e.Message}");
            return null;
        }
    }

    private static byte[] Pixels(BitmapSource b)
    {
        var stride = b.PixelWidth * 4;
        var bytes = new byte[stride * b.PixelHeight];
        b.CopyPixels(bytes, stride, 0);
        return bytes;
    }
}
