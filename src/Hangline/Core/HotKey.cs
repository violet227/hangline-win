using System.Windows.Input;
using System.Windows.Interop;

namespace Hangline.Core;

/// The shortcut that shows and hides the line: Ctrl+Alt+T unless changed,
/// or none.
internal readonly record struct Shortcut(uint Modifiers, uint Vk)
{
    public static readonly Shortcut Standard = new(Native.MOD_CONTROL | Native.MOD_ALT, (uint)KeyInterop.VirtualKeyFromKey(Key.T));

    public static Shortcut? Current
    {
        get
        {
            var s = Settings.Current;
            if (s.HotKeyVk == null) return Standard;
            if (s.HotKeyVk == 0) return null;
            return new Shortcut(s.HotKeyMods ?? 0, s.HotKeyVk.Value);
        }
        set
        {
            Settings.Current.HotKeyVk = value?.Vk ?? 0;
            Settings.Current.HotKeyMods = value?.Modifiers ?? 0;
            Settings.Current.Save();
        }
    }

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
            var key = KeyInterop.KeyFromVirtualKey((int)Vk);
            parts.Add(key switch
            {
                >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                Key.Space => "Space",
                _ => key.ToString(),
            });
            return string.Join("+", parts);
        }
    }
}

/// A single global hot key through RegisterHotKey, on a message-only window.
internal sealed class HotKey : IDisposable
{
    private const int Id = 0x7E4D;
    private readonly HwndSource source;
    private readonly Action action;
    public bool Registered { get; private set; }

    public HotKey(Shortcut shortcut, Action action)
    {
        this.action = action;
        source = new HwndSource(new HwndSourceParameters("HanglineHotKey") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
        Registered = Native.RegisterHotKey(source.Handle, Id, shortcut.Modifiers | Native.MOD_NOREPEAT, shortcut.Vk);
        if (!Registered) Log.Write($"Could not register {shortcut.Display}");
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (Registered) Native.UnregisterHotKey(source.Handle, Id);
        source.Dispose();
    }
}
