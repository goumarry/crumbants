// ─────────────────────────────────────────────────────────────
// Affichage des bâtiments (modèles Blender : Models/buildings.glb),
// des tapis (instanciés, bande animée, virages) et des objets qu'ils
// transportent, plus leur personnel (pilote, cuisinières…).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public static class ModelLib
{
    static readonly Dictionary<string, Dictionary<string, Mesh>> _libs = new();
    public static Mesh Get(string lib, string name)
    {
        if (!_libs.TryGetValue(lib, out var d))
        {
            d = new();
            var scene = GD.Load<PackedScene>($"res://Models/{lib}.glb").Instantiate();
            void Walk(Node n)
            {
                if (n is MeshInstance3D mi && mi.Mesh != null) d[mi.Name] = lib == "ant" ? mi.Mesh : MatLib.UpgradeMesh(mi.Mesh);
                foreach (var c in n.GetChildren()) Walk(c);
            }
            Walk(scene);
            scene.QueueFree();
            _libs[lib] = d;
        }
        return d.TryGetValue(name, out var m) ? m : null;
    }

    // Repère posé dans Blender (Blender/buildings2.py, « op_<type> ») : petit triangle isocèle dont
    // la pointe donne la direction du regard. Renvoie la position (milieu de la base) et la direction.
    static readonly Dictionary<string, (Vector3 pos, Vector3 fwd)?> _markers = new();
    public static (Vector3 pos, Vector3 fwd)? Marker(string lib, string name)
    {
        string key = lib + "/" + name;
        if (_markers.TryGetValue(key, out var r)) return r;
        r = null;
        var mesh = Get(lib, name);
        if (mesh != null && mesh.GetSurfaceCount() > 0)
        {
            var v = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (v.Length >= 3)
            {
                var c = (v[0] + v[1] + v[2]) / 3;
                int tip = 0;
                for (int i = 1; i < 3; i++) if ((v[i] - c).LengthSquared() > (v[tip] - c).LengthSquared()) tip = i;
                var bas = (v[(tip + 1) % 3] + v[(tip + 2) % 3]) / 2;
                r = (bas, (v[tip] - bas).Normalized());
            }
        }
        if (r == null) GD.PushWarning($"repère {key} introuvable");
        _markers[key] = r;
        return r;
    }
}

public partial class BuildingsView : Node3D
{
    GameState _game;
    readonly Dictionary<int, Node3D> _nodes = new();
    MultiMesh _beltStraight, _beltCurveL, _beltCurveR, _bandS, _bandL, _bandR;
    MultiMesh _beltOpenL, _beltOpenR, _beltOpenLR; // tapis droits ouverts sur un côté (jonction en T)
    readonly Dictionary<string, MultiMesh> _items = new();
    ShaderMaterial _beltMat, _beltMatC, _beltMatGap;
    MultiMesh _bandGap;
    float _time;
    public static Vector3? LastCraneRing; // test visuel (--camcrane)
    int _cableGen = -1, _cableCount = -1;
    MeshInstance3D _cables;

    public void Setup(GameState game)
    {
        _game = game;
        _beltMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/belt.gdshader") };
        // virages : la bande y fait un quart de cercle (longueur π/4 en UV) — une flèche par case quand même
        _beltMatC = (ShaderMaterial)_beltMat.Duplicate();
        _beltMatC.SetShaderParameter("len", MathF.PI / 4);
        _beltMatGap = (ShaderMaterial)_beltMat.Duplicate();
        _beltMatGap.SetShaderParameter("plain", true);
        // tapis modélisés dans Blender (Models/belts.glb) : cadre + bande défilante
        Mesh Band(string k)
        {
            var m = (Mesh)ModelLib.Get("belts", "belt_band_" + k).Duplicate();
            for (int i = 0; i < m.GetSurfaceCount(); i++) m.SurfaceSetMaterial(i, k == "s" ? _beltMat : _beltMatC);
            return m;
        }
        _beltStraight = MakeMM(ModelLib.Get("belts", "belt_frame_s"), 2000);
        _beltCurveL = MakeMM(ModelLib.Get("belts", "belt_frame_l"), 600);
        _beltCurveR = MakeMM(ModelLib.Get("belts", "belt_frame_r"), 600);
        _beltOpenL = MakeMM(ModelLib.Get("belts", "belt_frame_sl"), 400);
        _beltOpenR = MakeMM(ModelLib.Get("belts", "belt_frame_sr"), 400);
        _beltOpenLR = MakeMM(ModelLib.Get("belts", "belt_frame_slr"), 200);
        _bandS = MakeMM(Band("s"), 2000);
        // raccord de jonction en T : un bout de bande qui comble le vide entre le tapis qui arrive par
        // le côté et la bande de la chaîne (elle ne couvre pas toute la largeur de sa case)
        var gap = (Mesh)ModelLib.Get("belts", "belt_band_s").Duplicate();
        for (int i = 0; i < gap.GetSurfaceCount(); i++) gap.SurfaceSetMaterial(i, _beltMatGap);
        _bandGap = MakeMM(gap, 600);
        _bandL = MakeMM(Band("l"), 600);
        _bandR = MakeMM(Band("r"), 600);
        foreach (var t in new[] { "crumb", "ball", "small", "royal" }) _items[t] = MakeMM(ItemMesh(t), 2000, true);
    }

    MultiMesh MakeMM(Mesh mesh, int n, bool custom = false)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = custom, Mesh = mesh, InstanceCount = n, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, CustomAabb = new Aabb(new Vector3(-400, -10, -450), new Vector3(800, 60, 900)) });
        return mm;
    }

    // ───── Tapis : cadre, rails jaunes, bande (shader), pieds ─────
    // curve : 0 = droit, -1 = arrive par la gauche, +1 = par la droite. Sortie vers -Z.
    Mesh BeltMesh(int curve)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // bande : 12 segments le long du trajet ; UV.y = abscisse (pour le défilement)
        const int N = 12; const float H = 0.16f, W = 0.36f;
        Vector3 Path(float t, out Vector3 tangent)
        {
            if (curve == 0) { tangent = new Vector3(0, 0, -1); return new Vector3(0, 0, 0.5f - t); }
            // quart de cercle : du bord latéral (x = curve * 0.5) au bord avant (z = -0.5), centre au coin
            var c = new Vector3(curve * 0.5f, 0, -0.5f);
            var p = c + new Vector3(-curve * 0.5f * MathF.Sin(MathF.PI / 2 * t), 0, 0.5f * MathF.Cos(MathF.PI / 2 * t));
            tangent = new Vector3(-curve * MathF.Cos(MathF.PI / 2 * t), 0, -MathF.Sin(MathF.PI / 2 * t)).Normalized();
            return p;
        }
        float len = curve == 0 ? 1 : MathF.PI / 4;
        var top = new List<(Vector3 l, Vector3 r, float v)>();
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N;
            var p = Path(t, out var tg);
            var side = new Vector3(-tg.Z, 0, tg.X);
            top.Add((p - side * W + Vector3.Up * H, p + side * W + Vector3.Up * H, t * len));
        }
        // bande (matériau 0 : shader)
        for (int i = 0; i < N; i++)
        {
            var a = top[i]; var b = top[i + 1];
            st.SetNormal(Vector3.Up);
            Tri(st, a.l, new Vector2(0, a.v), b.r, new Vector2(1, b.v), a.r, new Vector2(1, a.v));
            Tri(st, a.l, new Vector2(0, a.v), b.l, new Vector2(0, b.v), b.r, new Vector2(1, b.v));
        }
        var band = st.Commit();
        band.SurfaceSetMaterial(0, _beltMat);
        // cadre et rails (matériau 1)
        var st2 = new SurfaceTool();
        st2.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < N; i++)
        {
            var a = top[i]; var b = top[i + 1];
            foreach (int s in new[] { -1, 1 })
            {
                var pa = s < 0 ? a.l : a.r; var pb = s < 0 ? b.l : b.r;
                var dirA = (s < 0 ? a.l - a.r : a.r - a.l).Normalized();
                var dirB = (s < 0 ? b.l - b.r : b.r - b.l).Normalized();
                // flanc riveté (de la bande jusqu'au sol)
                Quad(st2, pa + dirA * 0.02f, pb + dirB * 0.02f, pb + dirB * 0.02f - Vector3.Up * (H - 0.02f), pa + dirA * 0.02f - Vector3.Up * (H - 0.02f), dirA, new Color("#6b6f78"));
                // rail jaune au-dessus
                var ra = pa + dirA * 0.05f + Vector3.Up * 0.05f; var rb = pb + dirB * 0.05f + Vector3.Up * 0.05f;
                Quad(st2, pa + dirA * 0.0f + Vector3.Up * 0.05f, pb + Vector3.Up * 0.05f, rb, ra, Vector3.Up, new Color("#f2b81c"));
                Quad(st2, ra, rb, rb - Vector3.Up * 0.07f, ra - Vector3.Up * 0.07f, dirA, new Color("#e0a514"));
            }
        }
        st2.GenerateNormals();
        var frameMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.45f, Metallic = 0.3f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        st2.SetMaterial(frameMat);
        st2.Commit(band);
        return band;
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc)
    {
        st.SetUV(ua); st.AddVertex(a); st.SetUV(ub); st.AddVertex(b); st.SetUV(uc); st.AddVertex(c);
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
    {
        st.SetColor(col);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
    }

    public static Mesh ItemMesh(string t) => ItemMeshes.Build(t);

    // ───── Forme d'un tapis selon ses voisins ─────
    // un tapis alimenté par un seul côté (rien derrière) devient un virage
    // curve : 0 droit, -1/+1 virage ; open : côtés alimentés d'un tapis droit (1 = gauche, 2 = droite)
    (int curve, int open) BeltShape(Building b)
    {
        var (dx, dz) = Dirs.D[b.Dir];
        // un voisin « nourrit » ce tapis s'il y envoie réellement des objets : un tapis tourné vers lui,
        // une grue (elle vide sur les tapis collés), ou une usine/un entrepôt dont la SORTIE donne sur lui.
        // Les roues à fourmi (générateurs) ne s'y branchent jamais.
        bool Feeds(int x, int z)
        {
            var n = _game.At(x, z);
            if (n == null) return false;
            if (n.Type == "belt" || HasOutput(n.Type))
            {
                var (ox, oz) = GameState.OutCell(n); // machines 3 × 3 : la case devant leur tapis de sortie
                return ox == b.X && oz == b.Z;
            }
            return false;
        }
        bool back = Feeds(b.X - dx, b.Z - dz);
        // côtés : gauche = rotation de la direction
        int lx = dz, lz = -dx; // à gauche quand on regarde vers (dx,dz) : (dz, -dx)
        bool left = Feeds(b.X + lx, b.Z + lz), right = Feeds(b.X - lx, b.Z - lz);
        if (!back && left != right) return (left ? -1 : 1, 0);
        return (0, (left ? 1 : 0) | (right ? 2 : 0));
    }

    static float YawOf(int dir) => -dir * MathF.PI / 2;

    // ───── Flèches d'entrée (rouges) et de sortie (verte) ─────
    static Mesh _arrowMesh;
    static ShaderMaterial _arrowOut, _arrowIn;
    public static Mesh ArrowMesh()
    {
        if (_arrowMesh != null) return _arrowMesh;
        // contour d'une flèche à plat, pointe vers −Z, extrudée (épaisseur 0,05)
        var poly = new Vector2[]
        {
            new(-0.09f, 0.2f), new(0.09f, 0.2f), new(0.09f, 0.0f), new(0.24f, 0.0f),
            new(0f, -0.28f), new(-0.24f, 0.0f), new(-0.09f, 0.0f),
        };
        var tris = Geometry2D.TriangulatePolygon(poly);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float H = 0.05f;
        for (int f = 0; f < 2; f++)
        {
            float y = f == 0 ? H : 0;
            st.SetNormal(f == 0 ? Vector3.Up : Vector3.Down);
            for (int i = 0; i < tris.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    var p = poly[tris[i + (f == 0 ? k : 2 - k)]];
                    st.AddVertex(new Vector3(p.X, y, p.Y));
                }
        }
        for (int i = 0; i < poly.Length; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Length];
            var nrm = new Vector3(b.Y - a.Y, 0, -(b.X - a.X)).Normalized();
            st.SetNormal(nrm);
            var a0 = new Vector3(a.X, 0, a.Y); var b0 = new Vector3(b.X, 0, b.Y);
            var a1 = a0 + Vector3.Up * H; var b1 = b0 + Vector3.Up * H;
            st.AddVertex(a0); st.AddVertex(b1); st.AddVertex(b0);
            st.AddVertex(a0); st.AddVertex(a1); st.AddVertex(b1);
        }
        return _arrowMesh = st.Commit();
    }

    public static Material ArrowMat(bool output)
    {
        if (_arrowOut == null)
        {
            var sh = GD.Load<Shader>("res://Shaders/arrow.gdshader");
            _arrowOut = new ShaderMaterial { Shader = sh };
            _arrowOut.SetShaderParameter("color", new Color("#0fbf2c"));
            _arrowIn = new ShaderMaterial { Shader = sh };
            _arrowIn.SetShaderParameter("color", new Color("#e8140c"));
        }
        return output ? _arrowOut : _arrowIn;
    }

    // côtés par où une machine reçoit des objets (tous sauf sa sortie) ; la grue n'en reçoit pas
    public static bool HasInputs(string type) => type is "comp" or "oven" or "deco"; // (l'entrepôt : rempli par les ouvrières)
    public static bool HasOutput(string type) => type is "harv" or "store" or "comp" or "oven" or "deco" or "belt";

    // repère local : sortie vers −Z. Flèche verte qui sort, flèches rouges qui entrent par les 3 autres côtés.
    // Toutes les flèches dans UN conteneur « Arrows » : on l'affiche ou le masque d'un bloc
    // (repérer chaque flèche par son nom ne marche pas : Godot renomme les doublons).
    public const string ArrowsNode = "Arrows";
    public static Node3D AddPortArrows(Node3D n, string type)
    {
        float R = 0.8f + 1.05f * GameState.HalfOf(type); const float Y = 0.1f;
        var box = new Node3D { Name = ArrowsNode };
        n.AddChild(box);
        var outPos = HasPortBelts(type) ? PortBelts(type)[0].pos + new Vector3(0, Y, -0.85f) : new Vector3(0, Y, -R);
        if (HasOutput(type))
            box.AddChild(new MeshInstance3D { Mesh = ArrowMesh(), MaterialOverride = ArrowMat(true), Position = outPos,
                Scale = Vector3.One * 1.25f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (!HasInputs(type)) return box;
        // entrées : la flèche pointe vers la machine (rotation : −Z local → vers le centre)
        foreach (var (pos, yaw) in new[] { (new Vector3(R, Y, 0), MathF.PI / 2), (new Vector3(-R, Y, 0), -MathF.PI / 2), (new Vector3(0, Y, R), 0f) })
            box.AddChild(new MeshInstance3D { Mesh = ArrowMesh(), MaterialOverride = ArrowMat(false), Position = pos, Rotation = new Vector3(0, yaw, 0),
                Scale = Vector3.One * 1.1f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return box;
    }

    // ───── Tapis intégrés des machines 3 × 3 (mêmes modèles que les vrais tapis) ─────
    // repère local de la machine : sortie vers −Z. Trois tapis d'entrée (gauche, droite, arrière) qui
    // filent vers le centre, un tapis de sortie à l'avant. Ils s'arrêtent si la machine n'a pas de courant.
    static readonly (Vector3 pos, float yaw)[] PortBelts3 =
    {
        (new Vector3(0, 0, -1), 0f),                    // sortie (avant)
        (new Vector3(-1, 0, 0), -MathF.PI / 2),         // entrée gauche → vers +X
        (new Vector3(1, 0, 0), MathF.PI / 2),           // entrée droite → vers −X
        (new Vector3(0, 0, 1), 0f),                     // entrée arrière → vers −Z
    };
    // grue 2 × 2 : un seul tapis, la sortie, dans la case avant gauche
    static readonly (Vector3 pos, float yaw)[] PortBeltsCrane = { (new Vector3(-0.5f, 0, -0.5f), 0f) };
    public static (Vector3 pos, float yaw)[] PortBelts(string type) =>
        type == "harv" ? PortBeltsCrane : HasInputs(type) ? PortBelts3 : PortBelts3[..1];
    public static bool HasPortBelts(string type) => GameState.Big(type) || type == "harv";
    public static void AddPortBelts(Node3D n, Material band, string type)
    {
        var frame = ModelLib.Get("belts", "belt_frame_s");
        var bandMesh = ModelLib.Get("belts", "belt_band_s");
        var box = new Node3D { Name = "PortBelts" };
        n.AddChild(box);
        var ports = PortBelts(type);
        for (int i = 0; i < ports.Length; i++)
        {
            var (pos, yaw) = ports[i];
            var t = new Transform3D(new Basis(Vector3.Up, yaw), pos);
            if (frame != null) box.AddChild(new MeshInstance3D { Mesh = frame, Transform = t });
            if (bandMesh != null) box.AddChild(new MeshInstance3D { Mesh = bandMesh, Transform = t, Name = $"band{i}", MaterialOverride = band });
        }
    }
    // tapis intégrés : en marche seulement s'ils sont alimentés ET branchés (un tapis ou une machine en face)
    ShaderMaterial _portOn, _portOff;
    readonly Dictionary<int, bool[]> _portState = new();

    // objets qui glissent sur les tapis d'entrée intégrés, du bord jusque sous le capot
    readonly List<(int id, Vector2 side, float s, string type, int cake)> _portItems = new();
    public void PortIn(int id, Vector3 offset, string type, int cake)
    {
        if (_portItems.Count > 400) return;
        _portItems.Add((id, new Vector2(offset.X, offset.Z), 0, type, cake));
    }

    // ───── Grue : flèche en tronçons (Blender/buildings2.py : crane_jib_bay, crane_jib_tip) ─────
    public const float JibRoot = 0.3f, JibBay = 0.9f, JibZ = 13.75f, WheelAxle = 1.0f;
    static readonly Vector3 Apex = new(0, JibZ + 1.5f, -0.1f); // sommet du mât en A (repère de la tête)
    readonly Dictionary<int, int> _jibBays = new();
    static StandardMaterial3D _tieMat;
    void EnsureJib(Building b, Node3D top, float range)
    {
        // assez de tronçons pour que le chariot atteigne range + 1 (plus une marge)
        int nb = Math.Max(3, (int)MathF.Ceiling((range + 1.2f - JibRoot) / JibBay));
        if (_jibBays.TryGetValue(b.Id, out int have) && have == nb) return;
        _jibBays[b.Id] = nb;
        var jib = top.GetNode<Node3D>("jib");
        foreach (var c in jib.GetChildren()) { jib.RemoveChild(c); c.QueueFree(); }
        var bay = ModelLib.Get("buildings", "crane_jib_bay");
        for (int i = 0; i < nb; i++) jib.AddChild(new MeshInstance3D { Mesh = bay, Position = new Vector3(0, 0, -i * JibBay) });
        jib.AddChild(new MeshInstance3D { Mesh = ModelLib.Get("buildings", "crane_jib_tip"), Position = new Vector3(0, 0, -nb * JibBay) });
        // tirants : du sommet du mât en A jusqu'au bout de la flèche et vers son milieu
        foreach (var c in top.GetChildren()) if (c.Name.ToString().StartsWith("tie")) { top.RemoveChild(c); c.QueueFree(); }
        _tieMat ??= new StandardMaterial3D { AlbedoColor = new Color("#b9c0c8"), Metallic = 0.6f, Roughness = 0.35f };
        float L = JibRoot + nb * JibBay;
        int k = 0;
        foreach (float f in new[] { 0.93f, 0.5f })
        {
            var to = new Vector3(0, JibZ + 0.44f, -L * f);
            var d = to - Apex;
            var y = d.Normalized();
            var x = y.Cross(Vector3.Right).Normalized();
            var z = x.Cross(y);
            top.AddChild(new MeshInstance3D
            {
                Name = $"tie{k++}", Mesh = new CylinderMesh { TopRadius = 0.028f, BottomRadius = 0.028f, Height = 1, RadialSegments = 8 }, MaterialOverride = _tieMat,
                Transform = new Transform3D(new Basis(x, y * d.Length(), z), (Apex + to) / 2),
            });
        }
    }

    // ───── Nœuds des machines ─────
    Node3D MakeNode(Building b)
    {
        var n = new Node3D { Position = new Vector3(b.Cx, Terrain.GroundAt(b.Cx, b.Cz), b.Cz), Rotation = new Vector3(0, YawOf(b.Dir), 0) };
        void M(string name, string node = null)
        {
            var mesh = ModelLib.Get("buildings", name);
            if (mesh == null) return;
            n.AddChild(new MeshInstance3D { Mesh = mesh, Name = node ?? name });
        }
        switch (b.Type)
        {
            case "harv": M("crane_base"); var top = new Node3D { Name = "top", Position = new Vector3(GameState.MastOff, 0, GameState.MastOff) }; n.AddChild(top); // le mât : case arrière droite
                top.AddChild(new MeshInstance3D { Mesh = ModelLib.Get("buildings", "crane_top") });
                // flèche : tronçons mis bout à bout selon la portée (EnsureJib), contre le porte-flèche
                top.AddChild(new Node3D { Name = "jib", Position = new Vector3(0, 0, -JibRoot) });
                top.AddChild(new MeshInstance3D { Mesh = ModelLib.Get("buildings", "crane_trolley"), Name = "trolley" });
                // harnais rayé au bout du câble (la fourmi s'y tient par les pattes arrière)
                var hook = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.035f, OuterRadius = 0.065f, Rings = 24, RingSegments = 10 }, Name = "hook",
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#f2c21c"), Roughness = 0.4f } }; top.AddChild(hook);
                var cable = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 1, RadialSegments = 6 }, Name = "cable",
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#3a3836"), Metallic = 0.6f, Roughness = 0.4f } };
                top.AddChild(cable);
                break;
            case "wheel": M("wheel_base"); M("wheel_bulb"); var wh = new Node3D { Name = "wheelPivot", Position = new Vector3(0, WheelAxle, 0) }; n.AddChild(wh);
                // (pas de suffixe « _wheel » : Godot en ferait une roue de véhicule à l'import)
                wh.AddChild(new MeshInstance3D { Mesh = ModelLib.Get("buildings", "wheel_rotor") }); break;
            case "store": M("store_base"); M("store_pile", "pile"); break;
            case "comp": M("comp_base"); M("comp_press", "press"); break;
            case "oven": M("oven_base"); M("oven_glow", "glow"); break;
            case "deco": M("deco_base"); M("deco_cherry", "cherry"); break;
        }
        if (HasPortBelts(b.Type))
        {
            if (_portOn == null)
            {
                _portOn = (ShaderMaterial)_beltMat.Duplicate();
                _portOff = (ShaderMaterial)_beltMat.Duplicate();
                _portOff.SetShaderParameter("frozen", true);
            }
            AddPortBelts(n, _portOff, b.Type);
            _portState[b.Id] = new bool[PortBelts(b.Type).Length];
        }
        AddPortArrows(n, b.Type).Visible = false; // cachées : seulement 1 s après une rotation
        if (b.Type == "wheel" || b.Type == "store") { /* rien */ }
        AddChild(n);
        return n;
    }

    // ───── Image par image ─────
    public void UpdateFrame(float dt, float alpha, List<AntPose> staff)
    {
        _time += dt;
        if (_game.PowerGen != _cableGen || _game.Buildings.Count != _cableCount) { _cableGen = _game.PowerGen; _cableCount = _game.Buildings.Count; BuildCables(); }
        _beltMat.SetShaderParameter("speed", (float)Fx.Belt(_game.UpgLvl["oil"]));
        _beltMatC.SetShaderParameter("speed", (float)Fx.Belt(_game.UpgLvl["oil"]));
        _beltMatGap.SetShaderParameter("speed", (float)Fx.Belt(_game.UpgLvl["oil"]));
        int ng = 0;
        var alive = new HashSet<int>();
        int ns = 0, nl = 0, nr = 0, nss = 0, nol = 0, nor = 0, nolr = 0;
        var itemCount = new Dictionary<string, int> { ["crumb"] = 0, ["ball"] = 0, ["small"] = 0, ["royal"] = 0 };
        float hRange = Fx.HRange(_game.UpgLvl["turbo"]);
        foreach (var b in _game.Buildings)
        {
            if (b.Type == "belt")
            {
                var (shape, open) = BeltShape(b);
                // ce tapis entre-t-il PAR LE CÔTÉ dans un autre tapis (jonction en T) ? raccord
                {
                    var (fdx, fdz) = Dirs.D[b.Dir];
                    var nb = _game.At(b.X + fdx, b.Z + fdz);
                    if (nb != null && nb.Type == "belt" && (nb.Dir & 1) != (b.Dir & 1) && ng < _bandGap.InstanceCount)
                    {
                        // repère du tapis : sortie vers −Z ; la bande voisine commence à 0,1 dans sa case
                        var gt = new Transform3D(new Basis(Vector3.Up, YawOf(b.Dir)), new Vector3(b.X + 0.5f, Terrain.GroundAt(b.X + 0.5f, b.Z + 0.5f), b.Z + 0.5f))
                            * new Transform3D(Basis.FromScale(new Vector3(1, 0.98f, 0.13f)), new Vector3(0, 0, -0.56f));
                        _bandGap.SetInstanceTransform(ng, gt);
                        _bandGap.SetInstanceColor(ng++, b.Powered ? Colors.White : new Color(0.45f, 0.45f, 0.45f));
                    }
                }
                var band = shape == 0 ? _bandS : shape < 0 ? _bandL : _bandR;
                int idx = shape == 0 ? ns++ : shape < 0 ? nl++ : nr++;
                var tr = new Transform3D(new Basis(Vector3.Up, YawOf(b.Dir)), new Vector3(b.X + 0.5f, Terrain.GroundAt(b.X + 0.5f, b.Z + 0.5f), b.Z + 0.5f));
                if (idx < band.InstanceCount)
                {
                    band.SetInstanceTransform(idx, tr);
                    band.SetInstanceColor(idx, b.Powered ? Colors.White : new Color(0.45f, 0.45f, 0.45f));
                }
                // cadre : virage, droit, ou droit ouvert sur le(s) côté(s) alimenté(s)
                MultiMesh fm; int fi;
                if (shape < 0) { fm = _beltCurveL; fi = idx; }
                else if (shape > 0) { fm = _beltCurveR; fi = idx; }
                else if (open == 1) { fm = _beltOpenL; fi = nol++; }
                else if (open == 2) { fm = _beltOpenR; fi = nor++; }
                else if (open == 3) { fm = _beltOpenLR; fi = nolr++; }
                else { fm = _beltStraight; fi = nss++; }
                if (fi < fm.InstanceCount) { fm.SetInstanceTransform(fi, tr); fm.SetInstanceColor(fi, Colors.White); }
            }
            else
            {
                alive.Add(b.Id);
                if (!_nodes.TryGetValue(b.Id, out var n)) _nodes[b.Id] = n = MakeNode(b);
                n.Rotation = new Vector3(0, YawOf(b.Dir), 0);
                Animate(b, n, dt, hRange, staff);
            }
            // objets sur les tapis
            if (b.It != null && itemCount.ContainsKey(b.It.T))
            {
                var it = b.It;
                var p = it.PrevPos.Lerp(it.Pos, Mathf.Clamp(alpha, 0, 1));
                float s = Mathf.Clamp(0.75f + 0.18f * MathF.Log10(1 + (float)it.Q), 0.75f, 1.7f);
                var mm = _items[it.T];
                int k = itemCount[it.T]++;
                if (k < mm.InstanceCount)
                {
                    mm.SetInstanceTransform(k, new Transform3D(new Basis(Vector3.Up, it.Id * 0.7f).Scaled(Vector3.One * s), new Vector3(p.X, 0.42f + Terrain.GroundAt(p.X, p.Y), p.Y)));
                    mm.SetInstanceColor(k, Colors.White);
                    mm.SetInstanceCustomData(k, ItemMeshes.CustomOf(it.C)); // couleur des miettes de son gâteau
                }
            }
        }
        // machines 3 × 3 : tapis intégrés (défilent si alimentés), objets en entrée et en sortie
        float bspeed = (float)Fx.Belt(_game.UpgLvl["oil"]);
        var byId = new Dictionary<int, Building>();
        foreach (var b in _game.Buildings) if (HasPortBelts(b.Type)) byId[b.Id] = b;
        _portOn?.SetShaderParameter("speed", bspeed);
        foreach (var (id, st) in _portState)
        {
            if (!byId.TryGetValue(id, out var b) || !_nodes.TryGetValue(id, out var node)) continue;
            var box = node.GetNodeOrNull<Node3D>("PortBelts");
            if (box == null) continue;
            for (int i = 0; i < st.Length; i++)
            {
                bool on = b.Powered && _game.PortLinked(b, i);
                if (on == st[i]) continue;
                st[i] = on;
                if (box.GetNodeOrNull<MeshInstance3D>($"band{i}") is { } band) band.MaterialOverride = on ? _portOn : _portOff;
            }
        }
        foreach (var id in _portState.Keys.ToList()) if (!byId.ContainsKey(id)) _portState.Remove(id);
        void Item(string type, int cake, Vector3 p, float spin)
        {
            if (!_items.TryGetValue(type, out var mm)) return;
            int k = itemCount[type]++;
            if (k >= mm.InstanceCount) return;
            mm.SetInstanceTransform(k, new Transform3D(new Basis(Vector3.Up, spin), p));
            mm.SetInstanceColor(k, Colors.White);
            mm.SetInstanceCustomData(k, ItemMeshes.CustomOf(cake));
        }
        for (int i = _portItems.Count - 1; i >= 0; i--)
        {
            var it = _portItems[i];
            if (!byId.TryGetValue(it.id, out var b)) { _portItems.RemoveAt(i); continue; }
            if (b.Powered) it.s += dt * bspeed;
            if (it.s >= 0.75f) { _portItems.RemoveAt(i); continue; }
            _portItems[i] = it;
            var c = new Vector2(b.X + 0.5f, b.Z + 0.5f) + it.side * (1.5f - it.s);
            Item(it.type, it.cake, new Vector3(c.X, Terrain.GroundAt(c.X, c.Y) + 0.42f, c.Y), i * 1.3f);
        }
        foreach (var b in byId.Values)
        {
            if (b.OutIt == null) continue;
            var (dx, dz) = Dirs.D[b.Dir];
            // du capot (ou de la trémie de la grue) jusqu'au bord de la case de sortie
            var (px, pz) = GameState.PortCell(b);
            var f = new Vector2(dx, dz);
            var edge = new Vector2(px + 0.5f, pz + 0.5f) + f * 0.5f;
            float run = b.Size == 3 ? 0.75f : 0.8f;
            var c = edge - f * run * (1 - b.OutP);
            Item(b.OutIt.T, b.OutIt.C, new Vector3(c.X, Terrain.GroundAt(c.X, c.Y) + 0.42f, c.Y), b.OutIt.Id * 0.7f);
        }
        _bandS.VisibleInstanceCount = Math.Min(ns, _bandS.InstanceCount);
        _bandGap.VisibleInstanceCount = Math.Min(ng, _bandGap.InstanceCount);
        _beltStraight.VisibleInstanceCount = Math.Min(nss, _beltStraight.InstanceCount);
        _beltOpenL.VisibleInstanceCount = Math.Min(nol, _beltOpenL.InstanceCount);
        _beltOpenR.VisibleInstanceCount = Math.Min(nor, _beltOpenR.InstanceCount);
        _beltOpenLR.VisibleInstanceCount = Math.Min(nolr, _beltOpenLR.InstanceCount);
        _beltCurveL.VisibleInstanceCount = _bandL.VisibleInstanceCount = Math.Min(nl, _beltCurveL.InstanceCount);
        _beltCurveR.VisibleInstanceCount = _bandR.VisibleInstanceCount = Math.Min(nr, _beltCurveR.InstanceCount);
        foreach (var (t, c) in itemCount) _items[t].VisibleInstanceCount = Math.Min(c, _items[t].InstanceCount);
        foreach (var id in _nodes.Keys.ToList())
            if (!alive.Contains(id)) { _nodes[id].QueueFree(); _nodes.Remove(id); }
    }

    // ───── Câbles électriques : de la dynamo de chaque roue aux machines qu'elle alimente ─────
    // Un câble par machine ; pour les tapis, un seul câble par rangée continue alimentée par la même roue.
    void BuildCables()
    {
        _cables?.QueueFree(); _cables = null;
        var byId = new Dictionary<int, Building>();
        foreach (var b in _game.Buildings) byId[b.Id] = b;
        var targets = new List<(Building w, Building b)>();
        var seen = new HashSet<int>();
        foreach (var b in _game.Buildings)
        {
            if (b.Source < 0 || !byId.TryGetValue(b.Source, out var w) || seen.Contains(b.Id)) continue;
            if (b.Type != "belt") { targets.Add((w, b)); continue; }
            // rangée de tapis connexe (même roue) : on la parcourt, et on branche la case la plus proche
            var stack = new Stack<Building>(); stack.Push(b); seen.Add(b.Id);
            Building best = b; float bd = float.MaxValue;
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                float d = new Vector2(c.X - w.X, c.Z - w.Z).LengthSquared();
                if (d < bd) { bd = d; best = c; }
                foreach (var (dx, dz) in Dirs.D)
                {
                    var n = _game.At(c.X + dx, c.Z + dz);
                    if (n != null && n.Type == "belt" && n.Source == w.Id && seen.Add(n.Id)) stack.Push(n);
                }
            }
            targets.Add((w, best));
        }
        if (targets.Count == 0) return;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (w, b) in targets) Cable(st, w, b);
        st.GenerateNormals();
        var mesh = st.Commit();
        _cables = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#24201c"), Roughness = 0.35f, ClearcoatEnabled = true, Clearcoat = 0.5f, VertexColorUseAsAlbedo = true },
        };
        AddChild(_cables);
    }

    void Cable(SurfaceTool st, Building w, Building b)
    {
        float G(float x, float z) => Terrain.GroundAt(x, z);
        var wc = new Vector3(w.Cx, 0, w.Cz);
        var bc = new Vector3(b.Cx, 0, b.Cz);
        var wy = new Basis(Vector3.Up, YawOf(w.Dir));
        // départ : sous la dynamo de la roue (côté +X local), on descend au sol
        var dyn = wc + wy * new Vector3(0.76f, 0, 0);
        var toB = (bc - dyn); toB.Y = 0;
        float L = toB.Length();
        if (L < 0.3f) return;
        var dir = toB / L;
        // arrivée : contre la machine, au bord de sa case tourné vers la roue (un tapis : sous son flanc)
        var end = bc - dir * (b.Type == "belt" ? 0.42f : 0.55f + (b.Ext - 0.5f) * 0.95f);
        var pts = new List<Vector3>();
        pts.Add(dyn + new Vector3(0, 0.45f, 0) + dir * 0.12f);      // sortie du boîtier de la dynamo
        pts.Add(dyn + new Vector3(0, 0.2f, 0) + dir * 0.25f);
        // au sol, légère ondulation (un vrai câble n'est jamais tendu)
        var side = new Vector3(-dir.Z, 0, dir.X);
        float span = (end - dyn).Length();
        int n = Math.Max(2, (int)(span / 0.5f));
        float ph = (w.Id * 1.7f + b.Id * 0.9f) % 6.28f;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            var p = dyn.Lerp(end, t) + side * MathF.Sin(t * MathF.PI * 2 + ph) * 0.18f * MathF.Sin(t * MathF.PI);
            p.Y = G(p.X, p.Z) + 0.035f;
            if (i > 0 && i < n) pts.Add(p);
        }
        pts.Add(end + new Vector3(0, G(end.X, end.Z) + 0.035f, 0));
        pts.Add(end + new Vector3(0, G(end.X, end.Z) + (b.Type == "belt" ? 0.18f : 0.32f), 0) - dir * 0.02f); // la prise
        Tube(st, pts, 0.03f, new Color("#2a2420"));
        // prise en cuivre au bout
        var tip = pts[^1];
        Tube(st, new List<Vector3> { tip - new Vector3(0, 0.06f, 0), tip + new Vector3(0, 0.03f, 0) }, 0.05f, new Color("#c77a3a"));
    }

    static void Tube(SurfaceTool st, List<Vector3> pts, float r, Color col)
    {
        const int S = 6;
        st.SetColor(col);
        Vector3 prevN = Vector3.Up;
        var rings = new List<Vector3[]>();
        for (int i = 0; i < pts.Count; i++)
        {
            var t = (pts[Math.Min(i + 1, pts.Count - 1)] - pts[Math.Max(i - 1, 0)]).Normalized();
            var n0 = MathF.Abs(t.Dot(prevN)) > 0.95f ? t.Cross(Vector3.Right).Normalized() : (prevN - t * prevN.Dot(t)).Normalized();
            prevN = n0;
            var b0 = t.Cross(n0);
            var ring = new Vector3[S];
            for (int k = 0; k < S; k++) { float a = k * MathF.Tau / S; ring[k] = pts[i] + (n0 * MathF.Cos(a) + b0 * MathF.Sin(a)) * r; }
            rings.Add(ring);
        }
        for (int i = 0; i < rings.Count - 1; i++)
            for (int k = 0; k < S; k++)
            {
                int k2 = (k + 1) % S;
                st.AddVertex(rings[i][k]); st.AddVertex(rings[i + 1][k]); st.AddVertex(rings[i + 1][k2]);
                st.AddVertex(rings[i][k]); st.AddVertex(rings[i + 1][k2]); st.AddVertex(rings[i][k2]);
            }
    }

    // flèches d'entrée/sortie des machines posées : seulement 1 s après les avoir tournées
    // (en mode construction, seul le bâtiment en cours de pose montre les siennes)
    public bool ShowArrows;
    // toutes les opératrices des machines : orange, casque de chantier
    public static readonly Color OperatorColor = new("#f5841f");
    readonly Dictionary<int, float> _arrowFlash = new();
    public void FlashArrows(int id) => _arrowFlash[id] = 1f;

    void Animate(Building b, Node3D n, float dt, float range, List<AntPose> staff)
    {
        _arrowFlash.TryGetValue(b.Id, out float fl);
        if (fl > 0) _arrowFlash[b.Id] = fl - dt;
        bool arrows = ShowArrows || fl > 0;
        if (n.GetNodeOrNull<Node3D>(ArrowsNode) is { } box && box.Visible != arrows) box.Visible = arrows;
        var basePos = new Vector3(b.Cx, Terrain.GroundAt(b.Cx, b.Cz), b.Cz);
        float yaw = YawOf(b.Dir);
        Vector3 Local(Vector3 l) => basePos + new Basis(Vector3.Up, yaw) * l;
        bool working = b.State == "ok" && b.Powered;
        // l'opératrice de la machine, à la place prévue par le modèle (repère « op_<type> »)
        void Operator(string type, Node3D frame, float scale, float bite = 0, float walk = 0, float gait = 0, float pitch = 0, float time = -1)
        {
            if (ModelLib.Marker("buildings", "op_" + type + "_spot") is not { } mk) return;
            var gt = frame.GlobalTransform;
            var fwd = gt.Basis * mk.fwd;
            staff.Add(new AntPose
            {
                Pos = gt * mk.pos, Yaw = MathF.Atan2(fwd.X, fwd.Z), Pitch = pitch, Scale = scale, Time = time < 0 ? _time + b.Id : time,
                Color = OperatorColor, Helmet = true, Bite = bite, Walk = walk, Gait = gait,
            });
        }
        switch (b.Type)
        {
            case "harv":
            {
                var top = n.GetNode<Node3D>("top");
                var mastPos = new Vector3(top.GlobalPosition.X, basePos.Y, top.GlobalPosition.Z);
                var tgt = b.CraneTarget == Vector3.Zero ? mastPos + new Basis(Vector3.Up, yaw) * new Vector3(0, 0, -3) : b.CraneTarget;
                var d = tgt - mastPos;
                float want = MathF.Atan2(-d.X, -d.Z) - yaw;
                var r = top.Rotation;
                top.Rotation = new Vector3(0, Mathf.LerpAngle(r.Y, want, Mathf.Min(1, dt * 2)), 0);
                float hd = Mathf.Clamp(new Vector2(d.X, d.Z).Length(), 1.2f, range + 1);
                EnsureJib(b, top, range);
                var trolley = top.GetNode<Node3D>("trolley");
                var tp = trolley.Position;
                trolley.Position = new Vector3(0, 0, Mathf.Lerp(tp.Z, -hd, Mathf.Min(1, dt * 3)));
                // le câble descend jusqu'au dessus du gâteau quand la grue travaille
                float H = 13.55f;
                // la tête de la fourmi suspendue touche le gâteau, puis remonte entre deux bouchées
                float bite = working ? MathF.Max(0, MathF.Sin(_time * 5 + b.Id)) : 0;
                float hookY = working ? MathF.Max(tgt.Y + 0.62f, 1.0f) + (1 - bite) * 0.3f : H - 1.6f;
                var hook = top.GetNode<Node3D>("hook");
                hook.Position = new Vector3(0, hookY, trolley.Position.Z);
                var cable = top.GetNode<MeshInstance3D>("cable");
                float cl = H - 0.05f - hookY;
                cable.Position = new Vector3(0, hookY + cl / 2, trolley.Position.Z);
                cable.Scale = new Vector3(1, MathF.Max(0.01f, cl), 1);
                // la fourmi pendue tête en bas, les pattes arrière agrippées au harnais
                var trolleyZ = trolley.Position.Z;
                var ring = top.GlobalTransform * new Vector3(0, hookY, trolleyZ);
                LastCraneRing = ring;
                float ry = top.GlobalRotation.Y + MathF.Sin(_time * 0.8f + b.Id) * 0.15f;
                const float hs = 0.62f;
                var rootPos = ring - new Basis(Vector3.Up, ry) * new Vector3(0, 0.55f, 0.47f) * hs;
                staff.Add(new AntPose { Pos = rootPos, Yaw = ry, Pitch = MathF.PI / 2, Scale = hs, Hang = 1, Time = _time + b.Id, Color = OperatorColor, Helmet = true,
                    Bite = bite > 0.85f ? 1 : 0, Load = working ? 0.5f : 0, LoadColor = Cfg.CrumbColor(b.LastC) });
                // la grutière, assise dans la cabine vitrée, face au pupitre et à la flèche
                Operator("crane", top, 0.4f, bite: working && MathF.Sin(_time * 5 + b.Id) > 0.8f ? 0.4f : 0, time: _time * (working ? 2 : 1) + b.Id);
                break;
            }
            case "wheel":
            {
                var pv = n.GetNode<Node3D>("wheelPivot");
                pv.Rotate(Vector3.Right, dt * 2.6f);
                // la coureuse, dans la roue
                Operator("wheel", n, 0.8f, walk: 1, gait: _time * 11, time: _time);
                if (n.GetNodeOrNull<MeshInstance3D>("wheel_bulb") is { } bulb) bulb.Visible = MathF.Sin(_time * 9) > -0.6f;
                break;
            }
            case "store":
            {
                var pile = n.GetNode<Node3D>("pile");
                float f = (float)Math.Clamp(b.Stock / GameState.StoreMax, 0, 1);
                pile.Visible = f > 0.002f;
                pile.Position = new Vector3(0, 0.36f, 0); // fond de la cuve
                pile.Scale = new Vector3(0.45f + 0.55f * MathF.Sqrt(f), 0.15f + 1.35f * f, 0.45f + 0.55f * MathF.Sqrt(f));
                // la pile a la couleur des miettes dont l'entrepôt est le plus rempli
                if (pile is MeshInstance3D pm && b.Stock > 0)
                {
                    int dom = 0;
                    for (int c = 1; c < b.StockC.Length; c++) if (b.StockC[c] > b.StockC[dom]) dom = c;
                    if (pm.MaterialOverride is not StandardMaterial3D sm) pm.MaterialOverride = sm = new StandardMaterial3D { Roughness = 0.9f };
                    sm.AlbedoColor = Cfg.CrumbColor(dom);
                }
                // la magasinière, sur la passerelle au-dessus du bac
                Operator("store", n, 0.6f, bite: b.Anim > 0.5f ? 1 : 0);
                break;
            }
            case "comp":
            {
                var press = n.GetNode<Node3D>("press");
                float k = b.Anim > 0.01f ? MathF.Abs(MathF.Sin(_time * 5)) : 0;
                press.Position = new Vector3(0, 1.2f - k * 0.72f, 0); // jusqu'à l'enclume
                // l'opératrice, dans la cabine posée sur la traverse de la presse
                Operator("comp", n, 0.6f, bite: b.Anim > 0.1f ? MathF.Abs(MathF.Sin(_time * 6)) : 0);
                break;
            }
            case "oven":
            {
                var glow = n.GetNode<Node3D>("glow");
                glow.Visible = b.Batch > 0 || b.Anim > 0.1f;
                // la boulangère, dans l'alcôve arrière, au tableau de bord du four
                Operator("oven", n, 0.6f, bite: b.Batch > 0 ? MathF.Abs(MathF.Sin(_time * 4)) : 0);
                break;
            }
            case "deco":
            {
                var ch = n.GetNode<Node3D>("cherry");
                ch.Position = new Vector3(0, 0.75f + (b.Batch > 0 ? MathF.Abs(MathF.Sin(_time * 3.5f)) * 0.35f : 0.35f), 0);
                // la décoratrice, sur l'estrade arrière
                Operator("deco", n, 0.6f, bite: b.Batch > 0 ? MathF.Abs(MathF.Sin(_time * 5)) : 0);
                break;
            }
        }
    }
}
