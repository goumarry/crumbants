// ─────────────────────────────────────────────────────────────
// Le pique-nique : ciel, soleil, couverture vichy, prairie, herbe
// instanciée (par tuiles, pour le culling), fourmilière, semelles
// des gâteaux, cloches de verre.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public static class WorldBuilder
{
    public static ShaderMaterial GrassMat { get; private set; }
    public static readonly Dictionary<int, Node3D> Cloches = new();

    static Shader Sh(string name) => GD.Load<Shader>($"res://Shaders/{name}.gdshader");

    public static void Build(Node3D root, GameState game, int quality)
    {
        BuildEnvironment(root);
        BuildGround(root);
        BuildGrass(root, quality);
        BuildNest(root);
        BuildBoards(root);
        BuildCloches(root, game);
    }

    // ───── Ciel, soleil, post-traitement ─────
    static void BuildEnvironment(Node3D root)
    {
        var sky = new ShaderMaterial { Shader = Sh("sky") };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, ProcessMode = Sky.ProcessModeEnum.Quality, RadianceSize = Sky.RadianceSizeEnum.Size256 }, // ciel fixe : calculé une fois
            // lumière ambiante : ciel bleu (ombres fraîches) + un peu de chaleur
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1.0f,
            AmbientLightSkyContribution = 0.85f,
            AmbientLightColor = new Color("#fff1dc"),
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            TonemapWhite = 6.0f,
            SsaoEnabled = true,
            SsaoRadius = 0.9f,
            SsaoIntensity = 1.6f,
            SsaoPower = 1.3f,
            SsaoDetail = 0.5f,
            SsaoLightAffect = 0.05f,
            SsilEnabled = true,
            SsilRadius = 3f,
            SsilIntensity = 0.45f,
            // halo seulement autour des vraies sources de lumière (lanternes, four, fève)
            GlowEnabled = true,
            GlowIntensity = 0.25f,
            GlowStrength = 0.8f,
            GlowBloom = 0.0f,
            GlowHdrThreshold = 2.2f,
            GlowHdrScale = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogLightColor = new Color("#d6e6f2"),
            FogDensity = 0.00035f,
            FogAerialPerspective = 0.25f,
            FogSkyAffect = 0.0f,
            FogSunScatter = 0.08f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.12f,
            AdjustmentContrast = 1.04f,
            AdjustmentBrightness = 1.0f,
        };
        root.AddChild(new WorldEnvironment { Environment = env, Name = "Env" });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = new Color(1.0f, 0.93f, 0.8f),
            LightEnergy = 1.55f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 200,
            DirectionalShadowBlendSplits = true,
            LightAngularDistance = 0.6f,
            ShadowBias = 0.03f,
            ShadowNormalBias = 2.0f,
            RotationDegrees = new Vector3(-44, -52, 0),
        };
        root.AddChild(sun);
    }

    // ───── Couverture et prairie ─────
    static void BuildGround(Node3D root)
    {
        var meadow = new MeshInstance3D
        {
            Name = "Meadow",
            Mesh = new PlaneMesh { Size = new Vector2(3000, 3000) },
            MaterialOverride = new ShaderMaterial { Shader = Sh("meadow") },
            Position = new Vector3(0, -0.03f, -140),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(meadow);

        float w = BlanketX1 - BlanketX0, d = BlanketZ1 - BlanketZ0;
        var blanket = new MeshInstance3D
        {
            Name = "Blanket",
            Mesh = new PlaneMesh { Size = new Vector2(w, d) },
            MaterialOverride = new ShaderMaterial { Shader = Sh("blanket") },
            Position = new Vector3((BlanketX0 + BlanketX1) / 2, 0.0f, (BlanketZ0 + BlanketZ1) / 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(blanket);
        // ourlet : un bourrelet de tissu tout autour (il descend dans le sol)
        // ourlet roulé (même tissu vichy) et franges
        var hemMat = new ShaderMaterial { Shader = Sh("blanket") };
        var fringeMat = new StandardMaterial3D { AlbedoColor = new Color("#e8dcc6"), Roughness = 0.95f, BacklightEnabled = true, Backlight = new Color(0.4f, 0.35f, 0.3f) };
        var corners = new[] { new Vector2(BlanketX0, BlanketZ0), new Vector2(BlanketX1, BlanketZ0), new Vector2(BlanketX1, BlanketZ1), new Vector2(BlanketX0, BlanketZ1) };
        var fr = new List<Transform3D>();
        var rng = new Random(5);
        for (int e = 0; e < 4; e++)
        {
            var a = corners[e]; var b = corners[(e + 1) % 4];
            float len = (b - a).Length();
            var mid = (a + b) / 2;
            var dir = (b - a) / len;
            var outN = new Vector2(dir.Y, -dir.X);
            var hem = new MeshInstance3D
            {
                // ourlet plat : les tapis roulants passent par-dessus sans le traverser
                Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = len + 0.64f, RadialSegments = 16 },
                MaterialOverride = hemMat,
                Position = new Vector3(mid.X, 0.0f, mid.Y),
                Scale = new Vector3(0.3f, 1, 1),
                Rotation = new Vector3(MathF.PI / 2, 0, 0),
            };
            hem.Rotation = new Vector3(0, -MathF.Atan2(dir.Y, dir.X), MathF.PI / 2);
            root.AddChild(hem);
            // franges : des pompons de 3 fils qui sortent de l'ourlet, couchés vers l'extérieur
            for (float t = 0.4f; t < len - 0.2f; t += 0.55f)
            {
                var rp = a + dir * t + outN * 0.25f;
                float yaw = MathF.Atan2(outN.X, outN.Y);
                for (int k = 0; k < 3; k++)
                {
                    float spread = (k - 1) * 0.12f + ((float)rng.NextDouble() - 0.5f) * 0.08f;
                    float len2 = 0.28f + 0.14f * (float)rng.NextDouble();
                    // cylindre vertical (axe Y) couché le long de outN, légèrement incliné vers le sol
                    var basis = new Basis(Vector3.Up, yaw + spread) * new Basis(Vector3.Right, MathF.PI / 2 + 0.16f);
                    var off = new Vector3(outN.X, 0, outN.Y) * (len2 * 0.5f);
                    fr.Add(new Transform3D(basis * Basis.FromScale(new Vector3(1, len2, 1)), new Vector3(rp.X, 0.06f, rp.Y) + off + new Vector3(dir.X, 0, dir.Y) * spread * 0.3f));
                }
            }
        }
        var fmm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.018f, Height = 1f, RadialSegments = 6, Rings = 1, Material = fringeMat }, InstanceCount = fr.Count };
        for (int i = 0; i < fr.Count; i++) fmm.SetInstanceTransform(i, fr[i]);
        root.AddChild(new MultiMeshInstance3D { Multimesh = fmm });
    }

    // ───── Herbe : brins instanciés, par tuiles ─────
    static ArrayMesh BladeMesh()
    {
        const int seg = 4;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
        for (int s = 0; s <= seg; s++)
        {
            float t = s / (float)seg;
            float half = 0.5f * (1 - t) * (1 - 0.15f * t) + 0.01f;
            float bend = 0.18f * t * t;
            v.Add(new Vector3(-half, t, bend)); v.Add(new Vector3(half, t, bend));
            n.Add(new Vector3(0, 0.3f, -1).Normalized()); n.Add(new Vector3(0, 0.3f, -1).Normalized());
            uv.Add(new Vector2(0, t)); uv.Add(new Vector2(1, t));
        }
        for (int s = 0; s < seg; s++)
        {
            int a = s * 2;
            idx.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = n.ToArray();
        arr[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return m;
    }

    // changer de qualité en jeu : l'herbe est reconstruite (densité, ombres)
    public static void RebuildGrass(Node3D root, int quality)
    {
        var old = root.GetNodeOrNull("Grass");
        if (old != null) { root.RemoveChild(old); old.QueueFree(); }
        BuildGrass(root, quality);
    }
    public static int GrassQuality = -1;

    static void BuildGrass(Node3D root, int quality)
    {
        GrassQuality = quality;
        var rng = new Random(1234);
        var blade = BladeMesh();
        GrassMat ??= new ShaderMaterial { Shader = Sh("grass") };
        blade.SurfaceSetMaterial(0, GrassMat);
        const float band = 112, tile = 64;
        float x0 = BlanketX0 - band, x1 = BlanketX1 + band, z0 = BlanketZ0 - band, z1 = BlanketZ1 + band + 20;
        var tiles = new Dictionary<(int, int), List<(Transform3D, Color)>>();
        void Add(float x, float z, float h, float w)
        {
            float yaw = (float)(rng.NextDouble() * Math.Tau);
            float lean = (float)(rng.NextDouble() - 0.5) * 0.35f;
            var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, lean);
            basis = basis.Scaled(new Vector3(w, h, w));
            float tint = 0.85f + 0.3f * (float)rng.NextDouble();
            float warm = (float)rng.NextDouble();
            var col = new Color(tint * (0.88f + 0.3f * warm), tint * (1.0f + 0.04f * warm), tint * (1.05f - 0.25f * warm));
            var key = ((int)MathF.Floor(x / tile), (int)MathF.Floor(z / tile));
            if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new();
            list.Add((new Transform3D(basis, new Vector3(x, -0.05f, z)), col));
        }
        // la bande d'herbe autour de la couverture (des brins de plus en plus hauts)
        // densité selon la qualité : rapide 30 % · belle 65 % · sublime 100 %
        float dens = quality >= 2 ? 1f : quality == 1 ? 0.65f : 0.3f;
        int tall = (int)(90000 * dens), tries = 0, placed = 0;
        while (placed < tall && tries++ < tall * 30)
        {
            float x = x0 + (float)rng.NextDouble() * (x1 - x0), z = z0 + (float)rng.NextDouble() * (z1 - z0);
            float h = Terrain.GrassHeightAt(x, z);
            if (h <= 0 || Terrain.InClearing(x, z)) continue;
            if (-Terrain.EdgeDist(x, z) > band) continue;
            // les grands brins sont larges : on en met moins au m² (densité continue avec la clairière)
            if (rng.NextDouble() > Math.Clamp(Math.Pow(3.0 / h, 1.5), 0.015, 1)) continue;
            h *= 0.75f + 0.5f * (float)rng.NextDouble();
            Add(x, z, h, (0.09f + h * 0.05f) * (0.8f + 0.4f * (float)rng.NextDouble()));
            placed++;
        }
        // la clairière du nid : herbe rase, à hauteur de fourmi, très dense
        int shortN = (int)(100000 * dens);
        tries = 0; placed = 0;
        while (placed < shortN && tries++ < shortN * 40)
        {
            float x = -62 + (float)rng.NextDouble() * 124, z = BlanketZ1 - 1 + (float)rng.NextDouble() * 32;
            if (!Terrain.InClearing(x, z) || Terrain.EdgeDist(x, z) > -0.4f) continue;
            float r = new Vector2(x - Nest.X, z - Nest.Y).Length();
            if (r < NestKeepR - 0.4f) continue; // pas sur la fourmilière
            float h = Terrain.GrassHeightAt(x, z) * (0.7f + 0.6f * (float)rng.NextDouble());
            Add(x, z, h, (0.09f + h * 0.05f) * (0.8f + 0.4f * (float)rng.NextDouble()));
            placed++;
        }
        var grass = new Node3D { Name = "Grass" };
        root.AddChild(grass);
        foreach (var (_, list) in tiles)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = blade,
                InstanceCount = list.Count,
            };
            for (int i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, list[i].Item1); mm.SetInstanceColor(i, list[i].Item2); }
            // ombres de l'herbe : seulement en « sublime » (des centaines de milliers de brins
            // redessinés dans chaque tranche d'ombre du soleil : le plus gros coût du rendu)
            grass.AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = quality >= 2 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    // ───── Fourmilière ─────
    static void BuildNest(Node3D root)
    {
        // dôme de terre modélisé dans Blender (couleurs par sommet) et son décor
        var soil = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = false, Roughness = 0.95f };
        var dome = ModelLib.Get("nest", "nest_dome");
        if (dome != null)
        {
            var m = (Mesh)dome.Duplicate();
            for (int i = 0; i < m.GetSurfaceCount(); i++) m.SurfaceSetMaterial(i, soil);
            root.AddChild(new MeshInstance3D { Name = "Nest", Mesh = m, Position = new Vector3(Nest.X, 0, Nest.Y) });
        }
        var deco = ModelLib.Get("nest", "nest_deco");
        if (deco != null) root.AddChild(new MeshInstance3D { Mesh = deco, Position = new Vector3(Nest.X, 0, Nest.Y) });
        // rayon doré visible de loin
        var beam = new ShaderMaterial { Shader = Sh("beam") };
        beam.SetShaderParameter("height", 40f);
        beam.SetShaderParameter("intensity", 0.45f);
        root.AddChild(new MeshInstance3D
        {
            Name = "NestBeam",
            Mesh = new CylinderMesh { TopRadius = 1.6f, BottomRadius = 0.9f, Height = 40, RadialSegments = 24, CapTop = false, CapBottom = false },
            MaterialOverride = beam,
            Position = new Vector3(Nest.X, 20 + 1.6f, Nest.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var lamp = new OmniLight3D
        {
            LightColor = new Color(1f, 0.78f, 0.45f), LightEnergy = 1.2f, OmniRange = 9, Position = new Vector3(Nest.X, 3.2f, Nest.Y),
            ShadowEnabled = false,
        };
        root.AddChild(lamp);
    }

    // ───── Assiettes de porcelaine sous les gâteaux ─────
    static readonly string[] PlateBand = { "#2f63b8", "#b8862e", "#d0607a", "#3f8f62", "#d9a531", "#6a56b8" };

    static void BuildBoards(Node3D root)
    {
        var prop = GD.Load<Shader>("res://Shaders/prop.gdshader");
        ShaderMaterial Porcelain(Color c, float rough, float metal = 0)
        {
            var m = new ShaderMaterial { Shader = prop };
            m.SetShaderParameter("kind", 3); m.SetShaderParameter("albedo", c);
            m.SetShaderParameter("roughness", rough); m.SetShaderParameter("metallic", metal);
            m.SetShaderParameter("scale", 3f); m.SetShaderParameter("bump", 0.4f);
            return m;
        }
        var white = Porcelain(new Color("#fbf7ee"), 0.12f);
        var gold = Porcelain(new Color("#e2b44c"), 0.25f, 0.85f);
        var dough = new ShaderMaterial { Shader = Sh("crumbfloor") };
        for (int k = 0; k < CakePos.Length; k++)
        {
            var p = CakePos[k];
            var th = Themes[k % Themes.Length];
            var mesh = PlateMesh(white, Porcelain(new Color(PlateBand[k % PlateBand.Length]), 0.15f), gold);
            root.AddChild(new MeshInstance3D { Name = $"Plate{k + 1}", Mesh = mesh, Position = new Vector3(p.X, 0, p.Y) });
            // la semelle de pâte sombre : le sol des galeries
            root.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = CakeR + 0.2f, BottomRadius = CakeR + 0.2f, Height = 0.02f, RadialSegments = 96 },
                MaterialOverride = FloorMat(dough, th),
                Position = new Vector3(p.X, Terrain.BoardH + 0.005f, p.Y),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    static Material FloorMat(ShaderMaterial m, CakeTheme th)
    {
        var f = (ShaderMaterial)m.Duplicate();
        f.SetShaderParameter("base", th.Crust.Darkened(0.35f));
        f.SetShaderParameter("crumb", th.Fill.Lerp(th.Crust, 0.4f));
        return f;
    }

    // assiette tournée : profil Terrain.PlateTop, un filet coloré dans le marli et un filet d'or sur la lèvre
    static ArrayMesh PlateMesh(Material white, Material band, Material gold)
    {
        const int N = 192;
        var prof = Terrain.PlateTop;
        // normales du profil (perpendiculaires à la tangente, vers le haut/l'extérieur)
        var nrm = new Vector2[prof.Length];
        for (int i = 0; i < prof.Length; i++)
        {
            var a = prof[Math.Max(0, i - 1)]; var b = prof[Math.Min(prof.Length - 1, i + 1)];
            var t = new Vector2(b.r - a.r, b.y - a.y).Normalized();
            nrm[i] = new Vector2(-t.Y, t.X);
            if (nrm[i].Y < 0 && i < prof.Length - 3) nrm[i] = -nrm[i];
        }
        float bandR0 = Terrain.RingOf(1.75f), bandR1 = Terrain.RingOf(2.15f), goldR0 = Terrain.RingOf(3.4f), goldR1 = Terrain.RingOf(3.7f);
        var tools = new[] { new SurfaceTool(), new SurfaceTool(), new SurfaceTool() };
        foreach (var st in tools) st.Begin(Mesh.PrimitiveType.Triangles);
        // on subdivise le profil pour placer les filets exactement
        var rs = new List<(float r, float y, Vector2 n)>();
        for (int i = 0; i < prof.Length - 1; i++)
        {
            int sub = 6;
            for (int s = 0; s < sub; s++)
            {
                float t = s / (float)sub;
                rs.Add((Mathf.Lerp(prof[i].r, prof[i + 1].r, t), Mathf.Lerp(prof[i].y, prof[i + 1].y, t), nrm[i].Lerp(nrm[i + 1], t).Normalized()));
            }
        }
        rs.Add((prof[^1].r, prof[^1].y, nrm[^1]));
        for (int j = 0; j < rs.Count - 1; j++)
        {
            float rm = (rs[j].r + rs[j + 1].r) / 2;
            int surf = rm > bandR0 && rm < bandR1 ? 1 : rm > goldR0 && rm < goldR1 ? 2 : 0;
            var st = tools[surf];
            for (int i = 0; i < N; i++)
            {
                float a0 = i * MathF.Tau / N, a1 = (i + 1) * MathF.Tau / N;
                Vector3 V(int jj, float a) => new(MathF.Cos(a) * rs[jj].r, rs[jj].y, MathF.Sin(a) * rs[jj].r);
                Vector3 Nn(int jj, float a) => new Vector3(MathF.Cos(a) * rs[jj].n.X, rs[jj].n.Y, MathF.Sin(a) * rs[jj].n.X).Normalized();
                // ordre horaire vu de dessus/extérieur (faces avant de Godot)
                st.SetNormal(Nn(j, a0)); st.AddVertex(V(j, a0));
                st.SetNormal(Nn(j + 1, a0)); st.AddVertex(V(j + 1, a0));
                st.SetNormal(Nn(j + 1, a1)); st.AddVertex(V(j + 1, a1));
                st.SetNormal(Nn(j, a0)); st.AddVertex(V(j, a0));
                st.SetNormal(Nn(j + 1, a1)); st.AddVertex(V(j + 1, a1));
                st.SetNormal(Nn(j, a1)); st.AddVertex(V(j, a1));
            }
        }
        var mesh = new ArrayMesh();
        var mats = new[] { white, band, gold };
        for (int k = 0; k < 3; k++)
        {
            tools[k].SetMaterial(mats[k]);
            tools[k].Commit(mesh);
        }
        return mesh;
    }


    // ───── Cloches de verre sur les gâteaux verrouillés ─────
    static void BuildCloches(Node3D root, GameState game)
    {
        var glass = new ShaderMaterial { Shader = Sh("glass") };
        var gold = new StandardMaterial3D { AlbedoColor = new Color("#e8bd52"), Metallic = 0.9f, Roughness = 0.22f };
        foreach (var K in game.Cakes)
        {
            if (K.Unlocked) continue;
            var c = new Node3D { Name = $"Cloche{K.N}", Position = new Vector3(K.Cx, 0, K.Cz) };
            c.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = ClocheR, Height = ClocheR * 0.9f, IsHemisphere = true, RadialSegments = 96, Rings = 32 },
                MaterialOverride = glass,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            c.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 2.6f, Height = 4.2f },
                MaterialOverride = gold,
                Position = new Vector3(0, ClocheR * 0.45f + 1.6f, 0),
            });
            c.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = ClocheR - 0.4f, OuterRadius = ClocheR + 0.5f, Rings = 96, RingSegments = 12 },
                MaterialOverride = gold,
                Position = new Vector3(0, 0.2f, 0),
            });
            root.AddChild(c);
            Cloches[K.Index] = c;
        }
    }
}
