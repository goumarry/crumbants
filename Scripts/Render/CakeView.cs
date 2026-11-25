// ─────────────────────────────────────────────────────────────
// Affichage d'un gâteau : 25 tranches maillées en surface lisse,
// reconstruites quand un bloc change de palier (4 au plus par image).
// ─────────────────────────────────────────────────────────────
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public partial class CakeView : Node3D
{
    public CakeSim Sim { get; private set; }
    public CakeField Field { get; private set; }
    ShaderMaterial _mat;
    MeshInstance3D[] _chunks;
    // collisions EXACTES : le maillage affiché de chaque tranche sert aussi de forme physique
    StaticBody3D[] _bodies;
    readonly HashSet<int> _dirty = new();

    public static CakeView Create(CakeSim sim, Shader shader)
    {
        var v = new CakeView { Name = $"Cake{sim.N}" };
        v.Sim = sim;
        v.Field = new CakeField(sim);
        v.Position = new Vector3(sim.Cx, 0, sim.Cz);
        v._mat = new ShaderMaterial { Shader = shader };
        var t = sim.Theme;
        v._mat.SetShaderParameter("crust", t.Crust);
        v._mat.SetShaderParameter("glaze", t.Glaze);
        v._mat.SetShaderParameter("fill", t.Fill);
        v._mat.SetShaderParameter("accent", t.Accent);
        v._mat.SetShaderParameter("chunk", t.Chunk);
        v._mat.SetShaderParameter("center", new Vector2(sim.Cx, sim.Cz));
        v._mat.SetShaderParameter("style", (sim.N - 1) % Themes.Length);
        v._chunks = new MeshInstance3D[CakeField.Nch * CakeField.Nch];
        v._bodies = new StaticBody3D[CakeField.Nch * CakeField.Nch];
        return v;
    }

    // Construit toutes les tranches (en parallèle) : au chargement ou après une recharge.
    public void RebuildAll()
    {
        Field.RefreshAllLevels();
        int n = CakeField.Nch * CakeField.Nch;
        var arrays = new ChunkArrays[n];
        Parallel.For(0, n, i => arrays[i] = Field.BuildChunk(i));
        for (int i = 0; i < n; i++) Apply(i, arrays[i]);
        _dirty.Clear();
        Sim.Dirty.Clear();
    }

    void Apply(int n, ChunkArrays a)
    {
        var old = _chunks[n];
        if (a.Idx.Length == 0)
        {
            if (old != null) { old.QueueFree(); _chunks[n] = null; }
            if (_bodies[n] != null) { _bodies[n].QueueFree(); _bodies[n] = null; }
            return;
        }
        // forme physique = mêmes triangles que l'affichage
        var faces = new Vector3[a.Idx.Length];
        for (int i = 0; i < faces.Length; i++) faces[i] = a.Pos[a.Idx[i]];
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces);
        if (_bodies[n] == null)
        {
            _bodies[n] = new StaticBody3D { CollisionLayer = PropPhysics.Layer, CollisionMask = 0 };
            _bodies[n].AddChild(new CollisionShape3D { Shape = shape });
            AddChild(_bodies[n]);
        }
        else _bodies[n].GetChild<CollisionShape3D>(0).Shape = shape;
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = a.Pos;
        arr[(int)Mesh.ArrayType.Normal] = a.Nor;
        arr[(int)Mesh.ArrayType.Color] = a.Col;
        arr[(int)Mesh.ArrayType.Index] = a.Idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        mesh.SurfaceSetMaterial(0, _mat);
        if (old == null)
        {
            old = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
            AddChild(old);
            _chunks[n] = old;
        }
        old.Mesh = mesh;
    }

    // Blocs modifiés par la simulation → tranches à reconstruire
    public void CollectDirty()
    {
        if (Sim.Dirty.Count == 0) return;
        foreach (int b in Sim.Dirty)
        {
            byte l = Field.LevelOf(Sim.Cake[b]);
            if (l == Field.Level[b]) continue;
            Field.Level[b] = l;
            int i = CakeSim.IdxI(b), k = CakeSim.IdxK(b);
            for (int di = -2; di <= 2; di += 2)
            for (int dk = -2; dk <= 2; dk += 2)
            {
                int ii = i + di, kk = k + dk;
                if (ii < -1 || kk < -1 || ii > CW || kk > CW) continue;
                int ci = Mathf.Clamp(Mathf.FloorToInt(ii / (float)CakeField.Chunk), 0, CakeField.Nch - 1);
                int ck = Mathf.Clamp(Mathf.FloorToInt(kk / (float)CakeField.Chunk), 0, CakeField.Nch - 1);
                _dirty.Add(ci + ck * CakeField.Nch);
            }
        }
        Sim.Dirty.Clear();
    }

    public int Flush(int max)
    {
        if (_dirty.Count == 0 || max <= 0) return 0;
        var list = new List<int>();
        foreach (int c in _dirty) { list.Add(c); if (list.Count >= max) break; }
        foreach (int c in list) _dirty.Remove(c);
        var arrays = new ChunkArrays[list.Count];
        Parallel.For(0, list.Count, i => arrays[i] = Field.BuildChunk(list[i]));
        for (int i = 0; i < list.Count; i++) Apply(list[i], arrays[i]);
        return list.Count;
    }
}
