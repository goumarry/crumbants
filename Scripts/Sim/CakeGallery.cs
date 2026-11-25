// ─────────────────────────────────────────────────────────────
// Galeries des ouvrières : un réseau 3D organisé en « macro-cases »
// de 2 × 2 colonnes et 2 couches de haut. Une macro-case
// m = I + K·MW + y·MA (y = couche du sol). Les fourmis marchent au
// centre, là où le sol lissé est parfaitement plat.
// Règles : pas de salle, la croûte n'est jamais percée (2 blocs
// pleins au-dessus), pas de fenêtre sur les flancs au-dessus de la
// couche 2, et rien ne doit s'écrouler (marge de solidité).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Sim;

public sealed class DigStep
{
    public int From, T, Dir;
    public int Cake = -1;         // gâteau auquel appartiennent les indices de blocs
    public char Kind;
    public List<int> R; // blocs à vider, dans l'ordre
    public List<int> Via; // (évasion) cases vides à traverser avant d'atteindre From
}

public sealed class GalleryMap
{
    public double Time;
    public int Gen = -1;          // version du gâteau pour laquelle la carte a été calculée
    public float[] Dist;
    public int[] Par;
    public List<int> Order = new();
}

public sealed partial class CakeSim
{
    public const int GS = 2, GH = 2;
    public const int MW = CW / GS, MA = MW * MW;
    static readonly (int di, int dk)[] Nb4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    public int MY(int m) => m / MA;
    public int MI(int m) => (m % MA) % MW;
    public int MK(int m) => (m % MA) / MW;

    public IEnumerable<int> MBlocks(int m)
    {
        int y = MY(m), I = MI(m), K = MK(m);
        for (int dy = 0; dy < GH; dy++)
        for (int dk = 0; dk < GS; dk++)
        for (int di = 0; di < GS; di++)
        {
            int yy = y + dy;
            if (yy < Layers) yield return GS * I + di + (GS * K + dk) * CW + yy * Area;
        }
    }

    public int[] MCols(int m)
    {
        int I = MI(m), K = MK(m);
        return new[] { GS * I + GS * K * CW, GS * I + 1 + GS * K * CW, GS * I + (GS * K + 1) * CW, GS * I + 1 + (GS * K + 1) * CW };
    }

    // centre d'une macro-case (monde), au niveau de son sol
    public Vector3 MPos(int m)
    {
        int y = MY(m);
        return new Vector3(GS * MI(m) - IX + 1, y == 0 ? Render.Terrain.BoardH : y, GS * MK(m) - IZ + 1);
    }

    public int MAt(Vector3 p)
    {
        int I = Mathf.FloorToInt((p.X + IX) / GS), K = Mathf.FloorToInt((p.Z + IZ) / GS), y = Mathf.RoundToInt(p.Y < 0.5f ? 0 : p.Y);
        if (I < 0 || K < 0 || I >= MW || K >= MW || y < 0 || y >= Layers) return -1;
        return I + K * MW + y * MA;
    }

    public bool IsPlateM(int m)
    {
        if (m >= MA) return false;
        foreach (int c in MCols(m)) if (H0[c] != 0) return false;
        return true;
    }

    // une fourmi peut-elle se tenir dans cette macro-case ?
    public bool StandableM(int m)
    {
        int y = MY(m);
        if (y + 1 >= Layers) return false;
        // la fève ne gêne pas : les ouvrières la traversent (seule la joueuse la ramasse)
        foreach (int b in MBlocks(m)) if (Cake[b] != 0 && b != Feve) return false;
        int foot = 0;
        foreach (int c in MCols(m))
        {
            if (H0[c] == 0) continue;
            foot++;
            // fond de cuvette à ciel ouvert ; en mode « par le dessus », aussi le dessus entamé du gâteau
            if (ColTop[c] < y) { if (y > 0 && !(Relax >= 2 && Cake[c + (y - 1) * Area] != 0)) return false; continue; }
            if (ColTop[c] <= y + 1) return false;                     // sinon : couverte
            if (y > 0 && Cake[c + (y - 1) * Area] == 0) return false;  // sol plein
        }
        if (foot == 0) return y == 0;
        if (y > 0 && foot < GS * GS) return false;
        return true;
    }

    void MovesM(int m, List<(int n, float c)> outL)
    {
        outL.Clear();
        int y = MY(m), I = MI(m), K = MK(m);
        bool plate = IsPlateM(m);
        foreach (var (di, dk) in Nb4)
        {
            int I2 = I + di, K2 = K + dk;
            if (I2 < 0 || K2 < 0 || I2 >= MW || K2 >= MW) continue;
            int n = I2 + K2 * MW + y * MA;
            if (StandableM(n) && !(plate && IsPlateM(n))) outL.Add((n, 2));
            if (y + 1 < Layers && StandableM(n + MA)) outL.Add((n + MA, 2.3f));
            if (y >= 1 && StandableM(n - MA)) outL.Add((n - MA, 2.3f));
        }
    }

    // Trajet sur la couverture depuis un point jusqu'à une entrée : en ligne droite si elle est
    // visible, sinon en contournant le gâteau le long d'un cercle.
    public List<Vector3> Approach(Vector2 from, int m)
    {
        var p = MPos(m);
        float hx = from.X - Cx, hz = from.Y - Cz, lx = p.X - Cx, lz = p.Z - Cz;
        var pts = new List<Vector3>();
        if (SegDist2(hx, hz, lx, lz) >= (CakeR + 0.6f) * (CakeR + 0.6f)) return pts;
        float Rw = CakeR + 2.5f;
        float a0 = MathF.Atan2(hx, hz), a1 = MathF.Atan2(lx, lz);
        float da = Mathf.Wrap(a1 - a0, -MathF.PI, MathF.PI);
        int n = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(da) / 0.25f));
        for (int i = 0; i <= n; i++) { float a = a0 + da * i / n; pts.Add(new Vector3(Cx + MathF.Sin(a) * Rw, 0, Cz + MathF.Cos(a) * Rw)); }
        return pts;
    }

    static float SegDist2(float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az, L = dx * dx + dz * dz;
        float t = L > 0 ? Math.Clamp(-(ax * dx + az * dz) / L, 0, 1) : 0;
        float x = ax + dx * t, z = az + dz * t;
        return x * x + z * z;
    }

    float ApproachCost(int m, Vector2 door)
    {
        var pts = Approach(door, m);
        var p = MPos(m);
        if (pts.Count == 0) return new Vector2(p.X - door.X, p.Z - door.Y).Length();
        float d = new Vector2(pts[0].X - door.X, pts[0].Z - door.Y).Length();
        for (int i = 1; i < pts.Count; i++) d += pts[i].DistanceTo(pts[i - 1]);
        return d + new Vector2(p.X - pts[^1].X, p.Z - pts[^1].Z).Length();
    }

    GalleryMap _gal;
    bool _galDirty = true;
    int _galGen;
    void MarkGal() { _galDirty = true; _galGen++; }
    public void InvalidateGalleries() => MarkGal();

    // Carte des chemins depuis la porte (partagée, recalculée au plus une fois par seconde)
    public GalleryMap Galleries()
    {
        if (_gal != null && (!_galDirty || Game.PlayTime - _gal.Time < 1)) return _gal;
        _galDirty = false;
        var door = Layout.Door(Index);
        _gal = BuildMap(_gal, m => ApproachCost(m, door));
        return _gal;
    }

    // Carte des chemins depuis un autre point de la couverture (un entrepôt) : on entre par
    // l'entrée la plus proche de CE point, pas par celle qui arrange la porte côté nid.
    readonly Dictionary<(int, int), GalleryMap> _galFrom = new();
    public static bool DoorOnly; // développement : --dooronly (ancien calcul, pour comparer)
    public GalleryMap GalleriesFrom(Vector2 from)
    {
        var door = Layout.Door(Index);
        if (DoorOnly || from.DistanceTo(door) < 1.5f) return Galleries();
        var key = (Mathf.RoundToInt(from.X), Mathf.RoundToInt(from.Y));
        _galFrom.TryGetValue(key, out var G);
        if (G != null && (G.Gen == _galGen || Game.PlayTime - G.Time < 1)) return G;
        if (G == null && _galFrom.Count >= 32) _galFrom.Clear(); // points de départ oubliés au fil du jeu
        G = BuildMap(G, m => ApproachCost(m, from));
        _galFrom[key] = G;
        return G;
    }

    public static double MapMs; public static int MapCount; // développement : coût des cartes (--wlog)
    GalleryMap BuildMap(GalleryMap G, Func<int, float> rootCost)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return BuildMapCore(G, rootCost); }
        finally { MapMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds; MapCount++; }
    }

    GalleryMap BuildMapCore(GalleryMap G, Func<int, float> rootCost)
    {
        int N = MA * Layers;
        G ??= new GalleryMap { Dist = new float[N], Par = new int[N] };
        Array.Fill(G.Dist, float.PositiveInfinity);
        Array.Fill(G.Par, -1);
        G.Order.Clear();
        G.Time = Game.PlayTime;
        G.Gen = _galGen;
        var heap = new PriorityQueue<int, float>();
        // entrées possibles tout autour du gâteau (cases d'assiette au bord)
        for (int m = 0; m < MA; m++)
        {
            if (!IsPlateM(m)) continue;
            int I = MI(m), K = MK(m);
            bool edge = false;
            foreach (var (di, dk) in Nb4)
            {
                int I2 = I + di, K2 = K + dk;
                if (I2 >= 0 && K2 >= 0 && I2 < MW && K2 < MW && !IsPlateM(I2 + K2 * MW)) { edge = true; break; }
            }
            if (!edge) continue;
            G.Dist[m] = rootCost(m);
            heap.Enqueue(m, G.Dist[m]);
        }
        var mv = new List<(int, float)>();
        while (heap.TryDequeue(out int m, out float d))
        {
            if (d > G.Dist[m]) continue;
            G.Order.Add(m);
            MovesM(m, mv);
            foreach (var (n, c) in mv)
            {
                float nd = d + c;
                if (nd < G.Dist[n]) { G.Dist[n] = nd; G.Par[n] = m; heap.Enqueue(n, nd); }
            }
        }
        return G;
    }

    // Chemin depuis la porte du gâteau (ou un point de départ) jusqu'à la macro-case m
    public List<Vector3> PathTo(int m, Vector2 from)
    {
        var G = GalleriesFrom(from);
        var chain = new List<Vector3>();
        int root = m;
        for (int c = m; c >= 0; c = G.Par[c]) { chain.Add(MPos(c)); root = c; }
        chain.Reverse();
        var res = Approach(from, root);
        res.AddRange(chain);
        return res;
    }

    // Chemin de m vers la sortie (l'entrée par laquelle on est arrivé), sans l'approche
    public List<Vector3> PathOut(int m, Vector2? toward = null)
    {
        var G = toward is { } tw ? GalleriesFrom(tw) : Galleries();
        var chain = new List<Vector3>();
        int start = m;
        if (float.IsPositiveInfinity(G.Dist[m]))
        {
            // case isolée du réseau (éboulement, carte pas encore à jour) : on rejoint à pied, par les
            // cases praticables voisines, la case reliée la plus proche — jamais à travers la pâte
            var par = new Dictionary<int, int> { [m] = -1 };
            var q = new Queue<int>(); q.Enqueue(m);
            var mv = new List<(int, float)>();
            int found = -1;
            while (q.Count > 0 && par.Count < 400)
            {
                int c = q.Dequeue();
                if (!float.IsPositiveInfinity(G.Dist[c])) { found = c; break; }
                MovesM(c, mv);
                foreach (var (n, _) in mv) if (!par.ContainsKey(n)) { par[n] = c; q.Enqueue(n); }
                // le voisin juste en dessous / au-dessus (une montée inachevée)
                foreach (int n in new[] { c - MA, c + MA }) if (n >= 0 && n < MA * Layers && !par.ContainsKey(n) && StandableM(n)) { par[n] = c; q.Enqueue(n); }
            }
            if (found < 0) return null; // emmurée : il faut se dégager en creusant (EscapeStep)
            var pre = new List<Vector3>();
            for (int c = found; c != m && c >= 0; c = par[c]) pre.Add(MPos(c));
            pre.Reverse();
            chain.Add(MPos(m)); chain.AddRange(pre);
            start = found;
            chain.RemoveAt(chain.Count - 1);
        }
        for (int c = start; c >= 0; c = G.Par[c]) chain.Add(MPos(c));
        return chain;
    }

    // Chemin local entre deux macro-cases (dans les galeries)
    public List<Vector3> LocalPath(int a, int b)
    {
        if (a == b) return new List<Vector3> { MPos(b) };
        var prev = new Dictionary<int, int> { [a] = -1 };
        var q = new Queue<int>();
        q.Enqueue(a);
        var mv = new List<(int, float)>();
        int guard = 0;
        while (q.Count > 0 && guard++ < 4000)
        {
            int m = q.Dequeue();
            if (m == b) break;
            MovesM(m, mv);
            foreach (var (n, _) in mv) if (!prev.ContainsKey(n)) { prev[n] = m; q.Enqueue(n); }
        }
        if (!prev.ContainsKey(b)) return null;
        var path = new List<Vector3>();
        for (int c = b; c >= 0; c = prev[c]) path.Add(MPos(c));
        path.Reverse();
        return path;
    }

    // ───── Creusement ─────
    public DigStep MakeStep(int P, int d, char kind)
    {
        int y = MY(P), I = MI(P), K = MK(P);
        var (di, dk) = Nb4[d];
        int I2 = I + di, K2 = K + dk;
        if (I2 < 0 || K2 < 0 || I2 >= MW || K2 >= MW) return null;
        int ty = kind == 'U' ? y + 1 : kind == 'D' ? y - 1 : y;
        if (ty < 0 || ty + (Relax >= 2 ? 1 : 3) >= Layers) return null; // 2 blocs pleins au-dessus du tunnel (sauf par le dessus)
        int T = I2 + K2 * MW + ty * MA;
        foreach (int c in MCols(T))
        {
            if (H0[c] == 0) { if (ty > 0) return null; continue; }
            if (ty > 0 && Cake[c + (ty - 1) * Area] == 0) return null; // sol plein
        }
        var p = MPos(P);
        int vert = kind == 'D' ? -1 : 1;
        var R = new List<int>();
        // la section de la fève se creuse comme les autres, bloc de la fève compris (elle en sort)
        foreach (int b in MBlocks(T)) if (Cake[b] != 0) R.Add(b);
        if (R.Count == 0) return null;
        R.Sort((a, b) =>
        {
            float da = new Vector2(IdxI(a) - IX + 0.5f - p.X, IdxK(a) - IZ + 0.5f - p.Z).Length();
            float db = new Vector2(IdxI(b) - IX + 0.5f - p.X, IdxK(b) - IZ + 0.5f - p.Z).Length();
            float s = (MathF.Round(da) - MathF.Round(db)) * 1000 + (IdxY(a) - IdxY(b)) * vert * 100 + (da - db);
            return s.CompareTo(0);
        });
        return new DigStep { From = P, T = T, Dir = d, Kind = kind, R = R, Cake = Index };
    }

    bool Diggable(int b, bool entrance, HashSet<int> stepSet)
    {
        int c = b % Area, y = b / Area;
        if (Relax >= 2) return H0[c] != 0 && Cake[b] != 0; // plus le choix : tout se mange, croûte comprise
        if (H0[c] == 0 || Cake[b] == 0 || Crust[b] != 0) return false;
        if (!stepSet.Contains(b + Area) && (y + 2 >= Layers || Cake[b + Area] == 0 || Cake[b + 2 * Area] == 0)) return false;
        if (y >= 2 && !entrance)
        {
            int i = c % CW, k = c / CW;
            foreach (var (di, dk) in Nb4)
            {
                int i2 = i + di, k2 = k + dk;
                if (i2 < 0 || k2 < 0 || i2 >= CW || k2 >= CW) return false;
                int n = i2 + k2 * CW;
                if (H0[n] == 0 || ColTop[n] < y + 1) return false;
            }
        }
        return true;
    }

    HashSet<int> _reserved = new();

    bool MOpen(int m)
    {
        if (m < 0 || m >= MA * Layers) return false;
        if (_reserved.Count > 0) foreach (int b in MBlocks(m)) if (_reserved.Contains(b)) return true;
        if (IsPlateM(m) || !StandableM(m)) return false;
        int y = MY(m);
        foreach (int c in MCols(m)) if (H0[c] != 0 && ColTop[c] > y + 1) return true;
        return false;
    }

    // Pas de salle : la nouvelle section touche au plus une autre galerie, pas collée au couloir
    // d'où l'on vient, et pas d'empilement juste au-dessus ou en dessous d'une autre galerie.
    bool ShapeOk(DigStep step)
    {
        int T = step.T, P = step.From;
        int ty = MY(T), r = T % MA, I = MI(T), K = MK(T);
        int pr = P % MA, PI = MI(P), PK = MK(P), Py = MY(P);
        int touching = 0;
        foreach (var (di, dk) in Nb4)
        {
            int I2 = I + di, K2 = K + dk;
            if (I2 < 0 || K2 < 0 || I2 >= MW || K2 >= MW) continue;
            int c2 = I2 + K2 * MW;
            if (c2 == pr) continue;
            bool nearP = Math.Abs(I2 - PI) <= 1 && Math.Abs(K2 - PK) <= 1;
            bool open = false;
            for (int dy = -1; dy <= 1 && !open; dy++)
            {
                int yy = ty + dy;
                if (yy < 0 || yy >= Layers - 1) continue;
                if (nearP && yy == Py && ty != Py) continue;
                if (MOpen(c2 + yy * MA)) open = true;
            }
            if (!open) continue;
            if (nearP) return false;
            if (++touching > 1) return false;
        }
        foreach (int dy in new[] { -2, 2 })
        {
            int yy = ty + dy;
            if (yy < 0 || yy >= Layers - 1) continue;
            if (r != pr && MOpen(r + yy * MA)) return false;
        }
        return true;
    }

    // 'ok', 'feve' (la fève est dans le chemin) ou null
    public string CheckStep(DigStep step, ref int budget)
    {
        bool entrance = IsPlateM(step.From);
        // entrées espacées : pas d'arcade d'ouvertures côte à côte au bord du gâteau (sauf enragées)
        if (entrance && Relax < 2)
        {
            int I = MI(step.T), Kk = MK(step.T);
            for (int dk = -2; dk <= 2; dk++)
            for (int di = -2; di <= 2; di++)
            {
                int I2 = I + di, K2 = Kk + dk;
                if ((di == 0 && dk == 0) || I2 < 0 || K2 < 0 || I2 >= MW || K2 >= MW) continue;
                int c = I2 + K2 * MW;
                if (c == step.From) continue;
                for (int yy = 0; yy <= 1; yy++) if (!IsPlateM(c) && MOpen(c + yy * MA)) return null;
            }
        }
        var set = new HashSet<int>(MBlocks(step.T));
        foreach (int b in step.R) if (!Diggable(b, entrance, set)) return null;
        if (Relax < 2 && !ShapeOk(step)) return null;
        if (budget-- <= 0) return null;
        // niveau 0 : rien ne s'écroule et les plafonds gardent une marge ; niveau 1 : sans marge ;
        // niveau 2 : on mange par le dessus, quitte à provoquer des éboulements
        if (Relax < 2 && WouldCollapse(step.R, Relax == 0)) return null;
        return "ok";
    }

    // sections réservées : celles en cours ET celles qu'une ouvrière reviendra finir
    public void ReserveFaces(IEnumerable<Worker> workers, Worker except)
    {
        _reserved.Clear();
        foreach (var w in workers)
        {
            if (w == except) continue;
            if (w.At == Index && w.Step != null && w.State == WorkerState.Dig) foreach (int b in w.Step.R) _reserved.Add(b);
            if (w.SiteCake == Index && w.Resume != null) foreach (int b in w.Resume.R) _reserved.Add(b);
            if (w.Cake == Index && w.Step != null && w.State == WorkerState.Travel) foreach (int b in w.Step.R) _reserved.Add(b);
        }
    }
    public bool IsReserved(int b) => _reserved.Contains(b);

    // Vider ces blocs ferait-il s'écrouler quelque chose ? (simulation à blanc)
    // margin : on refuse aussi d'affaiblir un plafond sous une solidité de 2.
    byte[] _dry;
    public bool WouldCollapse(List<int> list, bool margin)
    {
        if (list.Count == 0) return false;
        const int K = Support;
        var cake = Cake; var st = Stab;
        int iMin = CW, iMax = -1, kMin = CW, kMax = -1, y0 = Layers;
        foreach (int b in list)
        {
            int i = IdxI(b), k = IdxK(b), y = IdxY(b);
            iMin = Math.Min(iMin, i); iMax = Math.Max(iMax, i); kMin = Math.Min(kMin, k); kMax = Math.Max(kMax, k); y0 = Math.Min(y0, y);
        }
        int i0 = Math.Max(0, iMin - K), i1 = Math.Min(CW - 1, iMax + K), k0 = Math.Max(0, kMin - K), k1 = Math.Min(CW - 1, kMax + K);
        int W = i1 - i0 + 1, D = k1 - k0 + 1;
        int size = W * D * (Layers - y0);
        if (_dry == null || _dry.Length < size) _dry = new byte[size * 2];
        int n = 0;
        for (int y = y0; y < Layers; y++) for (int kk = k0; kk <= k1; kk++) for (int ii = i0; ii <= i1; ii++) _dry[n++] = st[ii + kk * CW + y * Area];
        var vals = new uint[list.Count];
        for (int q = 0; q < list.Count; q++) { vals[q] = cake[list[q]]; cake[list[q]] = 0; }
        bool falls = false;
        for (int y = y0; y < Layers && !falls; y++)
        {
            int L = y * Area;
            for (int kk = k0; kk <= k1; kk++) for (int ii = i0; ii <= i1; ii++)
            {
                int c = ii + kk * CW + L;
                st[c] = (byte)(cake[c] == 0 ? 0 : y == 0 ? K : cake[c - Area] != 0 ? st[c - Area] : 0);
            }
            for (int pass = 0; pass < K; pass++)
                for (int kk = k0; kk <= k1; kk++) for (int ii = i0; ii <= i1; ii++)
                {
                    int c = ii + kk * CW + L;
                    if (cake[c] == 0 || st[c] >= K) continue;
                    int s2 = st[c];
                    if (ii > 0 && cake[c - 1] != 0 && st[c - 1] - 1 > s2) s2 = st[c - 1] - 1;
                    if (ii < CW - 1 && cake[c + 1] != 0 && st[c + 1] - 1 > s2) s2 = st[c + 1] - 1;
                    if (kk > 0 && cake[c - CW] != 0 && st[c - CW] - 1 > s2) s2 = st[c - CW] - 1;
                    if (kk < CW - 1 && cake[c + CW] != 0 && st[c + CW] - 1 > s2) s2 = st[c + CW] - 1;
                    st[c] = (byte)s2;
                }
            for (int kk = k0; kk <= k1 && !falls; kk++) for (int ii = i0; ii <= i1; ii++)
            {
                int c = ii + kk * CW + L;
                if (cake[c] != 0 && st[c] == 0) { falls = true; break; }
            }
        }
        if (margin && !falls)
        {
            var set = new HashSet<int>(list);
            foreach (int b in list)
            {
                int up = b + Area;
                if (up < cake.Length && !set.Contains(up) && cake[up] != 0 && st[up] < 2) { falls = true; break; }
            }
        }
        for (int q = 0; q < list.Count; q++) cake[list[q]] = vals[q];
        n = 0;
        for (int y = y0; y < Layers; y++) for (int kk = k0; kk <= k1; kk++) for (int ii = i0; ii <= i1; ii++) st[ii + kk * CW + y * Area] = _dry[n++];
        return falls;
    }

    // Choix d'un chantier pour une ouvrière : proche, dans SA direction de prédilection (une
    // exploratrice préfère les fronts lointains), en évitant la foule, avec un peu de hasard.
    // On essaie d'abord de prolonger sa propre galerie.
    public DigStep PlanFor(Worker w, IEnumerable<Worker> all)
    {
        ReserveFaces(all, w);
        int budget = 30;
        var rng = Game.Rng;
        // 0. un éboulement bouche le réseau : on déblaie (manger ce qui bloque le passage)
        if (!NoClear && ClearRubble(w) is { } clear) return clear;
        // 1. prolonger sa galerie
        int P0 = w.P >= 0 ? w.P : (w.SiteCake == Index ? w.Site : -1);
        if (P0 >= 0 && (StandableM(P0) || IsPlateM(P0)) && !float.IsPositiveInfinity(Galleries().Dist[P0]) && (Relax >= 2 || !NeedsDetour(P0, BaseOf(w))))
        {
            var kinds = StepKinds(MY(P0), PrefOf(w), rng);
            // près du bord, la galerie file vers le cœur du gâteau ; à l'intérieur elle garde son cap
            var pp = MPos(P0);
            float rx = pp.X - Cx, rz = pp.Z - Cz;
            int inward = 0; float bestDot = float.MaxValue;
            for (int d = 0; d < 4; d++) { float dt = Nb4[d].di * rx + Nb4[d].dk * rz; if (dt < bestDot) { bestDot = dt; inward = d; } }
            int straight = MathF.Sqrt(rx * rx + rz * rz) > 27 || w.LastDir < 0 ? inward : w.LastDir;
            var dirs = new List<int> { straight };
            // de temps en temps un virage : les galeries serpentent au lieu de filer tout droit
            if (rng.NextDouble() < 0.25) dirs.Insert(0, TurnOf(straight, rng)); // virages fréquents : les galeries partent dans tous les sens
            foreach (int d in DirsFrom(P0, rng)) if (!dirs.Contains(d)) dirs.Add(d);
            foreach (int d in dirs)
            foreach (char k in kinds)
            {
                var s = MakeStep(P0, d, k);
                if (s == null) continue;
                var r = CheckStep(s, ref budget);
                if (r == "ok") return s;
                if (r == "feve") FeveHint = true;
            }
        }
        // 2. un chantier ailleurs dans le réseau, vu depuis SA base (la porte côté nid, ou l'entrepôt
        // où elle dépose) : elle creuse du côté d'où elle revient, pas forcément du côté du nid
        var bp = BaseOf(w);
        var G = GalleriesFrom(bp);
        var busy = new List<Vector3>();
        foreach (var o in all)
        {
            if (o == w) continue;
            if (o.Step != null && (o.At == Index || o.Cake == Index)) busy.Add(MPos(o.Step.T));
            else if (o.Resume != null && o.SiteCake == Index) busy.Add(MPos(o.Resume.T));
        }
        var cand = new List<(int m, float score)>();
        // la direction de prédilection est un écart (±43°) autour de la porte du gâteau (côté nid) :
        // on ne fait pas le tour du gâteau pour creuser de l'autre côté
        float home = MathF.Atan2(bp.X - Cx, bp.Y - Cz) + (w.Sector / MathF.Tau - 0.5f) * 2.6f; // ±75° : chacune sa direction
        var door2 = bp;
        foreach (int m in G.Order)
        {
            // jamais de détour par l'assiette : on n'utilise que les entrées visibles depuis la porte ;
            // l'autre côté du gâteau se rejoint par l'INTÉRIEUR, en creusant
            int root = m; while (G.Par[root] >= 0) root = G.Par[root];
            // (enragées : tout ce qui reste compte, quitte à faire le tour du gâteau)
            if (Relax < 2 && Approach(door2, root).Count > 0) continue;
            var p = MPos(m);
            float ang = MathF.Atan2(p.X - Cx, p.Z - Cz);
            float da = MathF.Abs(Mathf.Wrap(ang - home, -MathF.PI, MathF.PI));
            float crowd = 0;
            foreach (var b in busy) { float dd = MathF.Abs(b.X - p.X) + MathF.Abs(b.Z - p.Z) + MathF.Abs(b.Y - p.Y); if (dd < 10) crowd += 10 - dd; }
            float d = G.Dist[m];
            // on vise les fronts qui s'enfoncent vers le CENTRE et vers SA hauteur ; ouvrir une
            // nouvelle entrée au bord coûte cher (sinon : des trous tout autour, le cœur intact)
            // dispersion : SA direction autour du gâteau (secteur), SA hauteur, loin des autres chantiers ;
            // la distance compte peu (sinon tout se tasse près de l'entrée), beaucoup de hasard
            float dy = MathF.Abs(MY(m) - PrefOf(w)) * (Relax >= 2 ? 3 : 1);
            float score = (w.Explorer ? -d * 0.15f : d * 0.35f) + dy * 2f + da * 9 + crowd * 4f + (float)rng.NextDouble() * 12 + (IsPlateM(m) ? 30 : 0);
            // sac entamé : elle enchaîne sur un chantier PROCHE d'elle (pas de traversée du gâteau
            // entre deux sections) ; sac vide, elle choisit librement (dispersion)
            if (w.Load > 0 && w.At == Index) score += p.DistanceTo(w.Pos) * 2.5f;
            cand.Add((m, score));
        }
        cand.Sort((a, b) => a.score.CompareTo(b.score));
        int tried = 0;
        float now = (float)Game.PlayTime;
        foreach (var (m, _) in cand)
        {
            if (budget <= 0) break;
            // culs-de-sac déjà explorés récemment : on cherche plus loin
            if (_dead.TryGetValue(m, out float until) && until > now) continue;
            if (tried++ > 80) break;
            var kinds = StepKinds(MY(m), PrefOf(w), rng);
            bool budgetHit = false;
            foreach (int d in DirsFrom(m, rng))
            foreach (char k in kinds)
            {
                if (budget <= 0) { budgetHit = true; break; }
                var s = MakeStep(m, d, k);
                if (s == null) continue;
                var r = CheckStep(s, ref budget);
                if (r == "ok") return s;
                if (r == "feve") FeveHint = true;
            }
            // rien de creusable depuis cette case : on l'oublie un moment (le gâteau change lentement)
            if (!budgetHit) _dead[m] = now + 45 + (float)rng.NextDouble() * 30;
        }
        return null;
    }

    readonly Dictionary<int, float> _dead = new();

    // ───── Plus rien de creusable sans risque ? ─────
    // Un gâteau très entamé finit par ne plus offrir de section qui respecte les règles
    // (solidité, croûte, pas de salle) : toutes les ouvrières s'arrêteraient. Si aucun chantier
    // n'est trouvé pendant un moment, on assouplit les règles, par paliers. Le palier 2 est
    // l'« enragement » : propre à CE gâteau (ses ouvrières seulement) et jusqu'à ce qu'il soit
    // mangé entièrement ; elles creusent par le dessus, sont toujours sonnées par les éboulements
    // mais ne perdent plus leurs miettes (ni vitesse ni force en plus).
    public bool Enraged => Relax >= 2;
    public int Relax;
    double _failSince = -1;
    int PrefOf(Worker w) => Relax >= 2 ? Layers - 2 : w.PrefY; // par le dessus : on vise les couches hautes
    public void NotePlan(bool ok)
    {
        double now = Game.PlayTime;
        if (ok) { _failSince = -1; return; }
        if (_failSince < 0) { _failSince = now; return; }
        if (now - _failSince < 25 || Relax >= 2) return;
        Relax++;
        _failSince = now;
        _dead.Clear();
        MarkGal(); _rubbleT = -1;
        if (Relax == 2) Game.Emit(new GameEvent("enraged") { Cake = Index });
    }
    public static bool NoClear; // développement : --noclear (comparaison)

    // ouvrière emmurée (galerie bouchée par un éboulement) : la section à creuser pour rejoindre
    // le réseau, par le plus court chemin à travers la pâte
    public DigStep EscapeStep(int m)
    {
        var G = Galleries();
        var par = new Dictionary<int, int> { [m] = -1 };
        var q = new Queue<int>(); q.Enqueue(m);
        int found = -1;
        while (q.Count > 0 && par.Count < 3000)
        {
            int c = q.Dequeue();
            if (c != m && !float.IsPositiveInfinity(G.Dist[c])) { found = c; break; }
            int y = MY(c), I = MI(c), K = MK(c);
            foreach (var (di, dk) in Nb4)
            {
                int I2 = I + di, K2 = K + dk;
                if (I2 < 0 || K2 < 0 || I2 >= MW || K2 >= MW) continue;
                foreach (int yy in new[] { y, y - 1 })
                {
                    if (yy < 0 || yy + 2 >= Layers) continue;
                    int n = I2 + K2 * MW + yy * MA;
                    if (!par.ContainsKey(n)) { par[n] = c; q.Enqueue(n); }
                }
            }
        }
        if (found < 0) return null;
        // le chemin de m vers le réseau : on creuse la PREMIÈRE case encore pleine (les cases déjà
        // vides du trajet se traversent à pied) — jamais d'aller-retour entre deux cases vides
        var chain = new List<int>();
        for (int c = found; c != m; c = par[c]) chain.Add(c);
        chain.Reverse();
        int prev = m;
        foreach (int first in chain)
        {
            var R = new List<int>();
            foreach (int b in MBlocks(first)) if (Cake[b] != 0) R.Add(b);
            if (R.Count == 0) { prev = first; continue; }
            R.Sort((a, b) => CakeSim.IdxY(b).CompareTo(CakeSim.IdxY(a)));
            int dir = 0;
            for (int d = 0; d < 4; d++) if (MI(first) - MI(prev) == Nb4[d].di && MK(first) - MK(prev) == Nb4[d].dk) dir = d;
            return new DigStep { From = prev, T = first, Dir = dir, Kind = 'F', R = R, Cake = Index, Via = prev != m ? chain.GetRange(0, chain.IndexOf(prev) + 1) : null };
        }
        return null;
    }

    // ───── Éboulis ─────
    // Une case de galerie bouchée par des blocs tombés (ou entamés) : sol plein, plafond intact,
    // il suffit de la déblayer pour rouvrir le passage.
    bool RubbleOnly(int n)
    {
        if (n < 0 || n >= MA * Layers) return false;
        int y = MY(n);
        if (y + 3 >= Layers) return false;
        bool any = false;
        foreach (int b in MBlocks(n))
        {
            uint v = Cake[b];
            if (v == 0) continue;
            if (b == Feve) continue; // on déblaie autour de la fève
            if (v >= CpvL) return false;
            any = true;
        }
        if (!any) return false;
        foreach (int c in MCols(n))
        {
            if (H0[c] == 0) { if (y > 0) return false; continue; }
            if (y > 0 && Cake[c + (y - 1) * Area] == 0) return false;
            if (Relax < 2 && ColTop[c] <= y + 1) return false; // plus de plafond : un cratère, on passe par-dessus
        }
        return true;
    }

    // fronts d'éboulis atteignables (recalculés avec la carte des galeries)
    readonly List<(int from, int d, char kind)> _rubble = new();
    double _rubbleT = -1;
    List<(int from, int d, char kind)> RubbleFronts()
    {
        var G = Galleries();
        if (_rubbleT == G.Time) return _rubble;
        _rubbleT = G.Time;
        _rubble.Clear();
        var seen = new HashSet<int>();
        foreach (int m in G.Order)
        {
            int y = MY(m), I = MI(m), K = MK(m);
            for (int d = 0; d < 4; d++)
            {
                int I2 = I + Nb4[d].di, K2 = K + Nb4[d].dk;
                if (I2 < 0 || K2 < 0 || I2 >= MW || K2 >= MW) continue;
                foreach (var (dy, kind) in new[] { (0, 'F'), (1, 'U'), (-1, 'D') })
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= Layers) continue;
                    int n = I2 + K2 * MW + yy * MA;
                    if (!float.IsPositiveInfinity(G.Dist[n]) || seen.Contains(n) || !RubbleOnly(n)) continue;
                    seen.Add(n);
                    _rubble.Add((m, d, kind));
                }
            }
        }
        return _rubble;
    }

    // la galerie est bouchée par un éboulement : déblayer le plus proche (de la place de l'ouvrière
    // ou de son chantier), avant de creuser ailleurs
    DigStep ClearRubble(Worker w)
    {
        var fronts = RubbleFronts();
        if (fronts.Count == 0) return null;
        var G = Galleries();
        Vector3 me = w.SiteCake == Index && w.Site >= 0 ? MPos(w.Site) : w.At == Index && w.P >= 0 ? MPos(w.P) : MPos(fronts[0].from);
        DigStep best = null; float bestS = float.MaxValue;
        foreach (var (from, d, kind) in fronts)
        {
            var p = MPos(from);
            float score = p.DistanceTo(me) + G.Dist[from] * 0.05f + (float)Game.Rng.NextDouble() * 4;
            if (score >= bestS) continue;
            var s = MakeStep(from, d, kind);
            if (s == null || s.R.Count == 0 || s.R.Exists(IsReserved)) continue;
            if (WouldCollapse(s.R, false)) continue;
            s.Kind = 'C';
            best = s; bestS = score;
        }
        return best;
    }

    bool NeedsDetour(int m, Vector2 from)
    {
        var G = GalleriesFrom(from);
        int root = m; while (G.Par[root] >= 0) root = G.Par[root];
        return Approach(from, root).Count > 0;
    }

    // d'où revient l'ouvrière : l'entrepôt de ce gâteau où elle dépose, sinon la porte côté nid
    Vector2 BaseOf(Worker w) => !DoorOnly && w.Base is { } b && w.BaseCake == Index ? b : Layout.Door(Index);

    // NB4 : 0 +x, 1 −x, 2 +z, 3 −z → virage à gauche ou à droite
    static int TurnOf(int d, Random rng)
    {
        if (d < 0) return rng.Next(4);
        int[] left = { 3, 2, 0, 1 }, right = { 2, 3, 1, 0 };
        return rng.Next(2) == 0 ? left[d] : right[d];
    }

    // chaque ouvrière vise SA hauteur (prefY) : elle monte tant qu'elle est en dessous, redescend
    // au-dessus ; à sa hauteur, elle file tout droit en ondulant un peu
    static List<char> StepKinds(int y, int prefY, Random rng)
    {
        var kinds = new List<char> { 'F', 'F' };
        if (y < prefY) kinds.AddRange(new[] { 'U', 'U', 'U', 'U', 'F' });
        else if (y > prefY) kinds.AddRange(new[] { 'D', 'D', 'D', 'F' });
        else kinds.AddRange(new[] { 'F', 'F', 'U', 'D' });
        for (int n = kinds.Count - 1; n > 0; n--) { int j = rng.Next(n + 1); (kinds[n], kinds[j]) = (kinds[j], kinds[n]); }
        var res = new List<char>();
        foreach (var k in kinds) if (!res.Contains(k)) res.Add(k);
        foreach (var k in "FUD") if (!res.Contains(k)) res.Add(k);
        return res;
    }

    // près du bord, on s'enfonce d'abord vers le centre
    List<int> DirsFrom(int P, Random rng)
    {
        var p = MPos(P);
        float x = p.X - Cx, z = p.Z - Cz, r = MathF.Sqrt(x * x + z * z);
        var dirs = new List<int> { 0, 1, 2, 3 };
        for (int n = 3; n > 0; n--) { int j = rng.Next(n + 1); (dirs[n], dirs[j]) = (dirs[j], dirs[n]); }
        if (r > 26) dirs.Sort((a, b) => (Nb4[a].di * x + Nb4[a].dk * z).CompareTo(Nb4[b].di * x + Nb4[b].dk * z));
        return dirs;
    }

    // Grue : le sommet de colonne qui minimise distance horizontale + profondeur sous le haut
    static readonly Dictionary<float, List<(int di, int dk, float d)>> _cols = new();
    public int PickTopTarget(int bx, int bz, float range)
    {
        if (!_cols.TryGetValue(range, out var cols))
        {
            cols = new();
            int R = (int)MathF.Ceiling(range);
            for (int di = -R; di <= R; di++) for (int dk = -R; dk <= R; dk++)
            {
                float d2 = di * di + dk * dk;
                if (d2 <= range * range) cols.Add((di, dk, MathF.Sqrt(d2)));
            }
            cols.Sort((a, b) => a.d.CompareTo(b.d));
            _cols[range] = cols;
        }
        int gi = bx + IX, gk = bz + IZ, best = -1, bestTop = -1;
        float bestScore = float.MaxValue;
        foreach (var (di, dk, d) in cols)
        {
            int i = gi + di, k = gk + dk;
            if (i < 0 || k < 0 || i >= CW || k >= CW) continue;
            int c = i + k * CW, t = ColTop[c];
            if (t < 0) continue;
            float score = d + (Layers - 1 - t) * 1.3f;
            if (score < bestScore) { bestScore = score; best = c; bestTop = t; }
        }
        return best < 0 ? -1 : best + bestTop * Area;
    }
}
