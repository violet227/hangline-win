using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Hangline.Core;

namespace Hangline.UI;

/// A quick look at a photo without opening it. The arrow keys go through
/// the rest of the line, and Space or Escape closes it. Recordings play.
internal sealed class PreviewWindow : Window
{
    private static PreviewWindow? open;
    private readonly List<string> paths;
    private int index;
    private readonly Grid stage = new();
    private readonly TextBlock caption = new();

    public static void Show(List<string> paths, int index)
    {
        open?.Close();
        open = new PreviewWindow(paths, index);
        open.Closed += (_, _) => open = null;
        open.Show();
        open.Activate();
    }

    private PreviewWindow(List<string> paths, int index)
    {
        this.paths = paths;
        this.index = index;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Title = Settings.AppName;

        var area = SystemParameters.WorkArea;
        var mon = Native.MonitorInfo(Native.MonitorUnderCursor()).rcWork;
        var s = Native.Scale(Native.MonitorUnderCursor());
        area = new Rect(mon.Left / s, mon.Top / s, mon.Width / s, mon.Height / s);
        Width = area.Width * 0.72;
        Height = area.Height * 0.78;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;

        caption.Foreground = Theme.Secondary;
        caption.FontSize = 12;
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, 10, 0, 12);
        var layout = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(caption, Dock.Bottom);
        layout.Children.Add(caption);
        layout.Children.Add(stage);
        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Theme.IsDark ? Color.FromArgb(0xF2, 0x1E, 0x1E, 0x20) : Color.FromArgb(0xF4, 0xFA, 0xFA, 0xFC)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(20),
            Child = layout,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.35, BlurRadius = 30, ShadowDepth = 8, Direction = 270 },
        };

        KeyDown += OnKey;
        Deactivated += (_, _) => Close();
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { Shell.Open(paths[this.index]); Close(); } else DragMove(); };
        Load();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape or Key.Space: Close(); break;
            case Key.Left or Key.Up when index > 0: index--; Load(); break;
            case Key.Right or Key.Down when index < paths.Count - 1: index++; Load(); break;
            case Key.Enter: Shell.Open(paths[index]); Close(); break;
        }
    }

    private void Load()
    {
        stage.Children.Clear();
        var path = paths[index];
        caption.Text = $"{System.IO.Path.GetFileName(path)}   {index + 1} / {paths.Count}";
        if (Thumbnails.IsRecording(path))
        {
            var media = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Play, Stretch = Stretch.Uniform };
            stage.Children.Add(media);
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.EndInit();
            }
            var img = new Image { Source = bmp, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            stage.Children.Add(img);
        }
        catch (Exception e)
        {
            stage.Children.Add(new TextBlock { Text = e.Message, Foreground = Theme.Secondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
    }
}

/// Records a new shortcut: the next key pressed with at least one modifier.
internal sealed class ShortcutRecorder : Window
{
    private Action<Shortcut?, bool>? done;

    /// Calls back with the new shortcut, null to remove it, and whether
    /// anything changed at all.
    public static void Record(Action<Shortcut?, bool> done)
    {
        var w = new ShortcutRecorder { done = done };
        w.Show();
        w.Activate();
    }

    private ShortcutRecorder()
    {
        Title = Strings.L("Change Shortcut");
        Width = 340;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = Strings.L("Press the new shortcut"), FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = Strings.L("Escape cancels. Delete removes the shortcut."), Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        Content = panel;
        PreviewKeyDown += OnKey;
        Closed += (_, _) => done?.Invoke(null, false);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { Close(); return; }
        if (key is Key.Delete or Key.Back) { Finish(null, true); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        uint mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;
        // A plain key would steal it from every app.
        if (mods == 0 || mods == Native.MOD_SHIFT) { System.Media.SystemSounds.Beep.Play(); return; }
        Finish(new Shortcut(mods, (uint)KeyInterop.VirtualKeyFromKey(key)), true);
    }

    private void Finish(Shortcut? s, bool changed)
    {
        var d = done;
        done = null;
        Close();
        d?.Invoke(s, changed);
    }
}
