// ─────────────────────────────────────────────────────────────
// Collisions EXACTES des objets du décor : chaque objet a pour
// forme de collision son propre maillage (triangles du modèle
// Blender). La fourmi du joueur et la caméra les interrogent avec
// une petite capsule : on passe sous le bol, on touche son pied, on
// remonte le long de sa courbe, on se glisse sous l'anse du panier…
// (Les ouvrières et la construction gardent la carte de hauteur.)
// ─────────────────────────────────────────────────────────────
using System;
using Godot;

namespace Crumbants.Render;

public static class PropPhysics
{
    public const uint Layer = 1u << 4;
    static Rid _space;
    static PhysicsDirectSpaceState3D _state;
    static PhysicsShapeQueryParameters3D _qAnt, _qCam;
    static Node3D _root;

    public static bool Ready => _root != null;

    public static void Add(Node3D parent, Mesh mesh, Transform3D xf)
    {
        if (mesh == null) return;
        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            _root = new Node3D { Name = "PropCollisions" };
            parent.AddChild(_root);
        }
        var shape = mesh.CreateTrimeshShape();
        if (shape is ConcavePolygonShape3D cp) cp.BackfaceCollision = true; // rubans, feuilles : les deux faces sont solides
        var body = new StaticBody3D { CollisionLayer = Layer, CollisionMask = 0, Transform = xf };
        body.AddChild(new CollisionShape3D { Shape = shape });
        _root.AddChild(body);
    }

    static PhysicsDirectSpaceState3D State()
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root) || !_root.IsInsideTree()) return null;
        _state ??= _root.GetWorld3D().DirectSpaceState;
        return _state;
    }

    // capsule verticale du corps de la fourmi, pieds en p
    public const float AntCapR = 0.25f, AntCapH = 0.56f;
    public static bool HitsAnt(Vector3 feet)
    {
        var st = State();
        if (st == null) return false;
        _qAnt ??= new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = AntCapR, Height = AntCapH },
            CollisionMask = Layer, CollideWithAreas = false, CollideWithBodies = true, Margin = 0.0f,
        };
        _qAnt.Transform = new Transform3D(Basis.Identity, feet + new Vector3(0, 0.02f + AntCapH / 2, 0));
        return st.IntersectShape(_qAnt, 1).Count > 0;
    }

    public static bool HitsSphere(Vector3 c, float r)
    {
        var st = State();
        if (st == null) return false;
        _qCam ??= new PhysicsShapeQueryParameters3D { Shape = new SphereShape3D { Radius = r }, CollisionMask = Layer, Margin = 0.0f };
        ((SphereShape3D)_qCam.Shape).Radius = r;
        _qCam.Transform = new Transform3D(Basis.Identity, c);
        return st.IntersectShape(_qCam, 1).Count > 0;
    }

    // capsule verticale (bas à pied.Y, hauteur totale h), formes mises en cache par taille
    static readonly System.Collections.Generic.Dictionary<(float, float), PhysicsShapeQueryParameters3D> _caps = new();
    public static bool HitsCapsule(Vector3 bottom, float r, float h)
    {
        var st = State();
        if (st == null) return false;
        if (!_caps.TryGetValue((r, h), out var q))
            _caps[(r, h)] = q = new PhysicsShapeQueryParameters3D { Shape = new CapsuleShape3D { Radius = r, Height = h }, CollisionMask = Layer, Margin = 0.0f };
        q.Transform = new Transform3D(Basis.Identity, bottom + new Vector3(0, h / 2, 0));
        return st.IntersectShape(q, 1).Count > 0;
    }

    static PhysicsRayQueryParameters3D _ray;
    static PhysicsRayQueryParameters3D RayQ(Vector3 from, Vector3 to)
    {
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = Layer, HitBackFaces = true };
        _ray.From = from; _ray.To = to;
        return _ray;
    }

    // premier contact le long d'un segment : (point, normale) ; null si rien
    public static (Vector3 p, Vector3 n)? RayHit(Vector3 from, Vector3 to)
    {
        var st = State();
        if (st == null) return null;
        var q = RayQ(from, to);
        var r = st.IntersectRay(q);
        if (r.Count == 0) return null;
        var n = (Vector3)r["normal"];
        if (n.Dot(to - from) > 0) n = -n; // face arrière : la normale regarde vers nous
        return ((Vector3)r["position"], n);
    }

    // premier contact le long d'un rayon (pour la caméra) ; -1 si rien
    public static float Ray(Vector3 from, Vector3 to)
    {
        var st = State();
        if (st == null) return -1;
        var r = st.IntersectRay(RayQ(from, to));
        if (r.Count == 0) return -1;
        return from.DistanceTo((Vector3)r["position"]) / MathF.Max(1e-5f, from.DistanceTo(to));
    }
}
