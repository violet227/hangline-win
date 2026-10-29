using System.IO;
using System.Media;

namespace Hangline.Core;

/// Three small sounds, synthesised at launch so the app ships no audio:
/// a bright tink when a photo hangs, a soft pop when it is taken down, and
/// a short paper crumple when it goes to the Recycle Bin.
internal static class Sounds
{
    public static readonly byte[] Tink = Make(0.16, t =>
        Math.Sin(2 * Math.PI * 2093 * t) * Math.Exp(-t * 32) * 0.35
        + Math.Sin(2 * Math.PI * 3136 * t) * Math.Exp(-t * 45) * 0.15);

    public static readonly byte[] Pop = Make(0.09, t =>
        Math.Sin(2 * Math.PI * (520 - 2600 * t) * t) * Math.Exp(-t * 55) * 0.4);

    private static readonly Random Rng = new(7);
    public static readonly byte[] Crumple = Make(0.28, t =>
        (Rng.NextDouble() * 2 - 1) * Math.Exp(-t * 14) * 0.22 * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 23 * t)));

    public static void Play(byte[] wav, bool on)
    {
        if (!on) return;
        // SoundPlayer plays one sound at a time per player; a new one each time overlaps fine.
        Task.Run(() =>
        {
            try
            {
                using var player = new SoundPlayer(new MemoryStream(wav));
                player.PlaySync();
            }
            catch { }
        });
    }

    /// A mono 16-bit PCM WAV of the given wave, with a tiny fade in.
    private static byte[] Make(double seconds, Func<double, double> wave)
    {
        const int rate = 44100;
        var n = (int)(rate * seconds);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(n * 2);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / rate;
            var fade = Math.Min(1, i / 60.0);
            w.Write((short)(Math.Clamp(wave(t) * fade, -1, 1) * short.MaxValue));
        }
        return ms.ToArray();
    }
}
