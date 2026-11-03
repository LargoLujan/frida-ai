using System.IO;

namespace TeamsJarvis.UI;

/// <summary>
/// Ladrido suave SINTETIZADO (no depende de ningún archivo de audio): un "woof" corto y suave que
/// suena cuando la mascota habla o llega una notificación, si el usuario lo activa. Con throttle
/// para no ser pesado.
/// </summary>
public static class BarkPlayer
{
    public static bool Enabled;
    private static DateTime _last = DateTime.MinValue;
    private static byte[]? _wav;

    public static void Bark()
    {
        if (!Enabled) return;
        var now = DateTime.UtcNow;
        if ((now - _last).TotalMilliseconds < 700) return; // no encadenar ladridos
        _last = now;
        try
        {
            _wav ??= BuildBarkWav();
            var player = new System.Media.SoundPlayer { Stream = new MemoryStream(_wav) };
            player.Load();
            player.Play(); // reproduce en segundo plano
        }
        catch { /* el sonido nunca debe tumbar la app */ }
    }

    private static byte[] BuildBarkWav()
    {
        const int rate = 22050;
        const double dur = 0.22;
        var n = (int)(rate * dur);
        var samples = new short[n];
        var rnd = new Random(7);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / rate;
            var p = t / dur;
            var freq = 300 * Math.Exp(-3.2 * p) + 120;                 // cae de ~420 Hz a ~120 Hz
            var env = Math.Min(1.0, t / 0.008) * Math.Exp(-4.5 * p);   // ataque rápido + decaimiento
            var s = Math.Sin(2 * Math.PI * freq * t) * 0.7
                  + Math.Sin(2 * Math.PI * freq * 2 * t) * 0.25;       // 2º armónico
            s += (rnd.NextDouble() * 2 - 1) * 0.08;                     // textura
            var v = s * env * 0.28;                                     // volumen suave
            samples[i] = (short)(Math.Clamp(v, -1, 1) * short.MaxValue);
        }
        return WriteWav(samples, rate);
    }

    private static byte[] WriteWav(short[] samples, int rate)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var dataLen = samples.Length * 2;
        bw.Write("RIFF".ToCharArray()); bw.Write(36 + dataLen); bw.Write("WAVE".ToCharArray());
        bw.Write("fmt ".ToCharArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data".ToCharArray()); bw.Write(dataLen);
        foreach (var s in samples) bw.Write(s);
        bw.Flush();
        return ms.ToArray();
    }
}
