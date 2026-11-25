// ─────────────────────────────────────────────────────────────
// Garnitures en relief posées sur le dessus des gâteaux : rosaces de
// chantilly et cerises (forêt-noire), demi-fraises (fraisier),
// macarons (matcha), meringues toastées (citron), myrtilles.
// Chaque garniture est accrochée à une colonne du gâteau : elle
// disparaît quand cette colonne est croquée, ou quand la fourmi
// la touche (elle la gobe au passage, petite gerbe de miettes).
// Maillages construits ici (SurfaceTool), affichés par MultiMesh.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public partial class CakeToppings : Node3D
{
    sealed class Kind
    {
        public MultiMesh Mm;
        public readonly List<(Transform3D t, Color c, int col, float r, float top)> Items = new();
        public bool[] Gone;
        public Color Burst;
    }

    CakeSim _k;
    readonly List<Kind> _kinds = new();
    float _check;

    public static CakeToppings Create(CakeSim k)
    {
        var t = new CakeToppings { Name = $"Toppings{k.N}", _k = k };
        t.Build();
        return t;
    }

    // ───── maillages ─────
    static ArrayMesh Lathe(int nu, int nv, Func<float, float, Vector3> pos, Func<float, float, Color> col)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = 0; j < nv; j++)
        for (int i = 0; i < nu; i++)
        {
            float u0 = i / (float)nu, u1 = (i + 1) / (float)nu, v0 = j / (float)nv, v1 = (j + 1) / (float)nv;
            void T(float ua, float va, float ub, float vb, float uc, float vc)
            {
                Vector3 a = pos(ua, va), b = pos(ub, vb), c = pos(uc, vc);
                if ((b - a).Cross(c - a).LengthSquared() < 1e-12f) return; // pôle : triangle dégénéré
                st.SetColor(col(ua, va)); st.AddVertex(a);
                st.SetColor(col(ub, vb)); st.AddVertex(b);
                st.SetColor(col(uc, vc)); st.AddVertex(c);
            }
            // Godot : faces avant dans le sens horaire vues de l'extérieur
            T(u0, v0, u1, v0, u1, v1);
            T(u0, v0, u1, v1, u0, v1);
        }
        st.GenerateNormals();
        return st.Commit();
    }

    static readonly Dictionary<string, ArrayMesh> _meshes = new();

    static ArrayMesh Rosette() => Get("rosette", () => Lathe(96, 20, (u, v) =>
    {
        float a = u * MathF.Tau, h = 2.4f;
        float r = 2.0f * MathF.Pow(1 - v, 0.75f) * (1 + 0.16f * MathF.Cos(8 * a + v * 7)) + 0.02f;
        return new Vector3(MathF.Cos(a) * r, v * h + 0.25f * MathF.Sin(MathF.PI * v), MathF.Sin(a) * r);
    }, (u, v) => new Color(1, 0.97f, 0.92f).Lerp(new Color(0.93f, 0.88f, 0.82f), 0.5f + 0.5f * MathF.Cos(8 * u * MathF.Tau + v * 7))));

    static ArrayMesh Meringue() => Get("meringue", () => Lathe(80, 22, (u, v) =>
    {
        float a = u * MathF.Tau + v * 2.2f, h = 2.6f;
        float r = 1.75f * MathF.Pow(1 - v, 0.8f) * (1 + 0.12f * MathF.Cos(6 * a)) + 0.01f;
        float lean = v * v * 0.5f;
        return new Vector3(MathF.Cos(a) * r + lean, v * h, MathF.Sin(a) * r);
    }, (u, v) =>
    {
        var cream = new Color(1, 0.96f, 0.86f); var gold = new Color(0.86f, 0.58f, 0.28f); var dark = new Color(0.45f, 0.24f, 0.1f);
        float ridge = 0.5f + 0.5f * MathF.Cos(6 * (u * MathF.Tau + v * 2.2f));
        float t = Math.Clamp(v * 1.3f - 0.3f + ridge * 0.25f, 0, 1);
        return t < 0.6f ? cream.Lerp(gold, t / 0.6f) : gold.Lerp(dark, (t - 0.6f) / 0.4f);
    }));

    static ArrayMesh Sphere(string key, float rx, float ry, Color c, Color top, float dimple = 0)
        => Get(key, () => Lathe(32, 18, (u, v) =>
        {
            float a = u * MathF.Tau, p = v * MathF.PI;
            float s = MathF.Sin(p);
            float y = -MathF.Cos(p) * ry;
            if (dimple > 0) y -= dimple * MathF.Exp(-(MathF.PI - p) * (MathF.PI - p) * 6);
            return new Vector3(MathF.Cos(a) * s * rx, y + ry, MathF.Sin(a) * s * rx);
        }, (u, v) => c.Lerp(top, MathF.Pow(v, 6))));

    static ArrayMesh Stem() => Get("stem", () => Lathe(8, 10, (u, v) =>
    {
        float a = u * MathF.Tau, r = 0.07f;
        var c = new Vector3(MathF.Sin(v * 1.2f) * 0.9f, v * 1.9f, 0);
        return c + new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
    }, (u, v) => new Color(0.35f, 0.42f, 0.12f).Lerp(new Color(0.42f, 0.28f, 0.12f), v)));

    // fraise entière, pointe en bas, collerette de sépales verte au sommet
    static ArrayMesh Strawberry() => Get("strawberry", () => Lathe(28, 24, (u, v) =>
    {
        float a = u * MathF.Tau;
        // profil : pointe arrondie (v = 0) → épaules larges → sommet plat
        // corps jusqu'à v = 0.9, puis le dessus se referme (fraise pleine, pas un cornet vide)
        float vb = MathF.Min(v / 0.9f, 1);
        float r = 1.15f * MathF.Pow(MathF.Sin(MathF.PI * 0.5f * MathF.Min(1, vb * 1.25f)), 0.8f) * (1 - 0.25f * MathF.Max(0, vb - 0.8f) * 5) + 0.01f;
        float y = vb * 2.6f;
        if (v > 0.9f) { float c = (v - 0.9f) / 0.1f; r *= 1 - c; y += 0.12f * MathF.Sin(c * MathF.PI * 0.5f); }
        float lobes = 1 + 0.05f * MathF.Cos(a * 5);
        return new Vector3(MathF.Cos(a) * r * lobes, y, MathF.Sin(a) * r * lobes);
    }, (u, v) =>
    {
        // grains (akènes) : petits points jaunes en quinconce
        float gu = u * 14, gv = v * 9 + ((int)(u * 14) % 2) * 0.5f;
        bool seed = MathF.Abs(gu - MathF.Round(gu)) < 0.12f && MathF.Abs(gv - MathF.Round(gv)) < 0.14f && v > 0.08f && v < 0.85f;
        if (v > 0.9f) return new Color(0.92f, 0.4f, 0.32f);
        return new Color(0.82f, 0.05f, 0.09f).Lerp(new Color(0.95f, 0.35f, 0.3f), MathF.Max(0, v - 0.75f) * 3);
    }));

    static ArrayMesh Sepals() => Get("sepals", () => Lathe(40, 4, (u, v) =>
    {
        float a = u * MathF.Tau;
        float spike = MathF.Pow(MathF.Abs(MathF.Cos(a * 2.5f)), 3);
        float r = 0.25f + v * (0.5f + 0.75f * spike);
        return new Vector3(MathF.Cos(a) * r, -v * v * 0.35f * spike + 0.05f * (1 - v), MathF.Sin(a) * r);
    }, (u, v) => new Color(0.2f, 0.5f, 0.12f).Lerp(new Color(0.35f, 0.65f, 0.2f), v)));

    // macaron : deux coques bombées, une collerette (« pied ») et la ganache
    static ArrayMesh Macaron() => Get("macaron", () =>
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Ring(float y0, float y1, Func<float, float, float> rad, Color c, int nv)
        {
            int nu = 40;
            for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                float a0 = i * MathF.Tau / nu, a1 = (i + 1) * MathF.Tau / nu, v0 = j / (float)nv, v1 = (j + 1) / (float)nv;
                Vector3 P(float a, float v) => new(MathF.Cos(a) * rad(a, v), y0 + (y1 - y0) * v, MathF.Sin(a) * rad(a, v));
                void V(float a, float v) { st.SetColor(c); st.AddVertex(P(a, v)); }
                V(a0, v0); V(a1, v0); V(a1, v1);
                V(a0, v0); V(a1, v1); V(a0, v1);
            }
        }
        var shell = Colors.White; var foot = new Color(0.9f, 0.9f, 0.88f); var ganache = new Color(1f, 0.95f, 0.85f);
        Ring(0, 0.35f, (a, v) => 1.6f + 0.08f * MathF.Sin(a * 23), foot, 2);                              // pied du bas
        Ring(0.35f, 0.75f, (a, v) => 1.65f * MathF.Sqrt(1 - v * v * 0.0f) - 0.05f * v, shell, 3);          // coque du bas
        Ring(0.75f, 1.15f, (a, v) => 1.45f, ganache, 2);                                                  // ganache
        Ring(1.15f, 1.5f, (a, v) => 1.6f + 0.08f * MathF.Sin(a * 23 + 1), foot, 2);                       // pied du haut
        Ring(1.5f, 2.3f, (a, v) => 1.65f * MathF.Sqrt(MathF.Max(0, 1 - v * v)) + 0.01f, shell, 8);         // dôme
        st.GenerateNormals();
        return st.Commit();
    });

    static ArrayMesh Get(string k, Func<ArrayMesh> make)
    {
        if (!_meshes.TryGetValue(k, out var m)) _meshes[k] = m = make();
        return m;
    }

    static StandardMaterial3D Mat(float rough, bool coat = false, float sss = 0)
    {
        var m = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = rough };
        if (coat) { m.ClearcoatEnabled = true; m.Clearcoat = 1; m.ClearcoatRoughness = 0.08f; }
        if (sss > 0) { m.BacklightEnabled = true; m.Backlight = new Color(sss, sss * 0.9f, sss * 0.8f); }
        m.RimEnabled = true; m.Rim = 0.25f; m.RimTint = 0.7f;
        return m;
    }

    // ───── placement ─────
    Kind AddKind(ArrayMesh mesh, Material mat, Color burst)
    {
        var k = new Kind { Burst = burst };
        var m = (ArrayMesh)mesh.Duplicate();
        m.SurfaceSetMaterial(0, mat);
        k.Mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = m };
        _kinds.Add(k);
        return k;
    }

    bool Place(Kind k, float lx, float lz, float yaw, float scale, Color c, float radius, float lift = 0, float tilt = 0)
    {
        int i = (int)MathF.Floor(lx + Off), kk = (int)MathF.Floor(lz + Off);
        if (i < 0 || kk < 0 || i >= CW || kk >= CW) return false;
        int col = i + kk * CW;
        if (_k.H0[col] == 0) return false;
        float y = _k.Hx[col] - 0.05f + lift;
        var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Forward, tilt);
        k.Items.Add((new Transform3D(basis.Scaled(Vector3.One * scale), new Vector3(_k.Cx + lx, y, _k.Cz + lz)), c, col, radius * scale, y));
        return true;
    }

    bool PlaceBasis(Kind k, float lx, float lz, Basis b, float scale, float lift, float radius, Vector3 off = default)
    {
        int i = (int)MathF.Floor(lx + Off), kk = (int)MathF.Floor(lz + Off);
        if (i < 0 || kk < 0 || i >= CW || kk >= CW) return false;
        int col = i + kk * CW;
        if (_k.H0[col] == 0) return false;
        float y = _k.Hx[col] - 0.05f + lift;
        k.Items.Add((new Transform3D(b.Scaled(Vector3.One * scale), new Vector3(_k.Cx + lx, y, _k.Cz + lz) + off), Colors.White, col, radius * scale, y));
        return true;
    }

    void Build()
    {
        int style = (_k.N - 1) % Themes.Length;
        var rng = new Random(_k.N * 977);
        float Rnd() => (float)rng.NextDouble();
        var th = _k.Theme;
        switch (style)
        {
            case 1:
            {
                var ros = AddKind(Rosette(), Mat(0.55f, false, 0.35f), Colors.White);
                var cher = AddKind(Sphere("cherry", 0.95f, 0.85f, new Color(0.62f, 0.02f, 0.08f), new Color(0.3f, 0.0f, 0.04f), 0.25f), Mat(0.15f, true, 0.2f), new Color(0.6f, 0.05f, 0.1f));
                var stem = AddKind(Stem(), Mat(0.6f), new Color(0.4f, 0.3f, 0.1f));
                for (int n = 0; n < 16; n++)
                {
                    float a = n * MathF.Tau / 16, x = MathF.Cos(a) * 31, z = MathF.Sin(a) * 31;
                    if (!Place(ros, x, z, Rnd() * 6.3f, 0.95f + 0.1f * Rnd(), Colors.White, 2f)) continue;
                    Place(cher, x, z, Rnd() * 6.3f, 1, Colors.White, 1f, 2.15f);
                    Place(stem, x, z, Rnd() * 6.3f, 1, Colors.White, 0.3f, 3.6f, 0.2f);
                }
                break;
            }
            case 2:
            {
                var str = AddKind(Strawberry(), Mat(0.2f, true, 0.3f), new Color(0.85f, 0.1f, 0.15f));
                var sep = AddKind(Sepals(), Mat(0.6f, false, 0.4f), new Color(0.3f, 0.6f, 0.2f));
                var ros = AddKind(Rosette(), Mat(0.55f, false, 0.35f), Colors.White);
                for (int n = 0; n < 20; n++)
                {
                    float a = n * MathF.Tau / 20, x = MathF.Cos(a) * 30, z = MathF.Sin(a) * 30;
                    // fraise couchée vers l'extérieur sur une petite rosace
                    float sc = 1.1f + 0.15f * Rnd();
                    if (!Place(ros, x, z, Rnd() * 6.3f, 0.75f, Colors.White, 1.5f)) continue;
                    var dir = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                    var axis = dir.Cross(Vector3.Up).Normalized();
                    var b = new Basis(axis, -1.1f);
                    PlaceBasis(str, x, z, b, sc, 1.5f, 1.2f);
                    PlaceBasis(sep, x, z, b, sc, 1.5f, 1.2f, b * new Vector3(0, 2.55f * sc, 0));
                }
                for (int n = 0; n < 6; n++)
                {
                    float a = n * MathF.Tau / 6 + 0.3f, x = MathF.Cos(a) * 3.2f, z = MathF.Sin(a) * 3.2f;
                    var b = new Basis(Vector3.Up, Rnd() * 6.3f) * new Basis(Vector3.Right, 0.3f);
                    PlaceBasis(str, x, z, b, 1.3f, 0, 1.2f);
                    PlaceBasis(sep, x, z, b, 1.3f, 0, 1.2f, b * new Vector3(0, 2.55f * 1.3f, 0));
                }
                Place(ros, 0, 0, 0, 1.3f, Colors.White, 2f);
                break;
            }
            case 3:
            {
                var mac = AddKind(Macaron(), Mat(0.6f, false, 0.15f), new Color(0.6f, 0.8f, 0.4f));
                var green = new Color(0.55f, 0.78f, 0.35f); var cream = new Color(1f, 0.9f, 0.62f); var pink = new Color(1f, 0.55f, 0.65f);
                for (int n = 0; n < 14; n++)
                {
                    float a = n * MathF.Tau / 14, x = MathF.Cos(a) * 30.5f, z = MathF.Sin(a) * 30.5f;
                    Place(mac, x, z, Rnd() * 6.3f, 1.0f, n % 2 == 0 ? green : cream, 1.7f, 0, 0.08f);
                }
                for (int n = 0; n < 5; n++)
                {
                    float a = n * MathF.Tau / 5;
                    Place(mac, MathF.Cos(a) * 3.2f, MathF.Sin(a) * 3.2f, Rnd() * 6.3f, 1.05f, n % 2 == 0 ? pink : green, 1.7f, 0, 0.35f);
                }
                break;
            }
            case 4:
            {
                var mer = AddKind(Meringue(), Mat(0.5f, false, 0.4f), new Color(1f, 0.95f, 0.85f));
                // pochage en cercles concentriques serrés
                for (float rr = 0; rr < 35.5f; rr += 3.3f)
                {
                    int cnt = Math.Max(1, (int)(MathF.Tau * rr / 3.3f));
                    float off = Rnd() * 6.3f;
                    for (int n = 0; n < cnt; n++)
                    {
                        float a = off + n * MathF.Tau / cnt;
                        Place(mer, MathF.Cos(a) * rr, MathF.Sin(a) * rr, Rnd() * 6.3f, 0.85f + 0.3f * Rnd(), Colors.White, 1.4f);
                    }
                }
                break;
            }
            case 5:
            {
                var berry = AddKind(Sphere("blueberry", 0.75f, 0.7f, new Color(0.28f, 0.3f, 0.62f), new Color(0.12f, 0.1f, 0.22f), 0.12f), Mat(0.5f, false, 0.15f), new Color(0.3f, 0.3f, 0.7f));
                for (int n = 0; n < 22; n++)
                {
                    float a = n * MathF.Tau / 22, rr = 31.5f;
                    for (int b = 0; b < 3; b++)
                    {
                        float ba = b * 2.1f + Rnd();
                        Place(berry, MathF.Cos(a) * rr + MathF.Cos(ba) * 0.85f, MathF.Sin(a) * rr + MathF.Sin(ba) * 0.85f, Rnd() * 6.3f, 0.9f + 0.25f * Rnd(), Colors.White, 0.8f);
                    }
                }
                for (int n = 0; n < 40; n++)
                {
                    float a = Rnd() * MathF.Tau, rr = 4 + Rnd() * 24;
                    Place(berry, MathF.Cos(a) * rr, MathF.Sin(a) * rr, Rnd() * 6.3f, 0.8f + 0.3f * Rnd(), Colors.White, 0.8f);
                }
                break;
            }
        }
        foreach (var k in _kinds)
        {
            k.Mm.InstanceCount = k.Items.Count;
            k.Gone = new bool[k.Items.Count];
            for (int i = 0; i < k.Items.Count; i++) { k.Mm.SetInstanceTransform(i, k.Items[i].t); k.Mm.SetInstanceColor(i, k.Items[i].c); }
            AddChild(new MultiMeshInstance3D { Multimesh = k.Mm });
        }
    }

    // Colonnes croquées → garniture disparue ; la fourmi gobe ce qu'elle touche
    public void UpdateFrame(float dt, Vector3 ant, Effects fx)
    {
        if (_kinds.Count == 0) return;
        _check -= dt;
        bool full = _check <= 0;
        if (full) _check = 0.25f;
        float dx = ant.X - _k.Cx, dz = ant.Z - _k.Cz;
        bool near = dx * dx + dz * dz < (CakeR + 2) * (CakeR + 2) && ant.Y > Layers - 3;
        if (!full && !near) return;
        foreach (var k in _kinds)
        for (int i = 0; i < k.Items.Count; i++)
        {
            if (k.Gone[i]) continue;
            var it = k.Items[i];
            bool gone = full && _k.ColTop[it.col] < _k.H0[it.col] - 1;
            if (!gone && near)
            {
                var p = it.t.Origin;
                float ddx = ant.X - p.X, ddz = ant.Z - p.Z;
                if (ddx * ddx + ddz * ddz < (it.r + 0.25f) * (it.r + 0.25f) && ant.Y > it.top - 1.0f && ant.Y < it.top + 4)
                {
                    gone = true;
                    fx?.Burst(p + Vector3.Up * 0.8f, k.Burst, 18, 1.2f);
                    Sfx.Play("crunch", -4, 1.2f);
                }
            }
            if (!gone) continue;
            k.Gone[i] = true;
            k.Mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.0001f), it.t.Origin));
        }
    }
}
