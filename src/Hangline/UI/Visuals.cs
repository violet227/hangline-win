using System.Windows;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace Hangline.UI;

/// Light or dark, following the Windows app theme, so the glass and the
/// text read the way the system's own surfaces do.
internal static class Theme
{
    public static bool IsDark
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
    }

    public static bool TaskbarIsDark
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is not int v || v == 0;
        }
    }

    public static Color GlassFill => IsDark ? Color.FromArgb(0xC8, 0x2C, 0x2C, 0x30) : Color.FromArgb(0xD0, 0xF6, 0xF6, 0xF8);
    public static Color Pill => IsDark ? Color.FromArgb(0xEC, 0x2C, 0x2C, 0x2E) : Color.FromArgb(0xEE, 0xF4, 0xF4, 0xF6);
    public static Brush Primary => new SolidColorBrush(IsDark ? Color.FromRgb(0xF2, 0xF2, 0xF7) : Color.FromRgb(0x1C, 0x1C, 0x1E));
    public static Brush Secondary => new SolidColorBrush(IsDark ? Color.FromRgb(0x98, 0x98, 0x9F) : Color.FromRgb(0x6E, 0x6E, 0x73));
}

internal static class Glass
{
    /// A crisp glass: a pale translucent fill with a thin specular edge, lit
    /// from above, and a faint dark rim so it reads on light backgrounds.
    public static FrameworkElement Wrap(UIElement child, double radius, double w, double h)
    {
        var outer = new System.Windows.Controls.Border
        {
            Width = w + 1, Height = h + 1,
            CornerRadius = new CornerRadius(radius + 0.5),
            BorderThickness = new Thickness(0.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            Margin = new Thickness(-0.5),
        };
        var inner = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = new SolidColorBrush(Theme.GlassFill),
            BorderThickness = new Thickness(0.75),
            BorderBrush = Specular(),
            Child = child,
        };
        outer.Child = inner;
        return outer;
    }

    public static Brush Specular() => new LinearGradientBrush(
        Color.FromArgb(0x8C, 255, 255, 255), Color.FromArgb(0x1F, 255, 255, 255), 90);

    /// A small round glass button with a glyph in it.
    public static FrameworkElement Circle(double size, Geometry glyph, double glyphSize)
    {
        var path = new Path
        {
            Data = glyph,
            Stroke = Theme.Primary,
            Fill = null,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = glyphSize, Height = glyphSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var grid = new System.Windows.Controls.Grid { Width = size, Height = size, IsHitTestVisible = false };
        grid.Children.Add(new Ellipse { Fill = new SolidColorBrush(Theme.GlassFill), Stroke = Specular(), StrokeThickness = 0.75 });
        grid.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)), StrokeThickness = 0.5, Margin = new Thickness(-0.5) });
        grid.Children.Add(path);
        grid.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.18, BlurRadius = 6, ShadowDepth = 1, Direction = 270 };
        return grid;
    }

    public static readonly Geometry Cross = Geometry.Parse("M0,0 L10,10 M10,0 L0,10");
    /// An eye with a slash: for a file that only leaves the line.
    public static readonly Geometry EyeSlash = Geometry.Parse("M0,5 C3,0 9,0 12,5 C9,10 3,10 0,5 Z M6,3.2 A1.8,1.8 0 1 1 6,6.8 A1.8,1.8 0 1 1 6,3.2 M1,0 L11,10");
    public static readonly Geometry Play = Geometry.Parse("M0,0 L9,5.5 L0,11 Z");
    public static readonly Geometry Check = Geometry.Parse("M0,5 L3.5,8.5 L10,1");
}

/// A thin, neutral line: a mid grey core with a faint highlight and a soft
/// shadow, so it reads on light and dark backgrounds alike. It fades out at
/// both ends so it seems to come from beyond the screen.
internal sealed class Rope : System.Windows.Controls.Canvas
{
    private readonly Path shadow, core, highlight;

    public Rope()
    {
        IsHitTestVisible = false;
        shadow = new Path { Stroke = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), StrokeThickness = 1.4,
            Effect = new BlurEffect { Radius = 2.4 }, RenderTransform = new TranslateTransform(0, 1.2) };
        core = new Path { Stroke = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C)), StrokeThickness = 1.2 };
        highlight = new Path { Stroke = new SolidColorBrush(Color.FromArgb(0x73, 255, 255, 255)), StrokeThickness = 0.4,
            RenderTransform = new TranslateTransform(0, -0.35) };
        Children.Add(shadow);
        Children.Add(core);
        Children.Add(highlight);
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.08));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.92));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        OpacityMask = fade;
    }

    public void Layout(double width)
    {
        Width = width;
        Height = Hangline.UI.Layout.RopeTop + 2 * Hangline.UI.Layout.Sag(width) + 8;
        var top = Hangline.UI.Layout.RopeTop;
        var fig = new PathFigure { StartPoint = new Point(-20, top) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(width / 2, top + 2 * Hangline.UI.Layout.Sag(width)), new Point(width + 20, top), true));
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        shadow.Data = core.Data = highlight.Data = geo;
    }
}

/// A minimal aluminium clip: a brushed metal pill with a slot where it grips
/// the line, and a soft shadow so it reads on any background.
internal static class Clothespin
{
    public static FrameworkElement Make()
    {
        var metal = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        metal.GradientStops.Add(new GradientStop(Gray(0.70), 0));
        metal.GradientStops.Add(new GradientStop(Gray(0.93), 0.35));
        metal.GradientStops.Add(new GradientStop(Gray(0.82), 0.65));
        metal.GradientStops.Add(new GradientStop(Gray(0.62), 1));
        var grid = new System.Windows.Controls.Grid { Width = 9, Height = Layout.ClipHeight, IsHitTestVisible = false };
        grid.Children.Add(new Rectangle
        {
            RadiusX = 3.5, RadiusY = 3.5, Fill = metal, StrokeThickness = 0.6,
            Stroke = new LinearGradientBrush(Color.FromArgb(0xE6, 255, 255, 255), Color.FromArgb(0x2E, 0, 0, 0), 90),
        });
        // The slot the line passes through.
        grid.Children.Add(new Rectangle
        {
            Width = 5, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7,
            Fill = new SolidColorBrush(Color.FromArgb(0x52, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8.5, 0, 0),
        });
        grid.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.30, BlurRadius = 4, ShadowDepth = 1.5, Direction = 270 };
        return grid;
    }

    private static Color Gray(double w) { var b = (byte)(w * 255); return Color.FromRgb(b, b, b); }
}
