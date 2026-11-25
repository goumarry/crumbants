// ─────────────────────────────────────────────────────────────
// Relief des objets du décor : une carte de hauteur (vue de dessus)
// calculée à partir des VRAIS maillages Blender posés sur la
// couverture. La fourmi marche dessus, grimpe sur leurs flancs
// comme sur le gâteau, et la caméra ne passe plus au travers.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public static class PropField
{
    public const float Cell = 0.25f;
    const float X0 = BlanketX0 - 12, Z0 = BlanketZ0 - 12, X1 = BlanketX1 + 12, Z1 = BlanketZ1 + 16;
    static readonly int NX = (int)MathF.Ceiling((X1 - X0) / Cell) + 1, NZ = (int)MathF.Ceiling((Z1 - Z0) / Cell) + 1;
    static float[] _h;
    public static bool Ready => _h != null;

    // les objets posés sur d'autres (la fourchette sur la serviette) : hauteur calculée
    public static readonly Dictionary<int, float> Lift = new();

    public static Transform3D XformOf(int i)
    {
        var (name, x, z, rot) = Props.Placed[i];
        float lift = Lift.TryGetValue(i, out var l) ? l : 0;
        return new Transform3D(new Basis(Vector3.Up, rot), new Vector3(x, lift, z));
    }

    public static void Build()
    {
        if (_h != null) return;
        var h = new float[NX * NZ];
        var t0 = Time.GetTicksMsec();
        var resting = new List<int>();
        for (int i = 0; i < Props.Placed.Length; i++)
        {
            if (Props.RestsOnOthers(Props.Placed[i].name)) { resting.Add(i); continue; }
            var mesh = ModelLib.Get("props", Props.Placed[i].name);
            if (mesh != null) Raster(h, mesh, XformOf(i));
        }
        // objets posés : on les descend jusqu'à toucher ce qui est dessous
        foreach (int i in resting)
        {
            var mesh = ModelLib.Get("props", Props.Placed[i].name);
            if (mesh == null) continue;
            var xf = XformOf(i);
            float lift = 0;
            ForEachVertex(mesh, xf, p => lift = MathF.Max(lift, Sample(h, p.X, p.Z) - p.Y));
            Lift[i] = lift;
            Raster(h, mesh, XformOf(i));
        }
        foreach (var (name, pos, rot, scale) in Props.Extra())
        {
            var mesh = ModelLib.Get("props", name);
            if (mesh != null) Raster(h, mesh, new Transform3D(new Basis(Vector3.Up, rot).Scaled(scale), pos));
        }
        for (int i = 0; i < Props.Streamers.Length; i++) Raster(h, Props.StreamerMesh(i), Transform3D.Identity);
        _h = h;
        GD.Print($"[props] relief des objets : {NX}×{NZ} cases en {Time.GetTicksMsec() - t0} ms");
    }

    static void ForEachVertex(Mesh mesh, Transform3D xf, Action<Vector3> f)
    {
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arr = mesh.SurfaceGetArrays(s);
            var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach (var p in v) f(xf * p);
        }
    }

    // rastérisation de dessus : chaque case garde la hauteur max des triangles qui la couvrent
    static void Raster(float[] h, Mesh mesh, Transform3D xf)
    {
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arr = mesh.SurfaceGetArrays(s);
            var vs = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idxV = arr[(int)Mesh.ArrayType.Index];
            int[] idx = idxV.VariantType == Variant.Type.Nil ? null : idxV.AsInt32Array();
            var w = new Vector3[vs.Length];
            for (int i = 0; i < vs.Length; i++) w[i] = xf * vs[i];
            int nt = idx != null ? idx.Length / 3 : vs.Length / 3;
            for (int t = 0; t < nt; t++)
            {
                var a = w[idx != null ? idx[3 * t] : 3 * t];
                var b = w[idx != null ? idx[3 * t + 1] : 3 * t + 1];
                var c = w[idx != null ? idx[3 * t + 2] : 3 * t + 2];
                Tri(h, a, b, c);
            }
        }
    }

    static void Put(float[] h, int i, int k, float y)
    {
        if (i < 0 || k < 0 || i >= NX || k >= NZ) return;
        int id = k * NX + i;
        if (y > h[id]) h[id] = y;
    }

    static void Tri(float[] h, Vector3 a, Vector3 b, Vector3 c)
    {
        float minX = MathF.Min(a.X, MathF.Min(b.X, c.X)), maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float minZ = MathF.Min(a.Z, MathF.Min(b.Z, c.Z)), maxZ = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));
        int i0 = (int)MathF.Floor((minX - X0) / Cell), i1 = (int)MathF.Ceiling((maxX - X0) / Cell);
        int k0 = (int)MathF.Floor((minZ - Z0) / Cell), k1 = (int)MathF.Ceiling((maxZ - Z0) / Cell);
        float d = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
        if (MathF.Abs(d) < 1e-6f)
        {
            // triangle vu par la tranche (paroi verticale) : on marque ses sommets
            foreach (var p in new[] { a, b, c, (a + b) / 2, (b + c) / 2, (a + c) / 2 })
                Put(h, (int)MathF.Round((p.X - X0) / Cell), (int)MathF.Round((p.Z - Z0) / Cell), p.Y);
            return;
        }
        const float eps = -0.02f;
        for (int k = k0; k <= k1; k++)
        for (int i = i0; i <= i1; i++)
        {
            float px = X0 + i * Cell, pz = Z0 + k * Cell;
            float l1 = ((b.X - px) * (c.Z - pz) - (c.X - px) * (b.Z - pz)) / d;
            float l2 = ((c.X - px) * (a.Z - pz) - (a.X - px) * (c.Z - pz)) / d;
            float l3 = 1 - l1 - l2;
            if (l1 < eps || l2 < eps || l3 < eps) continue;
            Put(h, i, k, l1 * a.Y + l2 * b.Y + l3 * c.Y);
        }
    }

    static float Sample(float[] h, float x, float z)
    {
        float fx = (x - X0) / Cell, fz = (z - Z0) / Cell;
        int i = (int)MathF.Floor(fx), k = (int)MathF.Floor(fz);
        if (i < 0 || k < 0 || i >= NX - 1 || k >= NZ - 1) return 0;
        float tx = fx - i, tz = fz - k;
        int id = k * NX + i;
        float a = h[id], b = h[id + 1], c = h[id + NX], e = h[id + NX + 1];
        return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + e * tx) * tz;
    }

    // hauteur du dessus des objets en (x, z) ; 0 hors des objets
    public static float Height(float x, float z) => _h == null ? 0 : Sample(_h, x, z);

    // hauteur max sur un petit disque (corps de la fourmi, emprise d'un bâtiment)
    public static float HeightDisc(float x, float z, float r)
    {
        if (_h == null) return 0;
        float m = Sample(_h, x, z);
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8;
            m = MathF.Max(m, Sample(_h, x + MathF.Cos(a) * r, z + MathF.Sin(a) * r));
        }
        return m;
    }
}
