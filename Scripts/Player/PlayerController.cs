// ─────────────────────────────────────────────────────────────
// La fourmi du joueur : déplacements, escalade, saut, collisions
// sur la surface LISSE du gâteau (iso 0,5 du champ de densité,
// testée en 27 points), visée (DDA voxel), morsures, vente au nid.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Render;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Player;

public struct AimHit
{
    public CakeSim K;
    public int Block;
    public int X, Y, Z;
    public float T;
    public bool InRange;
}

public partial class PlayerController : Node3D
{
    public GameState Game;
    public List<CakeView> Views;
    public CameraRig Cam;
    public AntSwarm Ant;
    float _gait, _bite, _time, _walk;
    Vector3 _lastDisp;

    public float Yaw;            // orientation du modèle
    public float Vy;
    public bool Grounded, Moving;
    public float ClimbK;
    public float StunT;
    public AimHit? Aim;
    public bool InputLocked;     // un panneau est ouvert
    public bool BiteHeld;        // clic gauche maintenu (hors interface)
    public bool PadAim;          // manette : on vise au centre de l'écran

    float _biteTimer, _sellTimer;
    Vector3 _disp;
    static readonly List<Vector3> ColPts = new();
    const float StepH = 0.14f;   // marche franchie sans escalader (un tapis, plus haut, s'escalade)
    float _cling, _dbgT;
    Basis _orient = Basis.Identity;
    public float BiteAnim => _bite;
    public float LoadAnim => Game.Player.Bag / (float)Game.Stats().cap;
    // œil de la fourmi : au-dessus de la tête, dans l'orientation du modèle
    public Vector3 EyePos => _disp + _orient * new Vector3(0, 0.50f, 0.28f); // un peu au-dessus de la tête
    public Basis Orientation => _orient;
    public static bool Debug;                // > 0 : collée à une paroi (escalade en cours)

    static PlayerController()
    {
        foreach (float fy in new[] { 0.04f, 0.3f, AntH })
        foreach (int fx in new[] { -1, 0, 1 })
        foreach (int fz in new[] { -1, 0, 1 })
        {
            float l = MathF.Sqrt(fx * fx + fz * fz);
            if (l == 0) l = 1;
            ColPts.Add(new Vector3(fx / l * AntR, fy, fz / l * AntR));
        }
    }

    public void Setup(GameState game, List<CakeView> views, CameraRig cam)
    {
        Game = game; Views = views; Cam = cam;
        Ant = AntSwarm.Create(1);
        AddChild(Ant);
        _disp = game.Player.Pos;
        _lastDisp = _disp;
        Yaw = MathF.PI; // face au premier gâteau (vers -Z)
    }

    // ───── Physique ─────
    // Le corps : trois capsules verticales (tête, thorax, gastre) alignées sur le cap de la
    // fourmi, pieds à pos.Y. Elles couvrent tout le modèle : la tête ne rentre plus dans rien.
    static readonly (float z, float r, float h)[] Parts = { (0.36f, 0.15f, 0.50f), (0.04f, 0.14f, 0.44f), (-0.30f, 0.17f, 0.50f) };
    const float FootGap = 0.01f;
    static readonly float[] Ox = { 1, -1, 0, 0 }, Oz = { 0, 0, 1, -1 };
    float _colYaw = MathF.PI;

    public bool Collides(float x, float y, float z) => CollidesYaw(new Vector3(x, y, z), _colYaw);

    public bool CollidesYaw(Vector3 p, float yaw)
    {
        if (p.Y < -0.01f) return true;
        float sy = MathF.Sin(yaw), cy = MathF.Cos(yaw);
        foreach (var (pz, r, h) in Parts)
        {
            var b = new Vector3(p.X + sy * pz, p.Y + FootGap, p.Z + cy * pz);
            // sol en relief (assiettes, dôme du nid) : la base de la capsule ne s'y enfonce pas
            float g = Terrain.BaseGroundAt(b.X, b.Z);
            for (int k = 0; k < 4; k++) g = MathF.Max(g, Terrain.BaseGroundAt(b.X + Ox[k] * r * 0.7f, b.Z + Oz[k] * r * 0.7f));
            if (b.Y < g - 0.02f) return true;
            // objets du décor : leur maillage exact
            if (PropPhysics.HitsCapsule(b, r, h)) return true;
            // tapis roulants : un caisson plein jusqu'à la bande (on monte dessus, on ne passe pas au travers)
            if (BeltBlocks(b, r)) return true;
            // gâteaux : leurs tranches de maillage sont aussi des formes physiques (déjà testées ci-dessus).
            // Filet de sécurité : le cœur de la capsule en pleine pâte (un éboulis vient de tomber dessus).
            // Un éboulis garde la moitié de ses miettes (densité ≈ 0,78) : seuil sous ce palier, sinon
            // la fourmi restait enfermée DANS le bloc tombé, entre ses parois.
            if (CakeDensity(b + new Vector3(0, h / 2, 0)) >= 0.6f) return true;
        }
        return false;
    }

    public const float BeltTopH = 0.34f;
    bool BeltBlocks(Vector3 b, float r)
    {
        int cx = Mathf.FloorToInt(b.X), cz = Mathf.FloorToInt(b.Z);
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var c = Game.At(cx + dx, cz + dz);
            if (c == null || c.Type != "belt") continue;
            float top = Terrain.GroundAt(c.X + 0.5f, c.Z + 0.5f) + BeltTopH;
            if (b.Y >= top - 0.005f) continue;
            float qx = Mathf.Clamp(b.X, c.X + 0.02f, c.X + 0.98f), qz = Mathf.Clamp(b.Z, c.Z + 0.02f, c.Z + 0.98f);
            if ((qx - b.X) * (qx - b.X) + (qz - b.Z) * (qz - b.Z) < r * r) return true;
        }
        return false;
    }

    float BeltTop(float x, float z)
    {
        var c = Game.At(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
        return c != null && c.Type == "belt" ? Terrain.GroundAt(x, z) + BeltTopH : float.NegativeInfinity;
    }

    // ───── Surfaces : hauteur sous un point, paroi devant (pour poser le modèle) ─────
    public bool InsideCake(Vector3 p) => CakeDensity(p) >= 0.5f;

    float CakeDensity(Vector3 p)
    {
        float d = 0;
        foreach (var v in Views)
        {
            float dx = p.X - v.Sim.Cx, dz = p.Z - v.Sim.Cz;
            if (dx * dx + dz * dz > (CakeR + 1.5f) * (CakeR + 1.5f)) continue;
            d = MathF.Max(d, v.Field.DensityAt(p.X, p.Y, p.Z));
        }
        return d;
    }

    // un point (œil, caméra) est-il dans la matière ?
    public bool PointSolid(Vector3 p, float r)
    {
        if (p.Y < Terrain.BaseGroundAt(p.X, p.Z) + 0.02f) return true;
        return PropPhysics.HitsSphere(p, r) || CakeDensity(p) >= 0.8f;
    }

    // dessus de la surface sous (x, z), cherché entre yRef + 0,45 et yRef − 0,7
    float SurfaceY(float x, float z, float yRef)
    {
        float top = yRef + 0.45f, bot = yRef - 0.7f;
        float best = MathF.Max(Terrain.BaseGroundAt(x, z), BeltTop(x, z));
        if (best > top) best = float.NegativeInfinity;
        if (PropPhysics.RayHit(new Vector3(x, top, z), new Vector3(x, bot, z)) is { } hit) best = MathF.Max(best, hit.p.Y);
        return float.IsNegativeInfinity(best) ? yRef : best;
    }

    Vector3 HeightNormal(float x, float z)
    {
        const float e = 0.12f;
        float hx = Terrain.BaseGroundAt(x + e, z) - Terrain.BaseGroundAt(x - e, z), hz = Terrain.BaseGroundAt(x, z + e) - Terrain.BaseGroundAt(x, z - e);
        return new Vector3(-hx / (2 * e), 1, -hz / (2 * e)).Normalized();
    }

    // premier contact le long d'un segment (objets, gâteau, sol en relief) : point et normale
    bool SurfaceHit(Vector3 from, Vector3 dir, float len, out Vector3 hitP, out Vector3 hitN)
    {
        hitP = default; hitN = Vector3.Up;
        float best = len;
        bool any = false;
        if (PropPhysics.RayHit(from, from + dir * len) is { } h)
        {
            best = from.DistanceTo(h.p); hitP = h.p; hitN = h.n; any = true;
        }
        for (float t = 0.03f; t < best; t += 0.04f)
        {
            var q = from + dir * t;
            if (q.Y < Terrain.BaseGroundAt(q.X, q.Z)) { hitP = q; hitN = HeightNormal(q.X, q.Z); return true; }
        }
        return any;
    }

    bool CanWalk(float x, float z)
    {
        if (!Terrain.InMoveArea(x, z) || Game.InsideCloche(x, z, AntR)) return false;
        if (Props.BlockedFixed(x, z, AntR)) return false;
        // les machines sont solides (pas les tapis : on marche dessus)
        var b = Game.At(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
        if (b != null && b.Type != "belt") return false;
        return true;
    }

    public bool Underground()
    {
        // sous terre : du gâteau plein au-dessus de la fourmi (un plafond), même quand elle grimpe
        // sur une paroi de galerie. Sur la paroi EXTÉRIEURE, au-dessus d'elle il n'y a que de l'air.
        var p = Game.Player.Pos;
        if (InsideCake(p + new Vector3(0, 0.3f, 0))) return false;
        for (float dy = 0.7f; dy <= 3.2f; dy += 0.25f) if (InsideCake(p + new Vector3(0, dy, 0))) return true;
        return false;
    }

    public void UpdatePlayer(float dt)
    {
        var P = Game.Player;
        var st = Game.Stats();
        float ix = 0, iz = 0;
        if (!InputLocked && StunT <= 0)
        {
            if (Input.IsActionPressed("move_forward")) iz += 1;
            if (Input.IsActionPressed("move_back")) iz -= 1;
            if (Input.IsActionPressed("move_left")) ix -= 1;
            if (Input.IsActionPressed("move_right")) ix += 1;
        }
        float yaw = Cam.MoveYaw;
        float fx = -MathF.Sin(yaw), fz = -MathF.Cos(yaw);
        float rx = -fz, rz = fx;
        float mx = fx * iz + rx * ix, mz = fz * iz + rz * ix;
        float ml = MathF.Sqrt(mx * mx + mz * mz);
        Moving = ml > 0;
        bool blocked = false;
        var pos = P.Pos;
        float mx0 = 0, mz0 = 0;
        float sp = st.speed;
        // le corps pivote vers la direction voulue, seulement s'il a la place de le faire
        float wantYaw = Cam.FirstPerson ? Cam.FpYaw + MathF.PI : ml > 0 ? MathF.Atan2(mx / ml, mz / ml) : _colYaw;
        if (MathF.Abs(Mathf.Wrap(wantYaw - _colYaw, -MathF.PI, MathF.PI)) > 1e-3f)
        {
            float ny = Mathf.LerpAngle(_colYaw, wantYaw, Mathf.Min(1, dt * (Cam.FirstPerson ? 30 : 10)));
            if (!CollidesYaw(pos, ny)) _colYaw = ny;
            else
            {
                // contre une paroi : on pivote par petits pas tant que ça passe
                float a0 = _colYaw;
                for (int k = 0; k < 6; k++) { float t = Mathf.LerpAngle(a0, ny, (6 - k) / 6f * 0.5f); if (!CollidesYaw(pos, t)) { _colYaw = t; break; } }
            }
        }
        Yaw = _colYaw;
        if (ml > 0)
        {
            mx /= ml; mz /= ml; mx0 = mx; mz0 = mz;
            bool free = !CanWalk(pos.X, pos.Z); // coincée hors zone : on la laisse sortir
            // un pas sur un axe ; une petite marche (bord de serviette, pente) se monte en douceur :
            // on se pose ensuite exactement sur la surface (pas de saut)
            bool Try(float nx, float nz)
            {
                if (!free && !CanWalk(nx, nz)) return false;
                if (!Collides(nx, pos.Y, nz)) { pos.X = nx; pos.Z = nz; return true; }
                if ((Grounded || _cling > 0) && !Collides(nx, pos.Y + StepH, nz) && !Collides(pos.X, pos.Y + StepH, pos.Z))
                {
                    float lo = pos.Y, hi = pos.Y + StepH;
                    for (int k = 0; k < 8; k++) { float mid = (lo + hi) / 2; if (Collides(nx, mid, nz)) lo = mid; else hi = mid; }
                    pos.X = nx; pos.Z = nz; pos.Y = hi;
                    return true;
                }
                blocked = true;
                return false;
            }
            Try(pos.X + mx * sp * dt, pos.Z);
            Try(pos.X, pos.Z + mz * sp * dt);
        }

        // escalade : on pousse contre une paroi → on monte. Sous un surplomb (dessous d'un bol,
        // d'une anse), on suit la courbe : on monte en reculant le long de la surface.
        // Tous les déplacements restent à la vitesse d'escalade : jamais de saut à travers rien.
        bool climbing = false;
        if (ml > 0 && blocked)
        {
            float cs = Climb * st.speed * dt;
            foreach (var (upK, back) in new[] { (1f, 0f), (0.7f, 0.7f), (0.35f, 1f), (0f, 1f), (-0.5f, 1f) })
            {
                float nx = pos.X - mx0 * cs * back, ny = pos.Y + cs * upK, nz = pos.Z - mz0 * cs * back;
                if (!Collides(nx, ny, nz)) { pos.X = nx; pos.Y = ny; pos.Z = nz; climbing = true; break; }
            }
            if (climbing) { Vy = 0; _cling = 0.25f; }
        }
        // collée à la paroi : si la surface fuit devant (pied du bol qui s'amincit, arrondi d'un bord),
        // on avance vers elle à la vitesse d'escalade au lieu de tomber
        else if (ml > 0 && _cling > 0 && !Grounded)
        {
            for (float d = 0.04f; d <= 0.45f; d += 0.04f)
            {
                if (!Collides(pos.X + mx0 * d, pos.Y, pos.Z + mz0 * d)) continue;
                float adv = MathF.Min(d - 0.04f, Climb * st.speed * dt);
                pos.X += mx0 * adv; pos.Z += mz0 * adv;
                climbing = true; Vy = 0; _cling = 0.25f;
                break;
            }
        }
        _cling = MathF.Max(0, _cling - dt);
        if (Debug && (_dbgT += dt) > 0.2f) { _dbgT = 0; GD.Print($"[plog] {pos.X:0.00},{pos.Y:0.00},{pos.Z:0.00} blocked {blocked} climb {climbing} grounded {Grounded} cling {_cling:0.00}"); }
        if (!climbing)
        {
            Vy = MathF.Max(-MaxFall, Vy - Gravity * dt);
            float ny = pos.Y + Vy * dt;
            if (Collides(pos.X, ny, pos.Z))
            {
                if (Vy < 0)
                {
                    // on se pose exactement sur la surface (dichotomie)
                    float lo = ny, hi = pos.Y;
                    for (int k = 0; k < 12; k++) { float mid = (lo + hi) / 2; if (Collides(pos.X, mid, pos.Z)) lo = mid; else hi = mid; }
                    pos.Y = hi; Grounded = true;
                }
                Vy = 0;
            }
            else { pos.Y = ny; Grounded = false; }
        }
        else Grounded = false;
        if ((Grounded || pos.Y <= 0.001f) && Input.IsActionPressed("jump") && !InputLocked && StunT <= 0) { Vy = Jump; Grounded = false; Sfx.Play("jump"); }
        // coincée (un éboulis vient de tomber dessus, elle remonte dans les miettes) : elle glisse vers
        // l'espace libre le plus proche — au-dessus, sur le côté, dans la galerie — à vitesse bornée
        if (Collides(pos.X, pos.Y, pos.Z)) pos = Unstick(pos, dt);
        else _unstick = null;
        ClimbK += ((climbing ? 1 : 0) - ClimbK) * Mathf.Min(1, dt * 10);
        P.Pos = pos;

        // ── pose du modèle : posé SUR la surface réelle, orienté selon sa pente ou sa paroi ──
        var fwdH = new Vector3(MathF.Sin(_colYaw), 0, MathF.Cos(_colYaw));
        var rightH = Vector3.Up.Cross(fwdH);
        var up = Vector3.Up;
        var target = pos;
        bool onWall = false;
        if (climbing || _cling > 0)
        {
            // la paroi touchée : devant, au-dessus (surplomb) ou en biais
            var c = pos + new Vector3(0, 0.25f, 0);
            float bestD = float.MaxValue;
            foreach (var dir in new[] { fwdH, (fwdH + Vector3.Up).Normalized(), Vector3.Up, (Vector3.Up - fwdH).Normalized() })
                if (SurfaceHit(c, dir, 1.0f, out var hp, out var hn))
                {
                    float d = c.DistanceTo(hp);
                    if (d < bestD) { bestD = d; target = hp + hn * 0.02f; up = hn; onWall = true; }
                }
        }
        if (!onWall)
        {
            // au sol (ou sur un objet) : la pente sous l'avant, l'arrière et les côtés
            float hF = SurfaceY(pos.X + fwdH.X * 0.36f, pos.Z + fwdH.Z * 0.36f, pos.Y);
            float hB = SurfaceY(pos.X - fwdH.X * 0.30f, pos.Z - fwdH.Z * 0.30f, pos.Y);
            float hR = SurfaceY(pos.X + rightH.X * 0.22f, pos.Z + rightH.Z * 0.22f, pos.Y);
            float hL = SurfaceY(pos.X - rightH.X * 0.22f, pos.Z - rightH.Z * 0.22f, pos.Y);
            var tf = fwdH * 0.66f + Vector3.Up * (hF - hB);
            var tr = rightH * 0.44f + Vector3.Up * (hR - hL);
            up = tf.Cross(tr).Normalized();
            if (up.Y < 0) up = -up;
            if (!Grounded) up = up.Lerp(Vector3.Up, 0.5f).Normalized();
        }
        // avant : le cap de la fourmi ramené dans le plan de la surface ; sur une paroi, elle monte
        var fwd = onWall ? Vector3.Up - up * Vector3.Up.Dot(up) : fwdH - up * fwdH.Dot(up);
        if (fwd.LengthSquared() < 1e-4f) fwd = fwdH - up * fwdH.Dot(up);
        if (fwd.LengthSquared() < 1e-4f) fwd = Vector3.Forward;
        fwd = fwd.Normalized();
        var want = new Basis(up.Cross(fwd).Normalized(), up, fwd).Orthonormalized();
        _orient = new Basis(_orient.GetRotationQuaternion().Slerp(want.GetRotationQuaternion(), Mathf.Min(1, dt * 12)));
        _disp = _disp.Lerp(target, Mathf.Min(1, dt * 18));
        StunT = MathF.Max(0, StunT - dt);
        // animation : la phase de marche avance avec la distance réellement parcourue
        var dd = _disp - _lastDisp;
        float moved = dd.Length();
        _lastDisp = _disp;
        _gait += moved / AntRig.Stride * MathF.Tau;
        _walk += ((moved / MathF.Max(dt, 1e-4f) > 0.4f || ClimbK > 0.3f ? 1 : 0) - _walk) * Mathf.Min(1, dt * 8);
        _time += dt;
        _bite = MathF.Max(0, _bite - dt * 5.5f);
        Ant.Begin();
        Ant.Add(new AntPose
        {
            Pos = _disp, UseOrient = true, Orient = _orient, Scale = 1, Gait = _gait, Walk = _walk, Bite = _bite,
            Load = P.Bag / (float)st.cap, LoadColor = CrumbColor(P.BagCake), Time = _time, Stun = StunT > 0 ? Mathf.Min(1, StunT) : 0,
            Color = AntColors[Math.Clamp(Game.Player.Color, 0, AntColors.Length - 1)], Hidden = Cam.FirstPerson,
        });
        Ant.End();

        // vente automatique à la fourmilière
        _sellTimer -= dt;
        if (P.Bag > 0 && _sellTimer <= 0 && Game.NearNest(pos.X, pos.Z))
        {
            _sellTimer = 0.5f;
            double v = Game.PlayerSell();
            if (v > 0) Game.Emit(new GameEvent("sold") { Value = v, Pos = pos });
        }
        // dépôt dans un entrepôt qu'on touche
        if (P.Bag > 0 && _sellTimer <= 0 && Game.StoreNear(pos.X, pos.Z) is { } store)
        {
            _sellTimer = 0.5f;
            long q = Game.PlayerDeposit(store);
            if (q > 0) Game.Emit(new GameEvent("deposit") { N = (int)q, Pos = new Vector3(store.X + 0.5f, 1.4f, store.Z + 0.5f) });
        }
    }

    public Vector3 DisplayPos => _disp;

    Vector3? _unstick;
    static readonly Vector3[] UnstickDirs = BuildUnstickDirs();
    static Vector3[] BuildUnstickDirs()
    {
        var l = new List<Vector3> { Vector3.Up };
        for (int e = 0; e < 3; e++)
        {
            float el = new[] { 0.9f, 0.35f, -0.3f }[e];
            for (int a = 0; a < 12; a++)
            {
                float an = a * MathF.Tau / 12;
                l.Add(new Vector3(MathF.Cos(an) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(an) * MathF.Cos(el)));
            }
        }
        return l.ToArray();
    }

    Vector3 Unstick(Vector3 pos, float dt)
    {
        // cible : le point libre le plus proche (cherché par rayons croissants, le haut d'abord)
        if (_unstick is not { } t || Collides(t.X, t.Y, t.Z))
        {
            _unstick = null;
            for (float r = 0.12f; r <= 3.0f && _unstick == null; r += 0.12f)
                foreach (var d in UnstickDirs)
                {
                    var q = pos + d * r;
                    if (q.Y < 0 || !Terrain.InMoveArea(q.X, q.Z) || Collides(q.X, q.Y, q.Z)) continue;
                    _unstick = q; break;
                }
            if (_unstick == null) return pos + new Vector3(0, 4f * dt, 0); // rien autour : on remonte
        }
        var to = _unstick.Value - pos;
        float len = to.Length(), step = MathF.Max(2.5f, len * 6) * dt;
        Vy = 0;
        if (len <= step) { var r = _unstick.Value; _unstick = null; return r; }
        return pos + to / len * step;
    }


    // ───── Visée : un rayon parcourt les blocs un par un (DDA) ─────
    public AimHit? Raycast(Vector3 o, Vector3 d, float maxT = 300)
    {
        int x = Mathf.FloorToInt(o.X), y = Mathf.FloorToInt(o.Y), z = Mathf.FloorToInt(o.Z);
        int sx = Math.Sign(d.X), sy = Math.Sign(d.Y), sz = Math.Sign(d.Z);
        float tdx = sx != 0 ? MathF.Abs(1 / d.X) : float.PositiveInfinity;
        float tdy = sy != 0 ? MathF.Abs(1 / d.Y) : float.PositiveInfinity;
        float tdz = sz != 0 ? MathF.Abs(1 / d.Z) : float.PositiveInfinity;
        float tmx = sx > 0 ? (x + 1 - o.X) * tdx : sx < 0 ? (o.X - x) * tdx : float.PositiveInfinity;
        float tmy = sy > 0 ? (y + 1 - o.Y) * tdy : sy < 0 ? (o.Y - y) * tdy : float.PositiveInfinity;
        float tmz = sz > 0 ? (z + 1 - o.Z) * tdz : sz < 0 ? (o.Z - z) * tdz : float.PositiveInfinity;
        float t = 0;
        for (int n = 0; n < 1000 && t < maxT; n++)
        {
            if (y >= 0 && y < Layers && Game.SolidAt(x, y, z))
            {
                var K = Game.CakeAtCell(x, z);
                return new AimHit { K = K, Block = K.IdxW(x, y, z), X = x, Y = y, Z = z, T = t };
            }
            if (tmx < tmy && tmx < tmz) { x += sx; t = tmx; tmx += tdx; }
            else if (tmy < tmz) { y += sy; t = tmy; tmy += tdy; }
            else { z += sz; t = tmz; tmz += tdz; }
            if (y < 0) return null;
        }
        return null;
    }

    public void UpdateAim()
    {
        Vector3 o, d;
        var cam = Cam.Camera;
        if (Cam.FirstPerson)
        {
            o = cam.GlobalPosition;
            d = -cam.GlobalTransform.Basis.Z;
        }
        else if (PadAim)
        {
            var c = GetViewport().GetVisibleRect().Size / 2;
            o = cam.ProjectRayOrigin(c);
            d = cam.ProjectRayNormal(c);
        }
        else
        {
            var mp = GetViewport().GetMousePosition();
            o = cam.ProjectRayOrigin(mp);
            d = cam.ProjectRayNormal(mp);
        }
        var hit = Raycast(o, d);
        if (hit is { } h)
        {
            if (!h.K.Unlocked) { Aim = null; return; }
            var me = Game.Player.Pos;
            float dx = h.X + 0.5f - me.X, dy = h.Y + 0.5f - (me.Y + 0.3f), dz = h.Z + 0.5f - me.Z;
            h.InRange = MathF.Sqrt(dx * dx + dy * dy + dz * dz) <= Reach;
            Aim = h;
        }
        else Aim = null;
    }

    // ───── Récolte ─────
    public void UpdateHarvest(float dt)
    {
        _biteTimer -= dt;
        var fo = Game.FeveObj;
        var me = Game.Player.Pos;
        if (fo != null && fo.State == FeveState.Ground && BiteHeld && !InputLocked && (fo.Pos - me).Length() <= 2.2f)
        {
            Game.PickupFeve();
            BiteHeld = false;
            return;
        }
        if (!BiteHeld || InputLocked || StunT > 0) return;
        if (Cam.FirstPerson && Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (Aim is not { } a || !a.InRange || _biteTimer > 0) return;
        var st = Game.Stats();
        _biteTimer = st.cooldown;
        bool onFeve = a.Block == a.K.Feve;
        if (Game.Player.Bag >= st.cap && !onFeve) { Game.Emit(new GameEvent("bagFull")); return; }
        var r = Game.PlayerBite(a.K, a.Block);
        _bite = 1;
        if (r.Taken > 0)
        {
            var c = new Vector3(a.X + 0.5f, a.Y + 0.5f, a.Z + 0.5f);
            var toMe = (me + new Vector3(0, 0.3f, 0) - c).Normalized();
            Game.Emit(new GameEvent("bite") { Cake = a.K.Index, Pos = c + toMe * 0.55f, N = a.Y });
            foreach (var f in r.Found)
                Game.Emit(new GameEvent("drop") { Text = f.drop.Id, N = f.fig, Value = f.gain, Who = f.isNew ? "new" : f.all ? "all" : "", Pos = c + new Vector3(0, 0.7f, 0) });
        }
        if (r.Full && !onFeve) Game.Emit(new GameEvent("bagFull"));
    }
}
