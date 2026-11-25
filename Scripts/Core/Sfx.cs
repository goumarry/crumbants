// ─────────────────────────────────────────────────────────────
// Sons synthétisés au démarrage (aucun fichier audio) : croc,
// pièce, saut, sac plein, achat, trouvaille, fanfare, éboulement.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;

namespace Crumbants;

public partial class Sfx : Node
{
    static Sfx _inst;
    const int Rate = 44100;
    readonly Dictionary<string, AudioStreamWav[]> _bank = new();
    readonly List<AudioStreamPlayer> _pool = new();
    int _next;
    public static bool Muted;
    readonly Random _rng = new(3);

    public override void _Ready()
    {
        _inst = this;
        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master" };
            AddChild(p);
            _pool.Add(p);
        }
        _bank["crunch"] = new[] { Crunch(0), Crunch(1), Crunch(2), Crunch(3) };
        _bank["coin"] = new[] { Notes(new[] { 1318.5, 1975.5 }, 0.07, 0.35, 0.5) };
        _bank["jump"] = new[] { Sweep(320, 640, 0.09, 0.25) };
        _bank["full"] = new[] { Notes(new[] { 220.0, 196.0 }, 0.12, 0.25, 0.4, square: true) };
        _bank["buy"] = new[] { Notes(new[] { 784.0, 988.0, 1175.0, 1568.0 }, 0.06, 0.3, 0.45) };
        _bank["find"] = new[] { Notes(new[] { 1568.0, 2093.0, 2637.0, 3136.0 }, 0.05, 0.5, 0.35) };
        _bank["fanfare"] = new[] { Notes(new[] { 523.3, 659.3, 784.0, 1046.5, 784.0, 1046.5 }, 0.13, 0.9, 0.45, square: true) };
        _bank["rumble"] = new[] { Rumble() };
        _bank["error"] = new[] { Notes(new[] { 180.0 }, 0.15, 0.1, 0.35, square: true) };
        _bank["pickup"] = new[] { Sweep(500, 1400, 0.18, 0.35) };
    }

    public static void Play(string name, float volDb = 0, float pitch = 1)
    {
        if (_inst == null || Muted || !_inst._bank.TryGetValue(name, out var v)) return;
        var p = _inst._pool[_inst._next++ % _inst._pool.Count];
        p.Stream = v[_inst._rng.Next(v.Length)];
        p.VolumeDb = volDb + Mathf.LinearToDb(Mathf.Max(0.0001f, UI.Settings.Effects));
        p.PitchScale = pitch * (0.94f + 0.12f * (float)_inst._rng.NextDouble());
        p.Play();
    }

    static AudioStreamWav Make(float[] s)
    {
        var data = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            short v = (short)(Math.Clamp(s[i], -1f, 1f) * 32000);
            data[i * 2] = (byte)(v & 0xff);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }

    AudioStreamWav Crunch(int seed)
    {
        var r = new Random(seed * 77 + 5);
        int n = (int)(Rate * 0.11);
        var s = new float[n];
        float lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = MathF.Exp(-t * 38) * (1 - MathF.Exp(-t * 900));
            // craquements : impulsions aléatoires
            float x = (float)(r.NextDouble() * 2 - 1);
            if (r.NextDouble() < 0.02) x *= 4;
            lp += (x - lp) * 0.35f;
            lp2 += (lp - lp2) * 0.5f;
            s[i] = (lp - lp2 * 0.6f) * env * 0.9f;
        }
        return Make(s);
    }

    static AudioStreamWav Notes(double[] freqs, double step, double tail, double vol, bool square = false)
    {
        int n = (int)(Rate * (step * freqs.Length + tail));
        var s = new float[n];
        for (int k = 0; k < freqs.Length; k++)
        {
            int start = (int)(Rate * step * k);
            for (int i = start; i < n; i++)
            {
                double t = (i - start) / (double)Rate;
                double env = Math.Exp(-t * 7) * (1 - Math.Exp(-t * 400));
                double ph = 2 * Math.PI * freqs[k] * t;
                double w = square ? Math.Sign(Math.Sin(ph)) * 0.35 + Math.Sin(ph) * 0.5 : Math.Sin(ph) + 0.3 * Math.Sin(ph * 2.01);
                s[i] += (float)(w * env * vol * 0.6);
            }
        }
        return Make(s);
    }

    static AudioStreamWav Sweep(double f0, double f1, double dur, double vol)
    {
        int n = (int)(Rate * dur);
        var s = new float[n];
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n;
            ph += 2 * Math.PI * (f0 + (f1 - f0) * t) / Rate;
            s[i] = (float)(Math.Sin(ph) * (1 - t) * vol);
        }
        return Make(s);
    }

    static AudioStreamWav Rumble()
    {
        var r = new Random(9);
        int n = (int)(Rate * 1.2);
        var s = new float[n];
        float lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp += ((float)(r.NextDouble() * 2 - 1) - lp) * 0.02f;
            lp2 += (lp - lp2) * 0.05f;
            s[i] = lp2 * 9 * MathF.Exp(-t * 2.5f) * (1 - MathF.Exp(-t * 30));
        }
        return Make(s);
    }
}
