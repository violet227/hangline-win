using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hangline.Core;

namespace Hangline.UI;

/// The capture lifting off the screen and flying up to the line, or a
/// discarded card falling off it. Drawn over the whole monitor, so it is
/// never cut by the line's strip. It turns into the hanging card on the way:
/// it shrinks, tilts into place and grows its glass frame and clip.
internal sealed class Flight : Window
{
    private const double FlyDuration = 0.65, FallDuration = 0.55, Arc = 30;
    private static readonly List<Flight> Current = new();

    /// Captures flying to the line right now. In a burst of captures only a
    /// couple fly and the rest drop in.
    public static int InProgress => Current.Count(f => !f.falling);

    private readonly Canvas canvas = new();
    private readonly Grid container = new();
    private readonly Border glass, photo;
    private readonly FrameworkElement clip;
    private readonly RotateTransform rotate = new();
    private readonly Rect from, to;
    private readonly double tilt, scale;
    private readonly Native.RECT screen;
    private readonly bool falling;
    private readonly double duration;
    private double start;
    private Action completion = () => { };
    private Action<double>? frame;

    /// from and to are in physical pixels, in screen coordinates. tilt in degrees, clockwise.
    public static void Fly(BitmapSource image, Rect from, Rect to, double tilt, IntPtr monitor, Action completion)
    {
        var f = new Flight(image, from, to, tilt, monitor, falling: false) { completion = completion };
        f.Run();
    }

    public static void Fall(BitmapSource image, Rect card, double tilt, IntPtr monitor)
    {
        var f = new Flight(image, card, card, tilt, monitor, falling: true);
        f.Run();
    }

    private Flight(BitmapSource image, Rect from, Rect to, double tilt, IntPtr monitor, bool falling)
    {
        this.from = from;
        this.to = to;
        this.tilt = tilt;
        this.falling = falling;
        duration = falling ? FallDuration : FlyDuration;
        screen = Native.MonitorInfo(monitor).rcMonitor;
        scale = Native.Scale(monitor);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Left = -10000; Top = -10000; Width = 10; Height = 10;
        Content = canvas;

        glass = new Border
        {
            Background = new SolidColorBrush(Theme.GlassFill),
            BorderBrush = Glass.Specular(),
            BorderThickness = new Thickness(0.75),
        };
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        photo = new Border { Background = brush };
        clip = Clothespin.Make();
        clip.HorizontalAlignment = HorizontalAlignment.Center;
        clip.VerticalAlignment = VerticalAlignment.Top;
        container.Children.Add(glass);
        container.Children.Add(photo);
        container.Children.Add(clip);
        container.RenderTransform = rotate;
        container.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.24, BlurRadius = 20, ShadowDepth = 5, Direction = 270 };
        canvas.Children.Add(container);

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(h, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT, 0);
            Native.SetWindowPos(h, Native.HWND_TOPMOST, screen.Left, screen.Top, screen.Width, screen.Height, Native.SWP_NOACTIVATE);
        };
    }

    private void Run()
    {
        Current.Add(this);
        if (falling) { Update(1); UpdateFall(0); } else Update(0);
        Show();
        start = Animator.Now;
        frame = _ => Tick();
        Animator.AddFrame(frame);
    }

    private void Tick()
    {
        var k = Math.Min(1, (Animator.Now - start) / duration);
        if (falling) UpdateFall(k); else Update(k);
        if (k < 1) return;
        Animator.RemoveFrame(frame!);
        Current.Remove(this);
        completion();
        if (falling) { Close(); return; }
        // The real card fades in underneath; this one fades out over it.
        var fade = new Anim(1, v => Opacity = v);
        fade.Tween(0, 0.16, Ease.Out, Close);
    }

    /// Screen pixels to this window's DIPs.
    private double DX(double x) => (x - screen.Left) / scale;
    private double DY(double y) => (y - screen.Top) / scale;

    private void UpdateFall(double raw)
    {
        var e = raw * raw * raw;
        var angle = tilt + (tilt * 7 + 20) * e;
        var w = to.Width / scale;
        Canvas.SetLeft(container, DX(to.X + to.Width / 2) - w / 2);
        Canvas.SetTop(container, DY(to.Y) + 520 * e);
        rotate.CenterX = w / 2;
        rotate.CenterY = 0;
        rotate.Angle = angle;
        container.Opacity = 1 - e;
    }

    private void Update(double raw)
    {
        var k = Ease.InOutCubic(raw);
        var chrome = Ease.Smooth(k, 0.35, 1);
        double Lerp(double a, double b) => a + (b - a) * k;

        var w = Lerp(from.Width, to.Width) / scale;
        var h = Lerp(from.Height, to.Height) / scale;
        var topX = DX(Lerp(from.X + from.Width / 2, to.X + to.Width / 2));
        var topY = DY(Lerp(from.Y, to.Y)) - Math.Sin(Math.PI * k) * Arc;
        var inset = 4 * k;
        var radius = Lerp(0, Layout.FrameRadius);

        container.Width = w;
        container.Height = h;
        Canvas.SetLeft(container, topX - w / 2);
        Canvas.SetTop(container, topY);
        rotate.CenterX = w / 2;
        rotate.CenterY = 0;
        rotate.Angle = tilt * k;

        glass.CornerRadius = new CornerRadius(radius);
        glass.Opacity = chrome;
        photo.Margin = new Thickness(inset);
        photo.CornerRadius = new CornerRadius(Math.Max(0, radius - inset));
        // The clip grips the top edge: 26 tall, 12 of them over the card.
        clip.Margin = new Thickness(0, -(Layout.ClipHeight - Layout.ClipOverlap), 0, 0);
        clip.Opacity = chrome;
    }
}
