// ─────────────────────────────────────────────────────────────
// Squelette de fourmi partagé (joueuse, ouvrières, personnel) :
// pièces modélisées dans Blender (Models/ant.glb), pattes en
// cinématique inverse (2 os) avec marche en trépied : les pieds
// restent plantés pendant l'appui. Rendu en instances (MultiMesh),
// une couche par type de pièce : des centaines de fourmis, ~12 appels.
// Le modèle regarde vers +Z.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;

namespace Crumbants.Render;

public struct AntPose
{
    public Vector3 Pos;
    public float Yaw, Pitch, Roll, Scale;
    public float Gait;       // phase de marche (radians), avance avec la distance
    public float Walk;       // 0 = à l'arrêt … 1 = en marche
    public float Bite;       // 0..1 (tête qui plonge, mandibules qui claquent)
    public float Load;       // 0..1 : charge de miettes dans les mandibules
    public float Time;       // horloge propre (antennes, respiration)
    public float Stun;       // 0..1 : sonnée
    public float Hang;       // 1 = suspendue tête en bas par les pattes arrière (grue)
    public Color Color;
    public Color LoadColor;  // couleur des miettes portées (celle de leur gâteau) ; transparente = défaut
    public bool Hidden;
    public bool Helmet;      // casque de chantier (opératrices des machines)
    public bool UseOrient;   // orientation libre (posée sur une pente, une paroi, un surplomb)
    public Basis Orient;     // colonnes : droite, haut (normale de la surface), avant
}

public static class AntRig
{
    // longueurs des os (modèle Blender)
    public const float L1 = 0.27f, L2 = 0.34f;
    public const float Stride = 0.56f; // distance parcourue par cycle complet de marche

    public const int PartThorax = 0, PartGaster = 1, PartHead = 2, PartMandL = 3, PartMandR = 4,
        PartScape = 5, PartFlag = 6, PartFemur = 7, PartTibia = 8, PartLoad = 9, PartTypes = 10;
    // nombre d'instances de chaque pièce par fourmi
    public static readonly int[] PerAnt = { 1, 1, 1, 1, 1, 2, 2, 6, 6, 1 };
    public static readonly string[] Names = { "thorax", "gaster", "head", "mandible_L", "mandible_R", "antenna_scape", "antenna_flag", "femur", "tibia", "crumb_load" };

    // hanches (repère de la fourmi, avant +Z) et pieds au repos (au sol)
    static readonly Vector3[] Hip =
    {
        new(-0.07f, 0.29f, 0.17f), new(0.07f, 0.29f, 0.17f),
        new(-0.08f, 0.28f, 0.09f), new(0.08f, 0.28f, 0.09f),
        new(-0.075f, 0.28f, 0.02f), new(0.075f, 0.28f, 0.02f),
    };
    static readonly Vector3[] Foot =
    {
        new(-0.36f, 0, 0.46f), new(0.36f, 0, 0.46f),
        new(-0.47f, 0, 0.08f), new(0.47f, 0, 0.08f),
        new(-0.40f, 0, -0.34f), new(0.40f, 0, -0.34f),
    };
    // trépied : avant G + milieu D + arrière G ensemble, puis les trois autres
    static readonly float[] LegPhase = { 0, MathF.PI, MathF.PI, 0, 0, MathF.PI };

    public static Vector3 MandHinge(float side) => new(side * 0.07f, -0.07f, 0.228f);
    public static float MandOpen(float bite, float load) { float b = MathF.Sin(bite * MathF.PI); return 0.42f * (1 - b) + 0.08f + (load > 0 ? 0.15f : 0); }

    static Basis AlongY(Vector3 dirY, Vector3 hint)
    {
        var y = dirY.Normalized();
        var x = hint.Cross(y);
        if (x.LengthSquared() < 1e-6f) x = Vector3.Right.Cross(y);
        x = x.Normalized();
        var z = x.Cross(y).Normalized();
        return new Basis(x, y, z);
    }

    // Calcule les transformations monde des 21 pièces d'une fourmi, rangées par type.
    // writer(type, indexDansLeType, transform)
    public static void Compute(in AntPose p, Action<int, int, Transform3D> writer)
    {
        float s = p.Scale <= 0 ? 1 : p.Scale;
        var orient = p.UseOrient ? p.Orient
            : new Basis(Vector3.Up, p.Yaw) * new Basis(Vector3.Right, p.Pitch) * new Basis(Vector3.Forward, p.Roll);
        var root = new Transform3D(orient.Scaled(Vector3.One * s), p.Pos);
        float t = p.Time, w = p.Walk;
        float bob = MathF.Abs(MathF.Sin(p.Gait)) * 0.018f * w + MathF.Sin(t * 2.2f) * 0.005f * (1 - w);
        float sway = MathF.Sin(p.Gait * 0.5f) * 0.035f * w + p.Stun * MathF.Sin(t * 18) * 0.12f;
        // corps (thorax)
        var body = new Transform3D(new Basis(Vector3.Forward, sway), new Vector3(0, 0.31f + bob, 0.06f));
        writer(PartThorax, 0, root * body);
        // gastre : relevé, balancement
        var gas = body * new Transform3D(new Basis(Vector3.Up, MathF.Sin(p.Gait) * 0.08f * w) * new Basis(Vector3.Right, 0.22f + MathF.Sin(t * 2.2f) * 0.035f), new Vector3(0, -0.01f, -0.14f));
        writer(PartGaster, 0, root * gas);
        // tête : plonge à la morsure
        float b = MathF.Sin(p.Bite * MathF.PI);
        var head = body * new Transform3D(new Basis(Vector3.Right, b * 0.5f - 0.05f) * new Basis(Vector3.Up, MathF.Sin(t * 0.9f) * 0.06f * (1 - w)), new Vector3(0, 0.05f, 0.13f));
        writer(PartHead, 0, root * head);
        // mandibules : s'ouvrent puis claquent
        float open = MandOpen(p.Bite, p.Load);
        // charnières articulées SUR la tête (sa surface avant est en z ≈ 0,235 à cette hauteur)
        writer(PartMandL, 0, root * head * new Transform3D(new Basis(Vector3.Up, -open), MandHinge(-1)));
        writer(PartMandR, 0, root * head * new Transform3D(new Basis(Vector3.Up, open), MandHinge(1)));
        // charge de miettes
        float ls = p.Load > 0.001f ? 0.55f + 0.75f * Mathf.Clamp(p.Load, 0, 1) : 0.0001f;
        writer(PartLoad, 0, root * head * new Transform3D(Basis.Identity.Scaled(Vector3.One * ls), new Vector3(0, -0.08f, 0.36f)));
        // antennes coudées, toujours en mouvement
        for (int i = 0; i < 2; i++)
        {
            float sd = i == 0 ? -1 : 1;
            float wig = MathF.Sin(t * 3.1f + i * 1.7f), wig2 = MathF.Sin(t * 2.3f + i);
            var baseP = new Vector3(sd * 0.065f, 0.13f, 0.17f);
            var dir1 = new Vector3(sd * 0.45f, 0.8f, 0.35f + 0.1f * wig).Normalized();
            var scape = new Transform3D(AlongY(dir1, Vector3.Forward), baseP);
            writer(PartScape, i, root * head * scape);
            var tip = baseP + dir1 * 0.17f;
            var dir2 = new Vector3(sd * (0.3f + 0.12f * wig2), 0.1f + 0.2f * wig, 0.95f).Normalized();
            writer(PartFlag, i, root * head * new Transform3D(AlongY(dir2, Vector3.Up), tip));
        }
        // pattes (IK à 2 os) : les pieds sont dans le repère racine (au sol)
        var bodyOff = body.Origin - new Vector3(0, 0.31f, 0.06f);
        for (int l = 0; l < 6; l++)
        {
            float sd = (l & 1) == 0 ? -1 : 1;
            var hip = Hip[l] + bodyOff;
            float u = ((p.Gait + LegPhase[l]) / MathF.Tau) % 1f; if (u < 0) u += 1;
            float fz, lift;
            float A = Stride / 4;
            if (u < 0.5f) { fz = A - 4 * A * u; lift = 0; }                    // appui : le pied recule
            else { float v = (u - 0.5f) * 2; fz = -A + 2 * A * v; lift = MathF.Sin(v * MathF.PI) * 0.13f; } // envol
            var foot = Foot[l] + new Vector3(0, lift * w, fz * w);
            foot.Y += p.Stun * MathF.Abs(MathF.Sin(t * 14 + l)) * 0.1f;
            if (p.Hang > 0)
            {
                // suspendue : les pattes arrière agrippent l'anneau du harnais (derrière le gastre),
                // les autres pendent vers le gâteau et gigotent
                float wg = MathF.Sin(t * 6 + l * 1.3f);
                Vector3 hf = l >= 4 ? new Vector3(sd * 0.1f, 0.47f, -0.55f)
                           : l >= 2 ? new Vector3(sd * (0.36f + 0.04f * wg), 0.12f, 0.3f + 0.05f * wg)
                                    : new Vector3(sd * (0.2f + 0.05f * wg), 0.16f, 0.58f + 0.05f * wg);
                foot = foot.Lerp(hf, p.Hang);
            }
            var d = foot - hip;
            float dl = Mathf.Clamp(d.Length(), 0.05f, L1 + L2 - 0.002f);
            var dir = d.Normalized();
            float a = (L1 * L1 + dl * dl - L2 * L2) / (2 * dl);
            float h = MathF.Sqrt(MathF.Max(0, L1 * L1 - a * a));
            var pole = new Vector3(sd * 0.6f, 1.2f, 0).Normalized();
            var perp = (pole - dir * pole.Dot(dir)).Normalized();
            var knee = hip + dir * a + perp * h;
            var footR = hip + dir * dl;
            writer(PartFemur, l, root * new Transform3D(AlongY(knee - hip, Vector3.Forward), hip));
            writer(PartTibia, l, root * new Transform3D(AlongY(footR - knee, Vector3.Forward), knee));
        }
    }
}

// Rendu en instances d'un groupe de fourmis (même matériau, couleurs par instance).
public partial class AntSwarm : Node3D
{
    static Dictionary<string, Mesh> _meshes;
    readonly MultiMesh[] _mm = new MultiMesh[AntRig.PartTypes];
    float[][] _buf = new float[AntRig.PartTypes][];
    // casques de chantier (Models/buildings.glb « hardhat », modélisé dans le repère de la tête)
    MultiMesh _hats; float[] _hatBuf; int _hatCount;
    public int Capacity { get; private set; }
    int _count;

    static void LoadMeshes()
    {
        if (_meshes != null) return;
        _meshes = new();
        var scene = GD.Load<PackedScene>("res://Models/ant.glb").Instantiate();
        var antSh = GD.Load<Shader>("res://Shaders/ant.gdshader");
        var shell = new ShaderMaterial { Shader = antSh };
        var dark = new ShaderMaterial { Shader = antSh };
        dark.SetShaderParameter("darken", 0.55f);
        dark.SetShaderParameter("rough", 0.4f);
        var eye = new StandardMaterial3D { AlbedoColor = new Color("#0c0b10"), Roughness = 0.05f, ClearcoatEnabled = true, Clearcoat = 1, ClearcoatRoughness = 0.02f };
        var shine = new StandardMaterial3D { AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        // la charge prend la couleur d'instance : celle des miettes de son gâteau
        var crumb = new StandardMaterial3D { AlbedoColor = Colors.White, VertexColorUseAsAlbedo = true, Roughness = 0.85f };
        void Walk(Node n)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var m = (Mesh)mi.Mesh.Duplicate();
                for (int i = 0; i < m.GetSurfaceCount(); i++)
                {
                    var src = m.SurfaceGetMaterial(i) ?? mi.GetActiveMaterial(i);
                    string nm = src?.ResourceName ?? "";
                    Material mat = nm.StartsWith("Eye") ? eye : nm.StartsWith("Shine") ? shine : nm.StartsWith("Crumb") ? crumb : nm.StartsWith("ShellDark") ? dark : shell;
                    m.SurfaceSetMaterial(i, mat);
                }
                _meshes[mi.Name] = m;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(scene);
        scene.QueueFree();
    }

    public static AntSwarm Create(int capacity, bool shadows = true, bool helmets = false)
    {
        LoadMeshes();
        var s = new AntSwarm { Name = "AntSwarm", Capacity = capacity };
        for (int t = 0; t < AntRig.PartTypes; t++)
        {
            _meshes.TryGetValue(AntRig.Names[t], out var mesh);
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = capacity * AntRig.PerAnt[t],
                VisibleInstanceCount = 0,
            };
            s._mm[t] = mm;
            s._buf[t] = new float[mm.InstanceCount * 16];
            var mi = new MultiMeshInstance3D
            {
                Multimesh = mm,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                // le MultiMesh couvre tout le pique-nique : pas de culling par boîte
                CustomAabb = new Aabb(new Vector3(-400, -50, -450), new Vector3(800, 300, 900)),
            };
            s.AddChild(mi);
        }
        if (helmets && ModelLib.Get("buildings", "hardhat") is { } hat)
        {
            s._hats = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = hat, InstanceCount = capacity, VisibleInstanceCount = 0 };
            s._hatBuf = new float[capacity * 12];
            s.AddChild(new MultiMeshInstance3D
            {
                Multimesh = s._hats,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                CustomAabb = new Aabb(new Vector3(-400, -50, -450), new Vector3(800, 300, 900)),
            });
        }
        return s;
    }

    public static Mesh PartMesh(string name) { LoadMeshes(); return _meshes.TryGetValue(name, out var m) ? m : null; }

    public void Begin() { _count = 0; _hatCount = 0; }

    // surcouche de toutes les pièces (radiographie : silhouette vue à travers les murs) ; null = aucune
    public void SetOverlay(Material m)
    {
        foreach (var c in GetChildren()) if (c is MultiMeshInstance3D mi) mi.MaterialOverlay = m;
    }

    public void Add(in AntPose p)
    {
        if (_count >= Capacity || p.Hidden) return;
        int ant = _count++;
        var col = p.Color.SrgbToLinear();
        var lcol = (p.LoadColor.A > 0 ? p.LoadColor : Cfg.CrumbColor(0)).SrgbToLinear();
        bool hat = p.Helmet && _hats != null;
        var hatBuf = _hatBuf;
        int hatIdx = _hatCount;
        if (hat) _hatCount++;
        AntRig.Compute(p, (type, k, tr) =>
        {
            if (hat && type == AntRig.PartHead)
            {
                int ho = hatIdx * 12;
                var hb = tr.Basis;
                hatBuf[ho] = hb.X.X; hatBuf[ho + 1] = hb.Y.X; hatBuf[ho + 2] = hb.Z.X; hatBuf[ho + 3] = tr.Origin.X;
                hatBuf[ho + 4] = hb.X.Y; hatBuf[ho + 5] = hb.Y.Y; hatBuf[ho + 6] = hb.Z.Y; hatBuf[ho + 7] = tr.Origin.Y;
                hatBuf[ho + 8] = hb.X.Z; hatBuf[ho + 9] = hb.Y.Z; hatBuf[ho + 10] = hb.Z.Z; hatBuf[ho + 11] = tr.Origin.Z;
            }
            var c = type == AntRig.PartLoad ? lcol : col;
            int idx = ant * AntRig.PerAnt[type] + k;
            var b = _buf[type];
            int o = idx * 16;
            var bs = tr.Basis;
            b[o] = bs.X.X; b[o + 1] = bs.Y.X; b[o + 2] = bs.Z.X; b[o + 3] = tr.Origin.X;
            b[o + 4] = bs.X.Y; b[o + 5] = bs.Y.Y; b[o + 6] = bs.Z.Y; b[o + 7] = tr.Origin.Y;
            b[o + 8] = bs.X.Z; b[o + 9] = bs.Y.Z; b[o + 10] = bs.Z.Z; b[o + 11] = tr.Origin.Z;
            b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = c.A;
        });
    }

    public void End()
    {
        for (int t = 0; t < AntRig.PartTypes; t++)
        {
            var mm = _mm[t];
            mm.Buffer = _buf[t];
            mm.VisibleInstanceCount = _count * AntRig.PerAnt[t];
        }
        if (_hats != null)
        {
            _hats.Buffer = _hatBuf;
            _hats.VisibleInstanceCount = _hatCount;
        }
    }
}
