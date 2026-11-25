// ─────────────────────────────────────────────────────────────
// Radiographie (G) : galeries → fourmis → les deux → coupé.
// Les cavités creusées dans le volume d'origine des gâteaux sont
// visibles à travers les murs, colorées selon la hauteur (bleu clair
// en bas → bleu nuit en haut) ; les ouvrières apparaissent en silhouettes rouges (WorkersView.SetXray).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public partial class XrayView : Node3D
{
    public int Mode; // 0 coupé, 1 galeries, 2 fourmis, 3 les deux
    GameState _game;
    readonly List<MultiMesh> _cells = new();
    float _t;
    int _next;
    public static readonly string[] Names = { "Radiographie coupée", "Radiographie : galeries", "Radiographie : fourmis", "Radiographie : galeries et fourmis" };

    public void Setup(GameState game)
    {
        _game = game;
        var cubeMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 3,
        };
        var cube = new BoxMesh { Size = Vector3.One * 0.9f, Material = cubeMat };
        foreach (var K in game.Cakes)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = cube, InstanceCount = 0 };
            AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = new Aabb(new Vector3(-400, -10, -450), new Vector3(800, 60, 900)) });
            _cells.Add(mm);
        }
    }

    public bool Ants => Mode == 2 || Mode == 3;

    public string Cycle()
    {
        Mode = (Mode + 1) % 4;
        _t = 0;
        if (Mode == 0 || Mode == 2) foreach (var m in _cells) m.VisibleInstanceCount = 0;
        return L.T(Names[Mode]);
    }

    public void UpdateFrame(float dt)
    {
        bool cells = Mode == 1 || Mode == 3;
        if (cells)
        {
            _t -= dt;
            if (_t <= 0)
            {
                _t = 0.25f / _game.Cakes.Length;
                Rebuild(_next++ % _game.Cakes.Length);
            }
        }
    }

    // nuances de bleu selon la hauteur : bleu ciel clair à la base → bleu roi → bleu nuit au sommet
    static readonly Color[] Ramp =
    {
        new(0.62f, 0.86f, 1.00f), new(0.36f, 0.66f, 1.00f), new(0.16f, 0.42f, 0.92f),
        new(0.06f, 0.20f, 0.66f), new(0.02f, 0.06f, 0.34f),
    };
    static Color Shade(float t)
    {
        t = Mathf.Clamp(t, 0, 1) * (Ramp.Length - 1);
        int i = Math.Min((int)t, Ramp.Length - 2);
        return Ramp[i].Lerp(Ramp[i + 1], t - i);
    }

    void Rebuild(int k)
    {
        var K = _game.Cakes[k];
        var mm = _cells[k];
        var list = new List<(Vector3, Color)>();
        // hauteur réelle du gâteau : le dégradé couvre tout l'éventail, de la base au sommet
        int hmax = 1;
        for (int c = 0; c < Area; c++) hmax = Math.Max(hmax, K.H0[c]);
        for (int c = 0; c < Area; c++)
        {
            int h = K.H0[c];
            if (h == 0) continue;
            int top = K.ColTop[c];
            if (top < 0) continue; // colonne entièrement mangée : rien à montrer
            // seulement les blocs mangés SOUS le dernier bloc plein de la colonne (galeries) :
            // ni les blocs intacts, ni l'air d'une cuvette ouverte, ni les colonnes vidées
            for (int y = 0; y < top; y++)
            {
                if (K.Cake[c + y * Area] != 0) continue;
                float t = hmax > 1 ? y / (float)(hmax - 1) : 0;
                var col = Shade(t);
                col.A = 0.36f + 0.16f * t;
                list.Add((new Vector3(c % CW - K.IX + 0.5f, y + 0.5f, c / CW - K.IZ + 0.5f), col));
            }
        }
        if (mm.InstanceCount < list.Count) mm.InstanceCount = list.Count + 512;
        for (int i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, list[i].Item1)); mm.SetInstanceColor(i, list[i].Item2); }
        mm.VisibleInstanceCount = list.Count;
    }
}
