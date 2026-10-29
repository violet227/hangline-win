using Hangline.Core;

namespace Hangline.UI;

/// Sizes and the shape of the line, in device-independent pixels. The same
/// numbers as the original's Layout.
internal static class Layout
{
    public enum Size { Small, Medium, Large }

    public static Size Current
    {
        get => Settings.Current.LineSize switch { "small" => Size.Small, "large" => Size.Large, _ => Size.Medium };
        set
        {
            Settings.Current.LineSize = value.ToString().ToLowerInvariant();
            Settings.Current.Save();
        }
    }

    public static double Scale => Current switch { Size.Small => 0.8, Size.Large => 1.35, _ => 1 };

    public static double PhotoMaxHeight => Math.Round(104 * Scale);
    public static double CardWidth => Math.Round(150 * Scale);
    public static double Spacing => CardWidth + 24;
    public static double PanelHeight => PhotoMaxHeight + 106;
    public const double RopeTop = 10;
    public const double PinAbove = 9.5;

    // Glass frame around each photo.
    public const double FrameRadius = 16;
    public const double FrameInset = 4;

    // The clip: 26 tall, 12 of them over the card.
    public const double ClipHeight = 26;
    public const double ClipOverlap = 12;
    public const double CardOffsetBelowTop = ClipHeight - ClipOverlap;

    /// The rope hangs as a parabola from edge to edge of the screen.
    public static double Sag(double width) => Math.Min(30, width * 0.018);

    public static double RopeY(double x, double width)
    {
        if (width <= 0) return RopeTop;
        var f = x / width;
        return RopeTop + 4 * Sag(width) * f * (1 - f);
    }

    /// Photos hang Spacing apart, centred. With Keep on line set and more
    /// than fit, they keep their spacing and Scroll brings older ones in from
    /// the left; otherwise they move closer so none is past the edge.
    public static double X(int index, int count, double width, int visible, double scroll)
    {
        if (Settings.Current.KeepOnLine != 0 && count > visible)
        {
            var newest = width / 2 + (visible - 1) * Spacing / 2;
            return newest - (count - 1 - index) * Spacing + scroll;
        }
        var gaps = Math.Max(count - 1, 0);
        var step = gaps > 0 ? Math.Min(Spacing, Math.Max(0, width - 200) / gaps) : Spacing;
        return width / 2 - gaps * step / 2 + index * step;
    }

    /// The photo fits inside the card keeping its proportions.
    public static System.Windows.Size PhotoSize(double w, double h)
    {
        double maxW = CardWidth - 14, maxH = PhotoMaxHeight;
        if (w <= 0 || h <= 0) return new(maxW, maxH);
        var s = Math.Min(maxW / w, maxH / h);
        return new(w * s, h * s);
    }

    public static System.Windows.Size CardSize(double w, double h)
    {
        var p = PhotoSize(w, h);
        return new(p.Width + FrameInset * 2, p.Height + FrameInset * 2);
    }
}
