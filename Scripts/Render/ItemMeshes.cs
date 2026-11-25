// ─────────────────────────────────────────────────────────────
// Modèles des objets de la chaîne de production, construits au
// démarrage. Chacun prend la couleur des miettes de SON gâteau :
//   miette → boulette de pâte (compacteur) → petit gâteau doré dans
//   sa caissette (four) → gâteau royal à étages, glacé, cerise (décorateur).
// Couleur de sommet : rgb = nuance, a = 1 si la partie se teinte.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;

namespace Crumbants.Render;

public static class ItemMeshes
{
    static ShaderMaterial _mat;
    public static ShaderMaterial Material => _mat ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/item.gdshader") };

    static float Hash(int a, int b) { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; }

    // anneaux (rayon, hauteur, couleur) révolus autour de l'axe Y ; rmod : rayon par secteur (plis, bosses)
    static void Lathe(SurfaceTool st, IList<(float r, float y, Color c)> rings, int seg, Vector3 at, Func<int, int, float> rmod = null)
    {
        for (int i = 0; i < rings.Count - 1; i++)
        for (int s = 0; s < seg; s++)
        {
            Vector3 P(int ri, int si)
            {
                float a = si * MathF.Tau / seg, r = rings[ri].r * (rmod?.Invoke(ri, si % seg) ?? 1);
                return at + new Vector3(MathF.Cos(a) * r, rings[ri].y, MathF.Sin(a) * r);
            }
            var a0 = P(i, s); var a1 = P(i, s + 1); var b0 = P(i + 1, s); var b1 = P(i + 1, s + 1);
            Color ca = rings[i].c, cb = rings[i + 1].c;
            // faces avant dans le sens horaire (convention Godot)
            st.SetColor(ca); st.AddVertex(a0); st.SetColor(ca); st.AddVertex(a1); st.SetColor(cb); st.AddVertex(b1);
            st.SetColor(ca); st.AddVertex(a0); st.SetColor(cb); st.AddVertex(b1); st.SetColor(cb); st.AddVertex(b0);
        }
    }

    static List<(float, float, Color)> Sphere(float r, float yc, Color c, int rings, float squashBottom = 1)
    {
        var l = new List<(float, float, Color)>();
        for (int i = 0; i <= rings; i++)
        {
            float t = MathF.PI * i / rings - MathF.PI / 2; // −90° → +90°
            float y = MathF.Sin(t) * r;
            if (y < 0) y *= squashBottom;
            l.Add((MathF.Max(0.0005f, MathF.Cos(t) * r), yc + y, c));
        }
        return l;
    }

    static Mesh Finish(SurfaceTool st)
    {
        st.GenerateNormals();
        var m = st.Commit();
        m.SurfaceSetMaterial(0, Material);
        return m;
    }

    static readonly Color Tint = new(1, 1, 1, 1);
    static Color T(float k) => new(k, k, k, 1);                 // teinte du gâteau, nuancée
    static Color Fixed(string hex) { var c = new Color(hex); c.A = 0; return c; }

    public static Mesh Build(string type)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        switch (type)
        {
            case "ball":
            {
                // boulette de pâte : ronde, un peu tassée dessous, pâte claire de la couleur du gâteau
                var dough = new Color(1f, 0.96f, 0.9f, 0.8f);
                Lathe(st, Sphere(0.14f, 0.0f, dough, 10, 0.75f), 18, Vector3.Zero, (ri, si) => 1 + 0.025f * MathF.Sin(si * 2.1f + ri * 1.3f));
                break;
            }
            case "small":
            {
                // petit gâteau sorti du four : caissette plissée (liseré à la couleur du gâteau), dôme doré bien cuit
                const int seg = 24;
                var paper = Fixed("#fbf6ec");
                var rings = new List<(float, float, Color)> { (0.0005f, -0.08f, paper), (0.105f, -0.08f, paper), (0.14f, 0.03f, paper), (0.142f, 0.035f, T(1f)) };
                Lathe(st, rings, seg, Vector3.Zero, (ri, si) => ri >= 1 ? (si % 2 == 0 ? 1.05f : 0.97f) : 1);
                var baked = new Color(0.82f, 0.62f, 0.42f, 1); var top = new Color(0.66f, 0.46f, 0.3f, 1);
                var dome = new List<(float, float, Color)> { (0.142f, 0.035f, baked), (0.16f, 0.07f, baked), (0.15f, 0.11f, baked), (0.11f, 0.145f, top), (0.05f, 0.16f, top), (0.0005f, 0.163f, top) };
                Lathe(st, dome, seg, Vector3.Zero, (ri, si) => 1 + 0.03f * (Hash(ri, si) - 0.5f));
                break;
            }
            case "royal":
            {
                // gâteau royal décoré : deux étages à la couleur du gâteau, glaçage blanc qui coule, perles, cerise
                const int seg = 28;
                var icing = Fixed("#fffaf2");
                var low = new List<(float, float, Color)> { (0.0005f, -0.08f, T(0.85f)), (0.18f, -0.08f, T(0.85f)), (0.18f, 0.02f, T(1f)) };
                Lathe(st, low, seg, Vector3.Zero);
                // nappe de glaçage : coulures irrégulières sur le bord
                var drip = new List<(float, float, Color)> { (0.183f, 0.006f, icing), (0.183f, 0.028f, icing), (0.17f, 0.04f, icing), (0.0005f, 0.04f, icing) };
                Lathe(st, drip, seg, Vector3.Zero, (ri, si) => 1);
                for (int k = 0; k < seg; k++)
                {
                    if (Hash(k, 7) < 0.5f) continue;
                    float a = k * MathF.Tau / seg, len = 0.02f + 0.035f * Hash(k, 3);
                    // une coulure : goutte étirée verticalement, collée au flanc
                    var g = new List<(float, float, Color)>();
                    for (int i = 0; i <= 5; i++) { float t = i / 5f; g.Add((0.0005f + 0.016f * MathF.Sin(MathF.PI * MathF.Min(1, t * 1.6f)), 0.03f - len * (1 - t) - 0.012f, icing)); }
                    Lathe(st, g, 6, new Vector3(MathF.Cos(a) * 0.178f, 0, MathF.Sin(a) * 0.178f));
                }
                var up = new List<(float, float, Color)> { (0.0005f, 0.04f, T(0.85f)), (0.115f, 0.04f, T(0.9f)), (0.115f, 0.12f, T(1f)) };
                Lathe(st, up, seg, Vector3.Zero);
                var cap = new List<(float, float, Color)> { (0.118f, 0.105f, icing), (0.118f, 0.125f, icing), (0.105f, 0.138f, icing), (0.0005f, 0.14f, icing) };
                Lathe(st, cap, seg, Vector3.Zero);
                // perles de crème autour de l'étage du dessus
                for (int k = 0; k < 10; k++)
                {
                    float a = k * MathF.Tau / 10;
                    Lathe(st, Sphere(0.022f, 0, icing, 5), 8, new Vector3(MathF.Cos(a) * 0.15f, 0.05f, MathF.Sin(a) * 0.15f));
                }
                // la cerise et sa queue
                Lathe(st, Sphere(0.04f, 0, Fixed("#d3122c"), 8), 12, new Vector3(0, 0.178f, 0));
                var stem = Fixed("#4f7a22");
                Lathe(st, new List<(float, float, Color)> { (0.006f, 0.21f, stem), (0.005f, 0.26f, stem) }, 5, new Vector3(0.008f, 0, 0));
                break;
            }
            default:
            {
                // miette : un éclat irrégulier, nuances de cuisson
                const int seg = 7;
                var rings = Sphere(0.12f, 0, Tint, 4, 0.7f);
                for (int i = 0; i < rings.Count; i++) { var (r, y, _) = rings[i]; rings[i] = (r, y, T(0.82f + 0.18f * i / (rings.Count - 1))); }
                Lathe(st, rings, seg, Vector3.Zero, (ri, si) => 0.78f + 0.4f * Hash(ri * 13 + 5, si));
                break;
            }
        }
        return Finish(st);
    }

    // couleur d'instance (INSTANCE_CUSTOM) : la couleur des miettes du gâteau, en linéaire
    public static Color CustomOf(int cake) => Cfg.CrumbColor(cake).SrgbToLinear();
}
