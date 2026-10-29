using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Hangline.Core;

namespace Hangline.UI;

/// A transparent strip along the top of the screen that floats over every
/// app, never takes focus, and lets clicks pass through everywhere except
/// over the photos. It also draws the line and lays the photos out on it.
internal sealed class LineWindow : Window
{
    private readonly Line line;
    private readonly Canvas root = new();
    private readonly Canvas content = new();
    private readonly Rope rope = new();
    private readonly FrameworkElement hint;
    private readonly TranslateTransform slide = new();
    private readonly Anim slideK, hintK;
    private readonly Dictionary<Guid, PeggedCard> cards = new();
    public IntPtr Handle { get; private set; }
    /// The monitor the line hangs on.
    public IntPtr Monitor { get; private set; }
    /// The window's frame in physical pixels.
    public Native.RECT Frame { get; private set; }
    public double Dpi { get; private set; } = 1;
    private bool passThrough = true;

    /// Two-finger swipes and the mouse wheel, for moving along a long line.
    public Action<double>? OnScroll;

    public LineWindow(Line line)
    {
        this.line = line;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = Settings.AppName;
        Width = 800;
        Height = Layout.PanelHeight;
        SizeToContent = SizeToContent.Manual;

        root.ClipToBounds = true;
        content.RenderTransform = slide;
        content.Children.Add(rope);
        hint = MakeHint();
        content.Children.Add(hint);
        root.Children.Add(content);
        Content = root;

        // Tucked away, the whole line waits above the top edge and slides out
        // from under it, the way an auto-hiding taskbar does.
        slideK = new Anim(0, v => slide.Y = -(Layout.PanelHeight + 12) * (1 - v));
        hintK = new Anim(0, v => hint.Opacity = v);

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(Handle, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT, 0);
            HwndSource.FromHwnd(Handle)?.AddHook(Hook);
        };
        DpiChanged += (_, _) => Ui.Post(() => Place(Monitor));
        PreviewMouseWheel += (_, e) =>
        {
            OnScroll?.Invoke(-e.Delta / 120.0 * 40);
            e.Handled = true;
        };

        line.Changed += Sync;
        line.StateChanged += id => { if (cards.TryGetValue(id, out var c)) c.Refresh(); };
        line.Gust += () => { foreach (var c in cards.Values) c.Breeze(); };
    }

    /// Never activated by a click, so the app you are in keeps the keyboard.
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    public double WidthDip => Frame.Width / Dpi;

    /// The line hangs at the top of the work area of a monitor, the whole
    /// width of it.
    public void Place(IntPtr monitor, bool force = false)
    {
        if (monitor == IntPtr.Zero) monitor = Native.MonitorUnderCursor();
        var work = Native.MonitorInfo(monitor).rcWork;
        var scale = Native.Scale(monitor);
        var height = (int)Math.Ceiling(Layout.PanelHeight * scale);
        var target = new Native.RECT { Left = work.Left, Top = work.Top, Right = work.Right, Bottom = work.Top + height };
        var changed = Monitor != monitor || Frame.Left != target.Left || Frame.Top != target.Top
            || Frame.Width != target.Width || Frame.Height != target.Height || Math.Abs(Dpi - scale) > 0.001;
        Monitor = monitor;
        Frame = target;
        Dpi = scale;
        var w = target.Width / scale;
        root.Width = w;
        root.Height = Layout.PanelHeight;
        ApplyFrame();
        if (force)
        {
            // A new size: every card is made again at it.
            foreach (var c in cards.Values) content.Children.Remove(c);
            cards.Clear();
            Sync();
        }
        if (changed || force) Relayout(animated: false);
    }

    /// WPF keeps its own idea of the window's place in DIPs and puts it back
    /// on Show, so the frame is set in physical pixels after anything WPF does.
    private void ApplyFrame()
    {
        if (Handle == IntPtr.Zero) return;
        var f = Frame;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, f.Left, f.Top, f.Width, f.Height, Native.SWP_NOACTIVATE);
    }

    public void EnsureHandle()
    {
        if (Handle != IntPtr.Zero) return;
        Handle = new WindowInteropHelper(this).EnsureHandle();
    }

    // MARK: Revealing

    public bool Revealed { get; private set; }

    public void SetRevealed(bool on)
    {
        if (Revealed == on) return;
        Revealed = on;
        if (on)
        {
            if (!IsVisible) Show();
            ApplyFrame();
            slideK.Spring(1, 0.42, 0.82);
        }
        else
        {
            SetPassThrough(true);
            slideK.Tween(0, 0.22, Ease.In);
            // Tucked away, the line leaves no window behind.
            Ui.After(0.3, () => { if (!Revealed) Hide(); });
        }
    }

    public void SetPassThrough(bool on)
    {
        if (passThrough == on || Handle == IntPtr.Zero) return;
        passThrough = on;
        Native.SetExStyle(Handle, on ? Native.WS_EX_TRANSPARENT : 0, on ? 0 : Native.WS_EX_TRANSPARENT);
    }

    /// Whether a point on screen, in physical pixels, is over a photo. A
    /// line longer than the screen also takes the band the photos hang in,
    /// gaps included, so the wheel works anywhere along it.
    public bool IsOverPhoto(Native.POINT p)
    {
        var local = new Point((p.X - Frame.Left) / Dpi, (p.Y - Frame.Top) / Dpi);
        var band = Rect.Empty;
        foreach (var c in cards.Values)
        {
            if (c.Item.Falling) continue;
            var r = c.CardBounds(root);
            if (r.IsEmpty) continue;
            r.Inflate(4, 4);
            if (r.Contains(local)) return true;
            band.Union(r);
        }
        if (line.Items.Count > line.VisibleCount && Settings.Current.KeepOnLine != 0 && !band.IsEmpty)
            return local.Y >= band.Top && local.Y <= band.Bottom;
        return false;
    }

    /// Where a card hangs, in physical screen pixels, unrotated.
    public Rect? CardFrame(Guid id)
    {
        var index = line.Items.ToList().FindIndex(i => i.Id == id);
        if (index < 0) return null;
        var width = WidthDip;
        var x = Layout.X(index, line.Items.Count, width, line.VisibleCount, line.Scroll);
        var top = Layout.RopeY(x, width) - Layout.PinAbove + Layout.CardOffsetBelowTop;
        var t = line.Items[index].Thumb;
        var size = Layout.CardSize(t.PixelWidth, t.PixelHeight);
        return new Rect(Frame.Left + (x - size.Width / 2) * Dpi, Frame.Top + top * Dpi, size.Width * Dpi, size.Height * Dpi);
    }

    // MARK: Layout

    private void Sync()
    {
        var ids = line.Items.Select(i => i.Id).ToHashSet();
        foreach (var gone in cards.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            content.Children.Remove(cards[gone]);
            cards.Remove(gone);
        }
        var width = WidthDip;
        for (var i = 0; i < line.Items.Count; i++)
        {
            var item = line.Items[i];
            if (cards.TryGetValue(item.Id, out var card))
            {
                if (!ReferenceEquals(card.Tag, item.Thumb))
                {
                    // A new picture can change the shape: start the card over, in place.
                    var x = card.X.Value;
                    content.Children.Remove(card);
                    cards.Remove(item.Id);
                    Add(item, x, arrived: true);
                }
                else card.ItemChanged();
            }
            else
            {
                var x = Layout.X(i, line.Items.Count, width, line.VisibleCount, line.Scroll);
                Add(item, x, arrived: false);
            }
        }
        hintK.Tween(line.Items.Count == 0 ? 1 : 0, 0.3, Ease.InOut);
        Relayout(animated: true);
    }

    private void Add(Pegged item, double x, bool arrived)
    {
        var card = new PeggedCard(item, line, x, arrived) { Tag = item.Thumb };
        card.PositionChanged += Position;
        cards[item.Id] = card;
        content.Children.Add(card);
        Position(card);
    }

    private void Relayout(bool animated)
    {
        var width = WidthDip;
        rope.Layout(width);
        Canvas.SetLeft(hint, width / 2 - hint.DesiredSize.Width / 2);
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(hint, width / 2 - hint.DesiredSize.Width / 2);
        Canvas.SetTop(hint, Layout.RopeY(width / 2, width) + 34 - hint.DesiredSize.Height / 2);
        for (var i = 0; i < line.Items.Count; i++)
        {
            if (!cards.TryGetValue(line.Items[i].Id, out var card)) continue;
            var x = Layout.X(i, line.Items.Count, width, line.VisibleCount, line.Scroll);
            if (animated && Math.Abs(card.X.Target - x) > 0.01) card.X.Spring(x, 0.55, 0.78);
            else if (!animated) card.X.Set(x);
            Position(card);
        }
    }

    private void Position(PeggedCard card)
    {
        var width = WidthDip;
        var x = card.X.Value;
        var ropeY = Layout.RopeY(x, width);
        Canvas.SetLeft(card, x - Layout.CardWidth / 2);
        Canvas.SetTop(card, ropeY - Layout.PinAbove);
        card.EdgeFade = Math.Clamp(Math.Min(x, width - x) / 90, 0, 1);
    }

    private static FrameworkElement MakeHint()
    {
        var border = new Border
        {
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Theme.Pill),
            BorderBrush = Glass.Specular(),
            BorderThickness = new Thickness(0.75),
            IsHitTestVisible = false,
            Opacity = 0,
            Child = new TextBlock
            {
                Text = Strings.L("Take a screenshot and it will hang here"),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Theme.Secondary,
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, Opacity = 0.12, BlurRadius = 10, ShadowDepth = 2, Direction = 270 },
        };
        return border;
    }
}
