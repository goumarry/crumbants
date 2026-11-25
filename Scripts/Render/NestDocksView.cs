// ─────────────────────────────────────────────────────────────
// Guichets de livraison de la fourmilière : un tunnel de terre
// (Models/docks.glb) devant chacun. Le dernier tapis se prolonge
// par une rampe qui plonge sous terre et se fond dans l'obscurité
// du nid ; les objets livrés y glissent et disparaissent.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public partial class NestDocksView : Node3D
{
    sealed class Dock
    {
        public Vector3 Pos, In;   // centre de la case, direction vers le nid
        public Node3D Node;
        public readonly List<(float t, string type, float spin, int cake)> Items = new();
    }
    readonly List<Dock> _docks = new();
    readonly Dictionary<string, MultiMesh> _itemMM = new();
    const float SlideT = 2.2f, Tilt = 0.32f; // durée de la glissade, pente de la rampe (rad)

    public void Setup()
    {
        var beltMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/belt.gdshader") };
        Mesh BandOf(string k)
        {
            var m = ModelLib.Get("belts", "belt_band_" + k);
            if (m == null) return null;
            m = (Mesh)m.Duplicate();
            for (int i = 0; i < m.GetSurfaceCount(); i++) m.SurfaceSetMaterial(i, beltMat);
            return m;
        }
        Mesh band = BandOf("s");
        var frame = ModelLib.Get("belts", "belt_frame_s");
        var veil = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.03f, 0.015f, 0.005f, 0.42f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (var (cell, o) in NestDocks)
        {
            var pos = new Vector3(cell.X + 0.5f, Terrain.GroundAt(cell.X + 0.5f, cell.Y + 0.5f), cell.Y + 0.5f);
            var inDir = new Vector3(-o.X, 0, -o.Y);
            var n = new Node3D { Position = pos, Rotation = new Vector3(0, MathF.Atan2(-inDir.X, -inDir.Z), 0) };
            AddChild(n);
            // le modèle regarde vers -Z local (le nid)
            foreach (var part in new[] { "dock_mound", "dock_stones", "dock_frame", "dock_lantern", "dock_floor" })
            {
                var m = ModelLib.Get("docks", part);
                if (m != null) n.AddChild(new MeshInstance3D { Mesh = m });
            }
            n.AddChild(new OmniLight3D { LightColor = new Color("#ffc27a"), LightEnergy = 0.5f, OmniRange = 2.5f, Position = new Vector3(0.55f, 0.9f, 0.18f) });
            // le tapis du guichet (à plat) puis la rampe qui plonge dans le tunnel
            void Belt(Transform3D t, Node3D parent = null, Mesh fr = null, Mesh bd = null)
            {
                parent ??= n;
                fr ??= frame; bd ??= band;
                if (fr != null) parent.AddChild(new MeshInstance3D { Mesh = fr, Transform = t });
                if (bd != null) parent.AddChild(new MeshInstance3D { Mesh = bd, Transform = t });
            }
            var tb = Basis.FromScale(new Vector3(0.92f, 1, 1)); // sortie vers -Z : vers le nid
            var dock = new Dock { Pos = pos, In = inDir, Node = n };
            Belt(new Transform3D(tb, Vector3.Zero));
            var rampB = new Basis(Vector3.Right, -Tilt) * tb;
            for (int k = 0; k < 3; k++)
            {
                float d = 0.5f + (k + 0.5f) * MathF.Cos(Tilt);
                Belt(new Transform3D(rampB, new Vector3(0, -(k + 0.5f) * MathF.Sin(Tilt) - 0.02f, -d)));
            }
            // voiles d'obscurité successifs : la rampe se fond dans le noir
            for (int k = 0; k < 4; k++)
                n.AddChild(new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(1.25f, 0.8f) }, MaterialOverride = veil,
                    Position = new Vector3(0, 0.38f, -0.95f - k * 0.4f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            _docks.Add(dock);
        }
        foreach (var t in new[] { "crumb", "ball", "small", "royal" })
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = BuildingsView.ItemMesh(t), InstanceCount = 64, VisibleInstanceCount = 0 };
            AddChild(new MultiMeshInstance3D { Multimesh = mm, CustomAabb = new Aabb(new Vector3(-20, -5, 30), new Vector3(40, 15, 40)) });
            _itemMM[t] = mm;
        }
    }

    // un objet vient d'être livré au guichet le plus proche de p : il continue sa route sous terre
    public void Deliver(Vector3 p, string type, int cake = 0)
    {
        Dock best = null; float bd = 4;
        foreach (var d in _docks) { float l = new Vector2(d.Pos.X - p.X, d.Pos.Z - p.Z).Length(); if (l < bd) { bd = l; best = d; } }
        if (best == null || best.Items.Count >= 12) return;
        best.Items.Add((0, _itemMM.ContainsKey(type ?? "") ? type : "crumb", GD.Randf() * 6.28f, cake));
    }

    public void UpdateFrame(float dt)
    {
        var count = new Dictionary<string, int>();
        foreach (var k in _itemMM.Keys) count[k] = 0;
        foreach (var d in _docks)
        {
            for (int i = d.Items.Count - 1; i >= 0; i--)
            {
                var it = d.Items[i];
                it.t += dt;
                if (it.t > SlideT) { d.Items.RemoveAt(i); continue; }
                d.Items[i] = it;
                // abscisse le long du tapis : du centre de la case jusqu'au fond de la rampe
                float s = it.t / SlideT * 3.6f;
                float y = 0.42f, z = -s;
                if (s > 0.5f) { float r = s - 0.5f; z = -0.5f - r * MathF.Cos(Tilt); y -= r * MathF.Sin(Tilt); }
                var mm = _itemMM[it.type];
                int n = count[it.type]++;
                if (n >= mm.InstanceCount) continue;
                var local = new Transform3D(new Basis(Vector3.Up, it.spin), new Vector3(0, y, z));
                mm.SetInstanceTransform(n, d.Node.Transform * local);
                mm.SetInstanceCustomData(n, ItemMeshes.CustomOf(it.cake));
            }
        }
        foreach (var (k, mm) in _itemMM) mm.VisibleInstanceCount = Math.Min(count[k], mm.InstanceCount);
    }
}
