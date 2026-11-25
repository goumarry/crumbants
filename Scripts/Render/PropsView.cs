// ─────────────────────────────────────────────────────────────
// Décor du pique-nique : objets géants modélisés dans Blender
// (Models/props.glb), leurs collisions ajustées (cercles et
// rectangles orientés), le bureau de la gestionnaire, les panneaux
// des gâteaux, le portique du nid, les serpentins et le chemin de
// fourmis qui se trace jusqu'aux gâteaux débloqués.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public static class Props
{
    // [nom, x, z, rotation]
    public static readonly (string name, float x, float z, float rot)[] Placed =
    {
        ("basket", 30, -248, 0.35f), ("bowl", 34, -88, 0), ("fruit_salad", 131, -267, 0.4f), ("napkin", -44, -78, -0.35f),
        ("fork", -55, -72, -0.35f + 0.785f), ("glass", -73, -25, 0.8f), ("bottle", -54, -234, 0), ("crown", -165, -147, 0.3f),
        ("jar", -101, -175, 0), ("cherries", 138, -112, 0.6f), ("sugar", 42, -150, 0.2f), ("cork", 68, 2, 0.7f),
        ("cherry", -132, -12, 0), ("knife", -140, 30, 0.2f),
        ("spoon", -78, -198, 1.6f),
        ("leaf_dry", -196, -285, 0.9f), ("leaf_dry", -202, -195, -0.6f), ("leaf_dry", 183, -194, 2.2f), ("leaf_dry", -200, -119, 1.3f),
    };
    public static readonly (float x, float z, float yaw, float len)[] Streamers = { (-96, -290, 0, 70), (-164, 6, 2.1f, 45), (168, -86, 1.4f, 60), (100, 22, 0, 55) };

    public static Vector2 StreamerPoint(int i, float t)
    {
        var (x, z, yaw, len) = Streamers[i];
        float lx = t * len + MathF.Sin(t * 5) * 6, lz = MathF.Sin(t * 3 + i) * 10;
        return new Vector2(x + lx * MathF.Cos(yaw) + lz * MathF.Sin(yaw), z - lx * MathF.Sin(yaw) + lz * MathF.Cos(yaw));
    }

    // Serpentin de papier : un ruban enroulé en ressort, couché le long de son tracé.
    // Les deux bouts sont déroulés à plat ; quelques tronçons sont plus étirés.
    static readonly Dictionary<int, ArrayMesh> _streamerMesh = new();
    public static readonly string[] StreamerColors = { "#e8475f", "#3fa0e0", "#f6c13a", "#5cc46a" };
    public static ArrayMesh StreamerMesh(int i)
    {
        if (_streamerMesh.TryGetValue(i, out var cached)) return cached;
        var (_, _, _, len) = Streamers[i];
        var rng = new Random(31 + i);
        const float W = 1.25f;                   // largeur du ruban (le long de l'axe du ressort)
        int turns = (int)(len / 2.3f), seg = 22, n = turns * seg;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Axis(float t) { var p = StreamerPoint(i, Mathf.Clamp(t, 0, 1)); return new Vector3(p.X, 0, p.Y); }
        Vector3 prevL = default, prevR = default, prevN = default;
        float ph = (float)rng.NextDouble() * 6;
        for (int k = 0; k <= n; k++)
        {
            float t = k / (float)n;
            var c = Axis(t);
            var tg = (Axis(t + 0.002f) - Axis(t - 0.002f)).Normalized();
            var side = tg.Cross(Vector3.Up).Normalized();
            // rayon : bouts déroulés, petites variations d'enroulement
            float unroll = Mathf.SmoothStep(0, 0.08f, t) * Mathf.SmoothStep(1, 0.9f, t);
            float R = (1.9f + 0.45f * MathF.Sin(t * 17 + i) + 0.25f * MathF.Sin(t * 41)) * unroll + 0.05f;
            float phi = t * turns * MathF.Tau + ph;
            var radial = side * MathF.Cos(phi) + Vector3.Up * MathF.Sin(phi);
            var ctr = c + Vector3.Up * (R + 0.03f) + radial * R;
            var L = ctr - tg * W / 2; var Rr = ctr + tg * W / 2;
            if (k > 0)
            {
                st.SetNormal(prevN); st.AddVertex(prevL);
                st.SetNormal(radial); st.AddVertex(Rr);
                st.SetNormal(prevN); st.AddVertex(prevR);
                st.SetNormal(prevN); st.AddVertex(prevL);
                st.SetNormal(radial); st.AddVertex(L);
                st.SetNormal(radial); st.AddVertex(Rr);
            }
            prevL = L; prevR = Rr; prevN = radial;
        }
        var m = st.Commit();
        _streamerMesh[i] = m;
        return m;
    }

    static readonly List<(float x, float z, float r)> Circles = new();
    static readonly List<(float x, float z, float hx, float hz, float rot)> Rects = new();

    static Vector2 Local(float ox, float oz, float rot, float lx, float lz) =>
        new(ox + lx * MathF.Cos(rot) + lz * MathF.Sin(rot), oz - lx * MathF.Sin(rot) + lz * MathF.Cos(rot));

    // la fourchette est posée sur la serviette (hauteur calculée par PropField)
    public static bool RestsOnOthers(string name) => name == "fork";

    // bureau-boîte d'allumettes et chaise de la gestionnaire (nom, position, rotation, échelle)
    public static IEnumerable<(string name, Vector3 pos, float rot, Vector3 scale)> Extra()
    {
        yield return ("desk", new Vector3(Layout.Manager.X, 0, Layout.Manager.Y), 0, Vector3.One);
        yield return ("chair", new Vector3(Layout.Manager.X, 0, Layout.Manager.Y + 2.15f), MathF.PI, Vector3.One);
    }

    static Props()
    {
        // les objets du décor et les serpentins sont solides par leur vraie forme (PropField, PropPhysics)
    }

    // panneau planté devant le gâteau k : position et direction (vers la porte)
    public static (Vector2 pos, Vector2 dir) SignOf(int k)
    {
        var c = new Vector2(CakePos[k].X, CakePos[k].Y);
        var door = k == 0 ? new Vector2(Nest.X, Nest.Y) : Layout.Door(k);
        var dir = (door - c).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        float rr = k == 0 ? CakeR + 3.2f : ClocheR + 3;
        return (c + dir * rr + side * (k == 0 ? 9 : 6), dir);
    }

    static bool _extra;
    static void AddExtra()
    {
        _extra = true;
        for (int k = 0; k < NbCakes; k++)
        {
            var (p, dir) = SignOf(k);
            Rects.Add((p.X, p.Y, 1.9f, 0.4f, MathF.Atan2(dir.X, dir.Y)));
        }
        // tunnels de terre des guichets de livraison
        foreach (var (cell, o) in NestDocks)
        {
            var c = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
            foreach (var d in new[] { 1.3f, 2.3f }) Circles.Add((c.X - o.X * d, c.Y - o.Y * d, 0.9f));
        }
        // poteaux du portique du nid
        Circles.Add((Nest.X - 4.2f, Nest.Y - 4.2f, 0.35f));
        Circles.Add((Nest.X + 4.2f, Nest.Y - 4.2f, 0.35f));
    }

    // pour les ouvrières et la construction : objets du décor (dès qu'ils dépassent
    // une demi-fourmi) et obstacles fixes
    public static bool Blocked(float x, float z, float r) => PropField.HeightDisc(x, z, r) > 0.45f || BlockedFixed(x, z, r);

    // pour la fourmi du joueur : seulement les obstacles fixes (elle escalade les objets)
    public static bool BlockedFixed(float x, float z, float r)
    {
        if (!_extra) AddExtra();
        foreach (var (cx, cz, cr) in Circles)
        {
            float dx = x - cx, dz = z - cz, rr = cr + r;
            if (dx * dx + dz * dz < rr * rr) return true;
        }
        foreach (var (bx, bz, hx, hz, rot) in Rects)
        {
            float dx = x - bx, dz = z - bz;
            if (MathF.Abs(dx) > hx + hz + r && MathF.Abs(dz) > hx + hz + r) continue;
            float c = MathF.Cos(rot), s = MathF.Sin(rot);
            float lx = dx * c - dz * s, lz = dx * s + dz * c;
            if (MathF.Abs(lx) < hx + r && MathF.Abs(lz) < hz + r) return true;
        }
        return false;
    }
}

public partial class PropsView : Node3D
{
    GameState _game;
    // textes des panneaux des gâteaux : bannière (titre), ardoise (nom, valeur), plaquette (état) ;
    // une série par face. Les chaînes passent par Tr() : il suffira d'ajouter les traductions.
    sealed class SignLabels { public Label3D Title, Name, Sub, State; }
    readonly Dictionary<int, List<SignLabels>> _signs = new();

    public void Setup(GameState game)
    {
        _game = game;
        PropField.Build();
        // obstacles des trajets des ouvrières : l'empreinte au sol de chaque objet
        Layout.PropObstacles = () =>
        {
            var l = new List<(Vector2, float)>();
            for (int i = 0; i < Props.Placed.Length; i++)
            {
                var m = ModelLib.Get("props", Props.Placed[i].name);
                if (m == null) continue;
                var bb = m.GetAabb(); var xf = PropField.XformOf(i);
                var c = xf * bb.GetCenter();
                l.Add((new Vector2(c.X, c.Z), new Vector2(bb.Size.X, bb.Size.Z).Length() * 0.5f * 0.92f));
            }
            return l;
        };
        for (int i = 0; i < Props.Placed.Length; i++)
        {
            var mesh = ModelLib.Get("props", Props.Placed[i].name);
            if (mesh == null) continue;
            AddChild(new MeshInstance3D { Mesh = mesh, Transform = PropField.XformOf(i) });
            PropPhysics.Add(this, mesh, PropField.XformOf(i));
        }
        foreach (var (name, pos, rot, scale) in Props.Extra())
        {
            var mesh = ModelLib.Get("props", name);
            if (mesh == null) continue;
            var xf = new Transform3D(new Basis(Vector3.Up, rot).Scaled(scale), pos);
            AddChild(new MeshInstance3D { Mesh = mesh, Transform = xf });
            PropPhysics.Add(this, mesh, xf);
        }
        for (int i = 0; i < Props.Streamers.Length; i++) PropPhysics.Add(this, Props.StreamerMesh(i), Transform3D.Identity);
        // plaque de laiton vissée sur le grand côté de la boîte
        var plate = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2.6f, 0.42f, 0.03f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#d9ac4a"), Metallic = 0.8f, Roughness = 0.3f },
            Position = new Vector3(Layout.Manager.X - 0.9f, 0.52f, Layout.Manager.Y - 1.43f),
        };
        AddChild(plate);
        var plaque = new Label3D
        {
            Text = "Gestion des ouvrières", FontSize = 64, PixelSize = 0.0042f, Modulate = new Color("#3a2410"), Font = UI.UiSkin.Bold,
            OutlineSize = 0, Position = new Vector3(Layout.Manager.X - 0.9f, 0.52f, Layout.Manager.Y - 1.452f),
            Rotation = new Vector3(0, MathF.PI, 0),
        };
        AddChild(plaque);
        BuildStreamers();
        BuildNestGate();
        BuildSigns();
    }

    void BuildStreamers()
    {
        for (int i = 0; i < Props.Streamers.Length; i++)
        {
            var col = new Color(Props.StreamerColors[i % Props.StreamerColors.Length]);
            AddChild(new MeshInstance3D
            {
                Mesh = Props.StreamerMesh(i),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = col, Roughness = 0.55f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    BacklightEnabled = true, Backlight = col * 0.45f, // papier : la lumière passe un peu au travers
                },
            });
        }
        BuildConfetti();
    }

    // confettis de papier éparpillés sur la nappe (ronds et carrés, à plat, un peu de travers)
    void BuildConfetti()
    {
        var rng = new Random(77);
        var round = new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.05f, RadialSegments = 14, Rings = 1 };
        var square = new BoxMesh { Size = new Vector3(0.95f, 0.05f, 0.95f) };
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.5f, BacklightEnabled = true, Backlight = new Color(0.3f, 0.3f, 0.3f) };
        round.Material = mat; square.Material = mat;
        var cols = new[] { "#e8475f", "#3fa0e0", "#f6c13a", "#5cc46a", "#b06ad9", "#f08a3a", "#ffffff" };
        var lists = new[] { new List<(Transform3D, Color)>(), new List<(Transform3D, Color)>() };
        int tries = 0;
        while (lists[0].Count + lists[1].Count < 900 && tries++ < 20000)
        {
            float x = BlanketX0 + 3 + (float)rng.NextDouble() * (BlanketX1 - BlanketX0 - 6);
            float z = BlanketZ0 + 3 + (float)rng.NextDouble() * (BlanketZ1 - BlanketZ0 - 6);
            // en petits nuages autour des serpentins, quelques-uns égarés ailleurs
            bool near = false;
            for (int i = 0; i < Props.Streamers.Length && !near; i++)
                for (float t = 0; t <= 1; t += 0.05f) if ((Props.StreamerPoint(i, t) - new Vector2(x, z)).LengthSquared() < 18 * 18) { near = true; break; }
            if (!near && rng.NextDouble() > 0.08) continue;
            bool onCake = false;
            foreach (var c in CakePos) if (new Vector2(x - c.X, z - c.Y).Length() < Terrain.PlateOuterR + 5) onCake = true;
            if (onCake || PropField.Height(x, z) > 0.05f) continue;
            var basis = new Basis(Vector3.Up, (float)rng.NextDouble() * MathF.Tau) * new Basis(Vector3.Right, ((float)rng.NextDouble() - 0.5f) * 0.25f);
            int kind = rng.Next(2);
            lists[kind].Add((new Transform3D(basis, new Vector3(x, 0.04f, z)), new Color(cols[rng.Next(cols.Length)])));
        }
        Mesh[] meshes = { round, square };
        for (int k = 0; k < 2; k++)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = meshes[k], InstanceCount = lists[k].Count };
            for (int i = 0; i < lists[k].Count; i++) { mm.SetInstanceTransform(i, lists[k][i].Item1); mm.SetInstanceColor(i, lists[k][i].Item2); }
            AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    // portique du nid : poteaux, traverse, fanions et lanternes
    void BuildNestGate()
    {
        var wood = new StandardMaterial3D { AlbedoColor = new Color("#8a5a32"), Roughness = 0.7f };
        float x0 = Nest.X - 4.2f, x1 = Nest.X + 4.2f, z = Nest.Y - 4.2f;
        foreach (var x in new[] { x0, x1 })
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.16f, Height = 4.6f, RadialSegments = 10 }, MaterialOverride = wood, Position = new Vector3(x, 2.3f, z) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(9.2f, 0.28f, 0.28f) }, MaterialOverride = wood, Position = new Vector3(Nest.X, 4.5f, z) });
        var flagCols = new[] { "#e8475f", "#f6c13a", "#3fa0e0", "#5cc46a", "#f08a3a" };
        for (int i = 0; i < 11; i++)
        {
            float x = x0 + 0.5f + i * 0.72f;
            float sag = 0.5f * MathF.Sin(MathF.PI * (i + 0.5f) / 11);
            AddChild(new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(0.5f, 0.6f, 0.03f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(flagCols[i % 5]), Roughness = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
                Position = new Vector3(x, 4.1f - sag, z), Rotation = new Vector3(0, 0, MathF.PI),
            });
        }
        var glow = new StandardMaterial3D { AlbedoColor = new Color("#ffd27a"), EmissionEnabled = true, Emission = new Color("#ffb347"), EmissionEnergyMultiplier = 2.2f };
        foreach (var x in new[] { x0, x1 })
        {
            AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.28f, Height = 0.6f }, MaterialOverride = glow, Position = new Vector3(x, 3.8f, z - 0.35f) });
            AddChild(new OmniLight3D { LightColor = new Color("#ffc27a"), LightEnergy = 0.8f, OmniRange = 5, Position = new Vector3(x, 3.8f, z - 0.6f) });
        }
        // pancarte suspendue à la traverse
        var board = ModelLib.Get("nest", "nest_sign");
        if (board != null)
        {
            var sn = new Node3D { Position = new Vector3(Nest.X, 3.35f, z) };
            AddChild(sn);
            sn.AddChild(new MeshInstance3D { Mesh = board });
            foreach (var side in new[] { 1, -1 })
                sn.AddChild(new Label3D
                {
                    Text = "Fourmilière", FontSize = 64, PixelSize = 0.009f, Modulate = new Color("#fff4dc"), OutlineSize = 12, OutlineModulate = new Color("#4a2a12"),
                    Position = new Vector3(0, 0.02f, side * 0.13f), Rotation = new Vector3(0, side > 0 ? 0 : MathF.PI, 0), Font = UI.UiSkin.Bold,
                });
        }
    }

    // le panneau de chaque gâteau : ardoise de pâtissier dans un cadre cintré (Blender/buildings2.py,
    // « cake_sign »), bannière rouge pour le titre, plaquette suspendue pour l'état. Textes en Label3D.
    void BuildSigns()
    {
        var board = ModelLib.Get("buildings", "cake_sign");
        foreach (var K in _game.Cakes)
        {
            var (p, dir) = Props.SignOf(K.Index);
            var n = new Node3D { Position = new Vector3(p.X, Terrain.GroundAt(p.X, p.Y), p.Y), Rotation = new Vector3(0, MathF.Atan2(dir.X, dir.Y), 0) };
            AddChild(n);
            if (board != null) n.AddChild(new MeshInstance3D { Mesh = board });
            var faces = new List<SignLabels>();
            foreach (var face in new[] { 1, -1 })
            {
                var rot = new Vector3(0, face > 0 ? 0 : MathF.PI, 0);
                Label3D L(float y, float z, int size, float px, Color col, Font font, int outline, Color outCol, float width)
                {
                    var l = new Label3D
                    {
                        FontSize = size, PixelSize = px, Modulate = col, OutlineSize = outline, OutlineModulate = outCol, Font = font,
                        Position = new Vector3(0, y, face * z), Rotation = rot, HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, Width = width / px, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                        LineSpacing = -4, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    };
                    n.AddChild(l);
                    return l;
                }
                faces.Add(new SignLabels
                {
                    // bannière : le numéro du gâteau
                    Title = L(2.965f, 0.118f, 72, 0.0034f, new Color("#fff4dc"), UI.UiSkin.Bold, 10, new Color("#6e1414"), 1.4f),
                    // ardoise : le nom, écrit à la main (craie), et la valeur d'une miette
                    Name = L(2.22f, 0.103f, 96, 0.0042f, new Color("#f6f0dc"), UI.UiSkin.Script, 0, Colors.Black, 2.45f),
                    Sub = L(1.6f, 0.103f, 56, 0.0036f, new Color("#ffd98a"), UI.UiSkin.Bold, 0, Colors.Black, 2.4f),
                    // plaquette suspendue : l'état
                    State = L(0.63f, 0.052f, 56, 0.0034f, new Color("#4a2a12"), UI.UiSkin.Bold, 0, Colors.Black, 1.8f),
                });
            }
            _signs[K.Index] = faces;
        }
    }

    // textes d'un panneau (point unique pour les traductions)
    (string title, string name, string sub, string state) SignTexts(CakeSim K)
    {
        string title = L.F("Gâteau {0}", K.N);
        string name = K.Theme.Name;
        string sub = L.F("×{0} par miette", Fmt(K.Vm));
        string state = !K.Unlocked ? L.F("Rapporte la fève n°{0}", K.Index)
            : K.Found ? (K.Remaining <= 0.5 ? L.T("Fève trouvée · tout croqué !") : L.T("Fève trouvée !"))
            : L.T("Fève à trouver");
        return (title, name, sub, state);
    }

    public void UpdateFrame()
    {
        foreach (var (i, faces) in _signs)
        {
            var (title, name, sub, state) = SignTexts(_game.Cakes[i]);
            foreach (var f in faces)
            {
                if (f.Title.Text != title) f.Title.Text = title;
                if (f.Name.Text != name)
                {
                    f.Name.Text = name;
                    f.Name.FontSize = name.Length > 15 ? 76 : 96; // noms longs : un peu plus petit
                }
                if (f.Sub.Text != sub) f.Sub.Text = sub;
                if (f.State.Text != state) f.State.Text = state;
            }
        }
    }
}
