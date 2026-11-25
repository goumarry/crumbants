// ─────────────────────────────────────────────────────────────
// Musique d'ambiance et sons de la nature, générés au démarrage :
// une boucle douce (nappes, marimba en arpèges, légère percussion),
// le vent dans l'herbe et des oiseaux de temps en temps.
// ─────────────────────────────────────────────────────────────
using System;
using Godot;

namespace Crumbants;

public partial class Music : Node
{
    const int Rate = 22050;
    AudioStreamPlayer _music, _wind, _bird;
    AudioStreamWav[] _birds;
    readonly Random _rng = new(11);
    float _birdT = 4;
    public float Underground; // 0..1 : sous terre, la nature s'étouffe

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _music = new AudioStreamPlayer { Stream = MakeLoop(), Autoplay = false };
        _wind = new AudioStreamPlayer { Stream = MakeWind() };
        _bird = new AudioStreamPlayer();
        AddChild(_music); AddChild(_wind); AddChild(_bird);
        _birds = new[] { MakeBird(0), MakeBird(1), MakeBird(2) };
        _music.Play(); _wind.Play();
        Apply();
    }

    public void Apply()
    {
        float m = Mathf.Max(0.0001f, UI.Settings.Music), e = Mathf.Max(0.0001f, UI.Settings.Effects);
        _music.VolumeDb = Mathf.LinearToDb(m * 0.55f);
        _wind.VolumeDb = Mathf.LinearToDb(e * 0.22f * (1 - 0.7f * Underground) + 0.0001f);
    }

    public override void _Process(double delta)
    {
        Apply();
        _birdT -= (float)delta;
        if (_birdT <= 0)
        {
            _birdT = 5 + (float)_rng.NextDouble() * 9;
            if (Underground < 0.5f)
            {
                _bird.Stream = _birds[_rng.Next(_birds.Length)];
                _bird.PitchScale = 0.9f + 0.25f * (float)_rng.NextDouble();
                _bird.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, UI.Settings.Effects) * 0.25f);
                _bird.Play();
            }
        }
    }

    static AudioStreamWav Wav(float[] l, float[] r, bool loop)
    {
        var data = new byte[l.Length * 4];
        for (int i = 0; i < l.Length; i++)
        {
            short a = (short)(Math.Clamp(l[i], -1f, 1f) * 30000), b = (short)(Math.Clamp(r[i], -1f, 1f) * 30000);
            data[i * 4] = (byte)a; data[i * 4 + 1] = (byte)(a >> 8); data[i * 4 + 2] = (byte)b; data[i * 4 + 3] = (byte)(b >> 8);
        }
        var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = true, Data = data };
        if (loop) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = l.Length; }
        return w;
    }

    static double Midi(double n) => 440 * Math.Pow(2, (n - 69) / 12);

    // 4 accords × 2 mesures à 84 bpm : Do maj7 – La m7 – Fa maj7 – Sol 6
    static AudioStreamWav MakeLoop()
    {
        double bpm = 84, beat = 60 / bpm;
        int bars = 8;
        int n = (int)(Rate * beat * 4 * bars);
        var L = new float[n]; var R = new float[n];
        int[][] chords = { new[] { 48, 55, 59, 64 }, new[] { 45, 52, 55, 60 }, new[] { 41, 48, 52, 57 }, new[] { 43, 50, 52, 59 } };
        int[] scale = { 60, 62, 64, 67, 69, 72, 74, 76, 79 };
        var rng = new Random(5);
        // nappes
        for (int b = 0; b < bars; b++)
        {
            var ch = chords[(b / 2) % 4];
            int s0 = (int)(Rate * beat * 4 * b), s1 = (int)(Rate * beat * 4 * (b + 1));
            foreach (int note in ch)
            {
                double f = Midi(note), ph = rng.NextDouble();
                for (int i = s0; i < s1 + Rate / 2 && i < n; i++)
                {
                    double t = (i - s0) / (double)Rate, T = (s1 - s0) / (double)Rate;
                    double env = Math.Min(1, t / 0.6) * Math.Min(1, Math.Max(0, (T + 0.5 - t) / 0.6));
                    double v = Math.Sin(2 * Math.PI * (f * t + ph)) * 0.6 + Math.Sin(2 * Math.PI * (f * 2.003 * t + ph)) * 0.15;
                    float a = (float)(v * env * 0.035);
                    L[i] += a * (note % 2 == 0 ? 1.1f : 0.9f); R[i] += a * (note % 2 == 0 ? 0.9f : 1.1f);
                }
            }
        }
        // marimba : arpèges en croches, quelques silences
        int eighths = bars * 8;
        for (int k = 0; k < eighths; k++)
        {
            if (rng.NextDouble() < 0.28) continue;
            var ch = chords[(k / 16) % 4];
            int note = rng.NextDouble() < 0.6 ? ch[rng.Next(ch.Length)] + 12 : scale[rng.Next(scale.Length)];
            double f = Midi(note);
            int s0 = (int)(Rate * beat / 2 * k);
            float pan = (float)rng.NextDouble();
            for (int i = s0; i < s0 + Rate && i < n; i++)
            {
                double t = (i - s0) / (double)Rate;
                double env = Math.Exp(-t * 6) * (1 - Math.Exp(-t * 300));
                double v = Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(2 * Math.PI * f * 4 * t) * Math.Exp(-t * 20);
                float a = (float)(v * env * 0.09);
                L[i] += a * (1.2f - pan * 0.4f); R[i] += a * (0.8f + pan * 0.4f);
            }
        }
        // petite percussion (balai) sur les temps 2 et 4
        for (int k = 0; k < bars * 4; k++)
        {
            if (k % 2 == 0) continue;
            int s0 = (int)(Rate * beat * k);
            for (int i = s0; i < s0 + Rate / 6 && i < n; i++)
            {
                double t = (i - s0) / (double)Rate;
                float a = (float)((rng.NextDouble() * 2 - 1) * Math.Exp(-t * 35) * 0.025);
                L[i] += a; R[i] += a;
            }
        }
        return Wav(L, R, true);
    }

    static AudioStreamWav MakeWind()
    {
        int n = Rate * 12;
        var L = new float[n]; var R = new float[n];
        var rng = new Random(2);
        float a = 0, b = 0, c = 0, d = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            float gust = (float)(0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 12) * Math.Sin(2 * Math.PI * t / 4 + 1));
            a += ((float)(rng.NextDouble() * 2 - 1) - a) * 0.02f; b += (a - b) * 0.05f;
            c += ((float)(rng.NextDouble() * 2 - 1) - c) * 0.02f; d += (c - d) * 0.05f;
            L[i] = b * 6 * (0.3f + 0.7f * gust); R[i] = d * 6 * (0.3f + 0.7f * gust);
        }
        // fondu pour une boucle sans clic
        int f = Rate / 2;
        for (int i = 0; i < f; i++) { float k = i / (float)f; L[n - f + i] = L[n - f + i] * (1 - k) + L[i] * k; R[n - f + i] = R[n - f + i] * (1 - k) + R[i] * k; }
        return Wav(L, R, true);
    }

    static AudioStreamWav MakeBird(int kind)
    {
        var rng = new Random(kind * 13 + 1);
        int notes = 3 + rng.Next(5);
        int n = (int)(Rate * (0.12 * notes + 0.3));
        var L = new float[n]; var R = new float[n];
        double ph = 0;
        for (int k = 0; k < notes; k++)
        {
            int s0 = (int)(Rate * 0.12 * k);
            double f0 = 2600 + rng.NextDouble() * 1500, f1 = f0 + (rng.NextDouble() - 0.3) * 1800;
            for (int i = s0; i < s0 + Rate * 0.09 && i < n; i++)
            {
                double t = (i - s0) / (Rate * 0.09);
                ph += 2 * Math.PI * (f0 + (f1 - f0) * t) / Rate;
                float a = (float)(Math.Sin(ph) * Math.Sin(Math.PI * t) * 0.5);
                L[i] += a * 0.7f; R[i] += a;
            }
        }
        return Wav(L, R, false);
    }
}
