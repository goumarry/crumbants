// ─────────────────────────────────────────────────────────────
// Disposition du pique-nique : portes des gâteaux et chemin de
// fourmis (autoroute) qui relie le nid aux gâteaux débloqués.
// Chaque tronçon part de la porte du gâteau précédent, longe ce
// gâteau, puis file vers le suivant.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Sim;

public static class Layout
{
    public const float RingR = CakeR + 3f;   // anneau de circulation autour d'un gâteau

    static Vector2 C(int k) => new(CakePos[k].X, CakePos[k].Y);

    // Porte du gâteau k : le point de son anneau tourné vers la fourmilière
    public static Vector2 Door(int k)
    {
        if (k == 0) return Nest;
        var d = (Nest - C(k)).Normalized();
        return C(k) + d * RingR;
    }

    // Obstacles au sol pour les trajets sur la couverture : gâteaux (et leur cloche), objets du décor
    static List<(Vector2 c, float r)> _obs;
    static bool _obsProps;
    public static Func<List<(Vector2 c, float r)>> PropObstacles;
    static List<(Vector2 c, float r)> Obstacles()
    {
        if (_obs != null && (_obsProps || PropObstacles == null)) return _obs;
        _obsProps = PropObstacles != null;
        _obs = new();
        for (int k = 0; k < NbCakes; k++) _obs.Add((C(k), RingR - 0.5f));
        if (PropObstacles != null) _obs.AddRange(PropObstacles());
        return _obs;
    }

    static float SegDist(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        float t = ab.LengthSquared() > 0 ? Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1) : 0;
        return (a + ab * t - p).Length();
    }

    // Trajet direct d'un point à un autre sur la couverture : tout droit, en contournant
    // par le plus court côté chaque gâteau ou objet qui barre la route (pas d'autoroute).
    public static List<Vector3> Direct(Vector2 from, Vector2 to, int ignoreCake = -1)
    {
        var pts = new List<Vector3>();
        Walk(from, to, ignoreCake, pts, 0);
        pts.Add(new Vector3(to.X, 0, to.Y));
        return pts;
    }

    static void Walk(Vector2 a, Vector2 b, int ignore, List<Vector3> pts, int depth)
    {
        if (depth > 6) return;
        var obs = Obstacles();
        int hit = -1; float bestT = float.MaxValue;
        var ab = b - a; float L = ab.Length();
        if (L < 1e-3f) return;
        for (int i = 0; i < obs.Count; i++)
        {
            if (i == ignore) continue;
            var (c, r) = obs[i];
            if ((a - c).Length() < r + 0.2f || (b - c).Length() < r + 0.2f) continue; // départ/arrivée contre l'obstacle
            if (SegDist(a, b, c) >= r) continue;
            float t = (c - a).Dot(ab) / L;
            if (t < bestT) { bestT = t; hit = i; }
        }
        if (hit < 0) return;
        var (cc, rr) = obs[hit];
        float R = rr + 1.2f;
        // arc autour de l'obstacle, du côté le plus court, entre les points tangents
        float a0 = MathF.Atan2(a.X - cc.X, a.Y - cc.Y), a1 = MathF.Atan2(b.X - cc.X, b.Y - cc.Y);
        float da = Mathf.Wrap(a1 - a0, -MathF.PI, MathF.PI);
        float ta = MathF.Acos(Mathf.Clamp(R / MathF.Max(R, (a - cc).Length()), -1, 1));
        float tb = MathF.Acos(Mathf.Clamp(R / MathF.Max(R, (b - cc).Length()), -1, 1));
        float s0 = a0 + MathF.Sign(da) * ta, s1 = a1 - MathF.Sign(da) * tb;
        float span = Mathf.Wrap(s1 - s0, -MathF.PI, MathF.PI);
        if (MathF.Sign(span) != MathF.Sign(da) && MathF.Abs(span) > 0.01f) span = 0;
        var entry = cc + new Vector2(MathF.Sin(s0), MathF.Cos(s0)) * R;
        Walk(a, entry, ignore, pts, depth + 1);
        int n = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(span) / 0.25f));
        for (int i = 0; i <= n; i++) { float an = s0 + span * i / n; pts.Add(new Vector3(cc.X + MathF.Sin(an) * R, 0, cc.Y + MathF.Cos(an) * R)); }
        var exit = cc + new Vector2(MathF.Sin(s0 + span), MathF.Cos(s0 + span)) * R;
        Walk(exit, b, ignore, pts, depth + 1);
    }

    // Chemin du nid jusqu'à la porte du gâteau k (points au sol)
    public static List<Vector3> RouteFromNest(int k)
    {
        var r = new List<Vector3> { new(Nest.X, 0, Nest.Y) };
        r.AddRange(Direct(Nest, Door(k), k));
        return r;
    }

    public static float PathLength(List<Vector3> pts)
    {
        float d = 0;
        for (int i = 1; i < pts.Count; i++) d += pts[i].DistanceTo(pts[i - 1]);
        return d;
    }


    // Bureau de la gestionnaire
    public static readonly Vector2 Manager = new(13.5f, 56f);
    public const float ManagerTalkR = 5f;
}
