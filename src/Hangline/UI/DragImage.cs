using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Hangline.Core;

namespace Hangline.UI;

/// The photo follows the pointer while it is dragged into Explorer or
/// another app: a small click-through window that tracks the cursor for as
/// long as the drag lasts.
internal sealed class DragGhost : Window
{
    private readonly double w, h;
    private IntPtr handle;

    public DragGhost(BitmapSource thumb, Size size)
    {
        w = size.Width;
        h = size.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Width = w + 24;
        Height = h + 24;
        var brush = new ImageBrush(thumb) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        Content = new Border
        {
            Width = w, Height = h,
            CornerRadius = new CornerRadius(Layout.FrameRadius - Layout.FrameInset),
            Background = brush,
            Opacity = 0.85,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 16, ShadowDepth = 4, Direction = 270 },
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(handle, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT, 0);
            Follow();
        };
    }

    /// Centred on the pointer, on whichever monitor it is over.
    public void Follow()
    {
        if (handle == IntPtr.Zero) return;
        var c = Native.Cursor;
        var s = Native.Scale(Native.MonitorFromPoint(c, Native.MONITOR_DEFAULTTONEAREST));
        var pw = (int)(Width * s);
        var ph = (int)(Height * s);
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, c.X - pw / 2, c.Y - ph / 2, pw, ph, Native.SWP_NOACTIVATE);
    }
}
