using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Hangline.UI;

/// The tray icon is drawn in code: a T-shirt, outlined, or filled in while
/// copied images are hung. It follows the taskbar's light or dark theme.
internal static class TrayIcon
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    public static Icon Make(bool filled, bool dimmed, bool dark)
    {
        var scale = Core.Native.Scale(Core.Native.MonitorFromPoint(default, Core.Native.MONITOR_DEFAULTTONEAREST));
        var size = Math.Max(16, (int)Math.Round(16 * scale));
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 16f, size / 16f);
            var alpha = dimmed ? 130 : 255;
            var color = dark ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 28, 28, 30);
            using var path = Shirt();
            if (filled)
            {
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);
            }
            using var pen = new Pen(color, 1.25f) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    /// A shirt: shoulders, short sleeves, a collar dip.
    private static GraphicsPath Shirt()
    {
        var p = new GraphicsPath();
        p.AddLines(new PointF[]
        {
            new(5.6f, 2.5f), new(1.3f, 4.6f), new(2.6f, 7.6f), new(4.3f, 6.9f),
            new(4.3f, 13.6f), new(11.7f, 13.6f), new(11.7f, 6.9f), new(13.4f, 7.6f),
            new(14.7f, 4.6f), new(10.4f, 2.5f),
        });
        p.AddBezier(new PointF(10.4f, 2.5f), new PointF(9.8f, 4.3f), new PointF(6.2f, 4.3f), new PointF(5.6f, 2.5f));
        p.CloseFigure();
        return p;
    }

    /// A menu from the tray needs a foreground window of its own, or it
    /// never closes when you click elsewhere.
    private static Forms.Form? owner;

    public static void ShowMenu(Forms.ContextMenuStrip menu)
    {
        owner ??= new Forms.Form
        {
            ShowInTaskbar = false, FormBorderStyle = Forms.FormBorderStyle.None,
            Size = new Size(1, 1), StartPosition = Forms.FormStartPosition.Manual, Location = new Point(-32000, -32000),
            Opacity = 0,
        };
        if (!owner.Visible) owner.Show();
        SetForegroundWindow(owner.Handle);
        menu.Closed += (_, _) =>
        {
            owner?.Hide();
            Core.Ui.After(0.5, menu.Dispose);
        };
        var c = Core.Native.Cursor;
        menu.Show(new Point(c.X, c.Y), Forms.ToolStripDropDownDirection.AboveLeft);
    }
}
