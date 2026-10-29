using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hangline.Core;

namespace Hangline.UI;

/// One photo with its clothespin. All the charm lives here: it drops onto
/// the line, swings, sways with the breeze and falls when you pull it off.
/// It also turns the mouse into the gestures: click copies, double click
/// opens, press and hold marks up, middle click takes a quick look, drag
/// sends it, right click shows the menu, the corner cross discards.
internal sealed class PeggedCard : Canvas
{
    public Pegged Item { get; }
    private readonly Line line;
    private static readonly Random Rng = new();

    private readonly RotateTransform rotate = new();
    private readonly TranslateTransform drop = new();
    private readonly ScaleTransform scale = new();
    private readonly Border photo;
    private readonly FrameworkElement card;
    private readonly FrameworkElement cross;
    private readonly FrameworkElement copiedPill;
    private readonly FrameworkElement? play;
    private readonly DropShadowEffect shadow;
    private readonly Grid cardHost;

    private readonly Anim swing, arrive, zoom, hoverK, crossK, pillK, fade;
    public readonly Anim X;
    private bool hovering, arrived;

    // Gesture state.
    private Point? downPoint;
    private bool startedDrag, didLongPress;
    private DispatcherTimer? holdTimer, clickTimer;
    public static bool IsDragging, IsShowingMenu;
    private const double HoldSeconds = 0.45;
    private const double CrossHit = 26;

    public PeggedCard(Pegged item, Line line, double x, bool alreadyArrived = false)
    {
        Item = item;
        this.line = line;
        Width = Layout.CardWidth;
        Height = Layout.PanelHeight;

        var p = Layout.PhotoSize(item.Thumb.PixelWidth, item.Thumb.PixelHeight);
        var photoRadius = Layout.FrameRadius - Layout.FrameInset;
        photo = new Border
        {
            Width = p.Width, Height = p.Height,
            CornerRadius = new CornerRadius(photoRadius),
            Background = PhotoBrush(item),
            BorderThickness = new Thickness(0.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255)),
        };
        var inner = new Grid { Margin = new Thickness(Layout.FrameInset) };
        inner.Children.Add(photo);
        if (item.IsRecording)
        {
            // A recording says so with a play button over its first frame.
            play = Glass.Circle(28, Glass.Play, 10);
            ((System.Windows.Shapes.Path)((Grid)play).Children[2]).Fill = Theme.Primary;
            ((System.Windows.Shapes.Path)((Grid)play).Children[2]).Margin = new Thickness(2, 0, 0, 0);
            inner.Children.Add(play);
        }
        var cs = Layout.CardSize(item.Thumb.PixelWidth, item.Thumb.PixelHeight);
        card = Glass.Wrap(inner, Layout.FrameRadius, cs.Width, cs.Height);
        shadow = new DropShadowEffect { Color = Colors.Black, Direction = 270, Opacity = 0.18, BlurRadius = 20, ShadowDepth = 5 };
        card.Effect = shadow;

        // A cross sends a file in Hangline's folder to the Recycle Bin; for a
        // file anywhere else it only hides it from the line.
        cross = line.IsInInbox(item.Id) ? Glass.Circle(20, Glass.Cross, 7) : Glass.Circle(20, Glass.EyeSlash, 10);
        cross.HorizontalAlignment = HorizontalAlignment.Left;
        cross.VerticalAlignment = VerticalAlignment.Top;
        cross.Margin = new Thickness(3);
        var crossScale = new ScaleTransform(0.6, 0.6, 10, 10);
        cross.RenderTransform = crossScale;
        cross.Opacity = 0;

        copiedPill = MakePill();

        cardHost = new Grid { Width = cs.Width, Height = cs.Height };
        cardHost.Children.Add(card);
        cardHost.Children.Add(cross);
        cardHost.RenderTransform = scale;
        scale.CenterX = cs.Width / 2;
        cardHost.Background = Brushes.Transparent;

        var clip = Clothespin.Make();
        var left = (Layout.CardWidth - cs.Width) / 2;
        SetLeft(cardHost, left);
        SetTop(cardHost, Layout.CardOffsetBelowTop);
        SetLeft(clip, Layout.CardWidth / 2 - 4.5);
        SetTop(clip, 0);
        SetLeft(copiedPill, 0);
        SetTop(copiedPill, Layout.CardOffsetBelowTop + cs.Height + 16 - 13);
        copiedPill.Width = Layout.CardWidth;
        Children.Add(cardHost);
        Children.Add(clip);
        Children.Add(copiedPill);

        // Tilted about the clip, where it grips the line.
        rotate.CenterX = Layout.CardWidth / 2;
        rotate.CenterY = 0;
        var group = new TransformGroup();
        group.Children.Add(rotate);
        group.Children.Add(drop);
        RenderTransform = group;

        swing = new Anim(0, v => rotate.Angle = v + Item.Tilt);
        arrive = new Anim(0, v => { drop.Y = -46 * (1 - v); UpdateOpacity(); });
        fade = new Anim(1, _ => UpdateOpacity());
        zoom = new Anim(1, v => { scale.ScaleX = scale.ScaleY = v; });
        hoverK = new Anim(0, v =>
        {
            shadow.Opacity = 0.18 + 0.08 * v;
            shadow.BlurRadius = 20 + 8 * v;
            shadow.ShadowDepth = 5 + 3 * v;
        });
        crossK = new Anim(0, v => { cross.Opacity = v; crossScale.ScaleX = crossScale.ScaleY = 0.6 + 0.4 * v; });
        pillK = new Anim(0, v =>
        {
            copiedPill.Opacity = v;
            copiedPill.RenderTransform = new TranslateTransform(0, -4 * (1 - v));
        });
        X = new Anim(x, _ => PositionChanged?.Invoke(this));

        cardHost.MouseEnter += (_, _) => SetHover(true);
        cardHost.MouseLeave += (_, _) => SetHover(false);
        cardHost.MouseLeftButtonDown += OnDown;
        cardHost.MouseMove += OnMove;
        cardHost.MouseLeftButtonUp += OnUp;
        cardHost.MouseRightButtonUp += OnRight;
        cardHost.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { line.QuickLook(Item.Id); e.Handled = true; } };

        arrived = alreadyArrived;
        Arrive();
    }

    public event Action<PeggedCard>? PositionChanged;

    /// The card's frame inside the line's canvas, for click-through and the flight.
    public Rect CardBounds(Visual root)
    {
        try { return cardHost.TransformToAncestor(root).TransformBounds(new Rect(0, 0, cardHost.Width, cardHost.Height)); }
        catch { return Rect.Empty; }
    }

    public void Refresh()
    {
        UpdateOpacity();
        var dragging = line.DraggingId == Item.Id;
        card.Opacity = dragging ? 0.45 : 1;
        var copied = line.CopiedId == Item.Id;
        if (copied && pillK.Target != 1) { pillK.Tween(1, 0.2); Nudge(3); }
        if (!copied && pillK.Target != 0) pillK.Tween(0, 0.2);
        var pressed = line.PressedId == Item.Id;
        var target = pressed ? 0.95 : hovering ? 1.035 : 1;
        if (zoom.Target != target)
        {
            if (pressed) zoom.Tween(target, 0.45, Ease.InOut);
            else zoom.Spring(target, 0.3, 0.6);
        }
        crossK.Tween(hovering && !dragging ? 1 : 0, 0.18);
    }

    private bool wasFlying;

    /// The photo's file was edited, or the size changed.
    public void UpdateThumb()
    {
        photo.Background = PhotoBrush(Item);
    }

    public void ItemChanged()
    {
        if (wasFlying && !Item.Flying)
        {
            // Landing after the flight: no jump, just a small sway from rest.
            fade.Set(0);
            fade.Tween(1, 0.16);
            Nudge(2.2);
        }
        wasFlying = Item.Flying;
        UpdateOpacity();
    }

    private void UpdateOpacity()
    {
        if (Item.Falling) { Opacity = 0; IsHitTestVisible = false; return; }
        if (Item.Flying) { Opacity = 0; return; }
        // The animations call this as they are made, before all of them exist.
        Opacity = Math.Clamp(arrive?.Value ?? 0, 0, 1) * (fade?.Value ?? 1) * EdgeFade;
    }

    /// Photos scrolled towards an edge fade out before they reach it.
    public double EdgeFade
    {
        get => edgeFade;
        set { if (Math.Abs(edgeFade - value) < 0.001) return; edgeFade = value; UpdateOpacity(); }
    }
    private double edgeFade = 1;

    private void Arrive()
    {
        wasFlying = Item.Flying;
        // A capture that flew in is already in place; the flight did the arriving.
        if (Item.Flying || arrived)
        {
            arrive.Set(1);
            arrived = true;
            return;
        }
        arrived = true;
        swing.Kick(16);
        arrive.Spring(1, 0.42, 0.72);
        swing.Interpolating(0, 46, 2.6);
    }

    public void Breeze()
    {
        var delay = Rng.NextDouble() * 0.35;
        Ui.After(delay, () => Nudge(1.6 + Rng.NextDouble() * 1.8));
    }

    private void Nudge(double degrees)
    {
        swing.Tween(degrees, 0.3, Ease.Out, () => swing.Interpolating(0, 38, 2.4));
    }

    private void SetHover(bool on)
    {
        if (hovering == on) return;
        hovering = on;
        hoverK.Tween(on ? 1 : 0, 0.18);
        Refresh();
    }

    private static Brush PhotoBrush(Pegged item)
    {
        var b = new ImageBrush(item.Thumb) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.HighQuality);
        b.Freeze();
        return b;
    }

    private static FrameworkElement MakePill()
    {
        var text = new TextBlock
        {
            Text = Strings.L("Copied"),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Primary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var check = new System.Windows.Shapes.Path
        {
            Data = Glass.Check, Stroke = Theme.Primary, StrokeThickness = 1.6, Width = 9, Height = 8,
            Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(check);
        row.Children.Add(text);
        var pill = new Border
        {
            Child = row,
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Theme.Pill),
            BorderBrush = Glass.Specular(),
            BorderThickness = new Thickness(0.75),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 8, ShadowDepth = 2, Direction = 270 },
        };
        var host = new Grid { IsHitTestVisible = false, Opacity = 0 };
        host.Children.Add(pill);
        return host;
    }

    // MARK: Gestures

    private bool InCross(Point p) => p.X <= CrossHit && p.Y <= CrossHit;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(cardHost);
        if (e.ClickCount > 1) { clickTimer?.Stop(); clickTimer = null; }
        if (InCross(p))
        {
            downPoint = null;
            line.Discard(Item.Id);
            return;
        }
        if (e.ClickCount == 2)
        {
            downPoint = null;
            line.Open(Item.Id);
            return;
        }
        downPoint = e.GetPosition(this);
        startedDrag = false;
        didLongPress = false;
        cardHost.CaptureMouse();
        line.SetPressed(Item.Id);
        holdTimer?.Stop();
        holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HoldSeconds) };
        holdTimer.Tick += (_, _) =>
        {
            holdTimer?.Stop();
            if (downPoint == null || startedDrag) return;
            didLongPress = true;
            line.SetPressed(null);
            cardHost.ReleaseMouseCapture();
            line.Markup(Item.Id);
        };
        holdTimer.Start();
    }

    private void EndPress()
    {
        holdTimer?.Stop();
        holdTimer = null;
        line.SetPressed(null);
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (downPoint is not { } start || startedDrag || didLongPress || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if ((p - start).Length <= 4) return;
        startedDrag = true;
        EndPress();
        cardHost.ReleaseMouseCapture();
        DragOut();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        cardHost.ReleaseMouseCapture();
        EndPress();
        if (downPoint != null && !startedDrag && !didLongPress && e.ClickCount == 1)
        {
            // A click copies only once it is clear it is not the first half
            // of a double click, which opens the photo instead.
            clickTimer?.Stop();
            var id = Item.Id;
            var l = line;
            clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Native.GetDoubleClickTime()) };
            clickTimer.Tick += (_, _) =>
            {
                clickTimer?.Stop();
                clickTimer = null;
                l.Copy(id);
            };
            clickTimer.Start();
        }
        downPoint = null;
        didLongPress = false;
    }

    /// An app gets a copy and the photo stays; a folder keeps the file and
    /// the photo leaves the line; the Recycle Bin discards it.
    private void DragOut()
    {
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { Item.Path });
        var ghost = new DragGhost(Item.Thumb, Layout.PhotoSize(Item.Thumb.PixelWidth, Item.Thumb.PixelHeight));
        ghost.Show();
        // Drag and drop runs its own loop; the feedback callback keeps the
        // ghost under the pointer.
        GiveFeedbackEventHandler follow = (_, e) => ghost.Follow();
        GiveFeedback += follow;
        IsDragging = true;
        line.SetDragging(Item.Id);
        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Log.Write($"Drag failed: {ex.Message}");
        }
        GiveFeedback -= follow;
        ghost.Close();
        IsDragging = false;
        startedDrag = false;
        downPoint = null;
        line.SetDragging(null);
        // Moved into a folder: it is saved where you wanted it. Explorer can
        // finish a move a moment later, so look again then.
        line.Prune();
        Ui.After(0.6, line.Prune);
        Ui.After(2.0, line.Prune);
    }

    private void OnRight(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var id = Item.Id;
        var menu = Menus.New();
        void Add(string title, Action action, bool enabled = true) =>
            menu.Items.Add(Menus.Item(Strings.L(title), action, enabled));
        Add("Copy", () => line.Copy(id));
        Add("Open", () => line.Open(id));
        Add("Quick Look", () => line.QuickLook(id));
        if (!Item.IsRecording) Add("Markup", () => line.Markup(id));
        Add("Show in Explorer", () => line.Reveal(id));
        var inInbox = line.IsInInbox(id);
        if (inInbox) Add("Save to Desktop", () => line.SaveToDesktop(id));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        if (inInbox) Add("Discard", () => line.Discard(id));
        else
        {
            Add("Take down", () => line.Discard(id));
            Add("Move to Recycle Bin", () => line.Trash(id));
        }
        Add("Take down everything to the left", () => line.TakeDown(id, true), line.Neighbours(id, true).Count > 0);
        Add("Take down everything to the right", () => line.TakeDown(id, false), line.Neighbours(id, false).Count > 0);
        // The menu reaches past the line, so the line waits while it is open.
        IsShowingMenu = true;
        menu.Closed += (_, _) => { IsShowingMenu = false; Ui.After(0.5, menu.Dispose); };
        var c = Native.Cursor;
        menu.Show(new System.Drawing.Point(c.X, c.Y));
    }
}
