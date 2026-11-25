// ─────────────────────────────────────────────────────────────
// Les ouvrières : elles sortent du nid, filent par l'autoroute
// jusqu'à leur gâteau, creusent un vrai réseau de galeries 2 × 2,
// rapportent leur charge au point de dépôt le plus rapide (nid ou
// entrepôt), puis retournent prolonger LEUR galerie.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Sim;

public enum WorkerState { Rest, Travel, Dig, Stunned }

public sealed class Worker
{
    public int Id;
    public int Cake;              // gâteau d'affectation (-1 : au repos)
    public int At = -1;           // gâteau où elle se trouve (-1 : dehors / au nid)
    public Vector3 Pos, PrevPos;
    public float Yaw;
    public WorkerState State = WorkerState.Rest;
    public List<Vector3> Path;
    public int PathI;
    public float PathSpeedMul = 1;
    public Action OnArrive;
    public long Load;
    public double LoadV;
    public int LoadC = -1;        // gâteau d'où vient sa charge (couleur des miettes)
    public Vector2? Base;         // d'où elle repart : l'entrepôt où elle a déposé (null : le nid)
    public int BaseCake = -1;
    public int P = -1;            // macro-case où elle se tient (dans le gâteau At)
    public int LastDir = -1;
    // mémoire de SA galerie : le chantier où elle reviendra après chaque dépôt
    public int Site = -1, SiteCake = -1;
    public int PrefY;
    public string Tag = "";
    public bool Escaping;          // emmurée : se dégage en creusant
    public int Blocked;            // trajets interrompus par un éboulis (de suite)
    public DigStep Resume;        // section commencée, à finir au prochain voyage
    public DigStep Step;
    public int StepI;
    public double EatAcc;
    public float Sector;          // direction de prédilection autour du gâteau
    public bool Explorer;
    public float Stun;
    public bool Dropping;          // trajet en cours = aller déposer
    public float BiteT;           // animation
    public float IdleT;
    public bool Moving;
    public int Store = -1;        // entrepôt visé (-1 : le nid)
}

public sealed partial class GameState
{
    public readonly List<Worker> Workers = new();
    public int[] Alloc = new int[NbCakes];   // ouvrières voulues par gâteau
    public int RecruitTo;                    // gâteau des nouvelles recrues (-1 : repos)
    int _nextWorkerId = 1;
    public int PlanOk, PlanNull; // statistiques (développement)

    public int WantedWorkers => UpgLvl["recruit"];
    public int Resting => Math.Max(0, WantedWorkers - Alloc.Sum());

    public (long load, double rate, float speed) WStats()
    {
        int t = UpgLvl["train"];
        return (Fx.WLoad(t), Fx.WRate(t), Fx.WSpeed(t));
    }

    // ───── Répartition (gestionnaire) ─────
    public void SyncWorkers()
    {
        // nombre total : niveau de « Recruter »
        while (Workers.Count < WantedWorkers)
        {
            var w = new Worker
            {
                Id = _nextWorkerId++, Cake = -1,
                Sector = (float)(Rng.NextDouble() * Math.Tau), Explorer = Rng.Next(4) == 0, PrefY = Rng.Next(0, 8),
            };
            float a = (float)(Rng.NextDouble() * Math.Tau);
            w.Pos = w.PrevPos = DomeAt(new Vector2(MathF.Cos(a), MathF.Sin(a)), 0.1f);
            Workers.Add(w);
            if (Alloc.Sum() < WantedWorkers && RecruitTo >= 0 && Cakes[RecruitTo].Unlocked) Alloc[RecruitTo]++;
        }
        NormalizeAlloc();
        // chaque ouvrière reçoit un gâteau selon la répartition voulue
        var have = new int[NbCakes];
        foreach (var w in Workers) if (w.Cake >= 0) have[w.Cake]++;
        foreach (var w in Workers)
            if (w.Cake >= 0 && have[w.Cake] > Alloc[w.Cake]) { have[w.Cake]--; Reassign(w, -1); }
        foreach (var w in Workers)
        {
            if (w.Cake >= 0) continue;
            for (int k = 0; k < NbCakes; k++)
                if (have[k] < Alloc[k]) { have[k]++; Reassign(w, k); break; }
        }
    }

    void NormalizeAlloc()
    {
        for (int k = 0; k < NbCakes; k++) if (!Cakes[k].Unlocked) Alloc[k] = 0;
        int over = Alloc.Sum() - WantedWorkers;
        for (int k = NbCakes - 1; k >= 0 && over > 0; k--) { int t = Math.Min(over, Alloc[k]); Alloc[k] -= t; over -= t; }
    }

    void Reassign(Worker w, int cake)
    {
        if (w.Cake == cake) return;
        w.Cake = cake;
        // elle finit son voyage en cours (si elle porte quelque chose, elle va déposer)
        w.Site = -1; w.Resume = null; w.Base = null; w.BaseCake = -1;
        if (w.State == WorkerState.Dig) { w.Step = null; GoDrop(w); }
        else if (w.State == WorkerState.Rest) w.IdleT = 0;
    }

    public void MoveWorkers(int cake, int delta)
    {
        if (delta > 0)
        {
            for (int n = 0; n < delta; n++)
            {
                if (Resting > 0) { Alloc[cake]++; continue; }
                // sinon : celles du gâteau le plus peuplé
                int from = -1;
                for (int k = 0; k < NbCakes; k++) if (k != cake && Alloc[k] > 0 && (from < 0 || Alloc[k] > Alloc[from])) from = k;
                if (from < 0) break;
                Alloc[from]--; Alloc[cake]++;
            }
        }
        else Alloc[cake] = Math.Max(0, Alloc[cake] + delta);
        SyncWorkers();
    }

    public void AllWorkersTo(int cake)
    {
        Array.Fill(Alloc, 0);
        Alloc[cake] = WantedWorkers;
        SyncWorkers();
    }

    public void SpreadWorkers()
    {
        var list = Enumerable.Range(0, NbCakes).Where(k => Cakes[k].Unlocked && Cakes[k].Remaining > 0).ToList();
        if (list.Count == 0) return;
        Array.Fill(Alloc, 0);
        for (int n = 0; n < WantedWorkers; n++) Alloc[list[n % list.Count]]++;
        SyncWorkers();
    }

    // ───── Déplacements ─────
    public static string PathTag = "";
    public Func<Vector3, bool> GhostCheck;
    public readonly Dictionary<string, int> Ghost = new(); // développement : ouvrières vues dans la pâte, par type de trajet
    void SetPath(Worker w, List<Vector3> path, float speedMul, Action onArrive)
    {
        w.Tag = PathTag;
        w.Dropping = false;
        path = OverDome(w, AvoidBuildings(path));
        w.Path = path; w.PathI = 0; w.PathSpeedMul = speedMul; w.OnArrive = onArrive;
        w.State = WorkerState.Travel;
        // développement (--wlog) : détour des trajets entrepôt ↔ chantier (longueur / vol d'oiseau)
        if (GhostCheck != null && path is { Count: > 1 } && (PathTag == "work-entree" && w.At >= 0 || PathTag == "drop" && w.Store >= 0))
        {
            float len = Layout.PathLength(path) + w.Pos.DistanceTo(path[0]);
            float direct = new Vector2(w.Pos.X - path[^1].X, w.Pos.Z - path[^1].Z).Length() + 0.01f;
            TripStats.Add(len / direct); TripLen.Add(len); TripLen.Add(len);
        }
    }
    public readonly List<float> TripStats = new(), TripLen = new();
    public readonly int[] DropStats = new int[2];
    public static bool DropEach; // développement : --dropeach (ancien comportement : dépôt après chaque section)
    public long UnloadSum; public int UnloadN;
    // choix du dépôt : 0 = à vol d'oiseau (--straightdrop), 1 = vrai trajet parmi les 2 plus proches
    // (par défaut), 2 = vrai trajet vers tous les entrepôts (--pathdrop)
    public static int DropMode = 1; // développement : dépôts au nid / en entrepôt

    // Le nid se rejoint comme la joueuse : on escalade le dôme de terre et on
    // dépose par le grand trou du sommet, puis on redescend par la pente.
    static readonly float[] DomeR = { 6.2f, 4.8f, 3.8f, 2.8f, 1.9f, 1.25f, 0.6f, 0.1f };
    static Vector3 DomeAt(Vector2 u, float r)
    {
        float x = Nest.X + u.X * r, z = Nest.Y + u.Y * r;
        return new Vector3(x, Render.Terrain.NestH(x - Nest.X, z - Nest.Y) + 0.03f, z);
    }
    static bool IsNestCenter(Vector3 q) => new Vector2(q.X - Nest.X, q.Z - Nest.Y).LengthSquared() < 0.5f && q.Y < 0.3f;
    static Vector2 Out(Vector3 from)
    {
        var d = new Vector2(from.X - Nest.X, from.Z - Nest.Y);
        return d.LengthSquared() < 1e-4f ? new Vector2(0, -1) : d.Normalized();
    }
    public static bool InNestDome(Vector3 p) => new Vector2(p.X - Nest.X, p.Z - Nest.Y).Length() < 6.4f && p.Y > 0.05f;

    List<Vector3> OverDome(Worker w, List<Vector3> path)
    {
        if (path == null) return null;
        var res = new List<Vector3>(path.Count + 16);
        // on part du dôme : redescendre d'abord du côté de la destination
        int first = 0;
        while (first < path.Count && IsNestCenter(path[first])) first++;
        if (InNestDome(w.Pos) && first < path.Count)
        {
            var u = Out(path[first]);
            float rNow = new Vector2(w.Pos.X - Nest.X, w.Pos.Z - Nest.Y).Length();
            foreach (var r in DomeR) if (r > rNow + 0.2f) res.Add(DomeAt(u, r));
            res.Sort((a, b) => new Vector2(a.X - Nest.X, a.Z - Nest.Y).Length().CompareTo(new Vector2(b.X - Nest.X, b.Z - Nest.Y).Length()));
        }
        for (int i = first; i < path.Count; i++)
        {
            var q = path[i];
            if (!IsNestCenter(q)) { res.Add(q); continue; }
            if (i < path.Count - 1) continue; // passage par le centre en cours de route : inutile
            // arrivée au nid : escalade jusqu'au trou
            var prev = res.Count > 0 ? res[^1] : w.Pos;
            var u = Out(prev);
            float rPrev = new Vector2(prev.X - Nest.X, prev.Z - Nest.Y).Length();
            foreach (var r in DomeR) if (r < rPrev - 0.2f) res.Add(DomeAt(u, r));
        }
        return res;
    }

    // Sur la couverture, un trajet qui traverse une machine la contourne (petit détour latéral).
    List<Vector3> AvoidBuildings(List<Vector3> path)
    {
        if (Buildings.Count == 0 || path == null) return path;
        var res = new List<Vector3>(path.Count + 8);
        Vector3? prev = null;
        foreach (var q in path)
        {
            if (prev is { } a && a.Y < 0.3f && q.Y < 0.3f)
            {
                var d = q - a; float len = d.Length();
                if (len > 0.5f)
                {
                    var dir = d / len; var side = new Vector3(-dir.Z, 0, dir.X);
                    for (float t = 0.5f; t < len; t += 0.5f)
                    {
                        var p = a + dir * t;
                        var b = At(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));
                        if (b == null || b.Type == "belt") continue;
                        // on passe à côté de la machine (du côté le plus libre)
                        var c = new Vector3(b.Cx, 0, b.Cz);
                        float sgn = side.Dot(p - c) >= 0 ? 1 : -1;
                        var off = c + side * sgn * (b.Ext + 0.95f);
                        res.Add(off - dir * (b.Ext + 0.4f)); res.Add(off + dir * (b.Ext + 0.4f));
                        t += 2 * b.Ext + 0.4f;
                    }
                }
            }
            res.Add(q);
            prev = q;
        }
        return res;
    }

    static List<Vector3> Reverse(List<Vector3> l) { var r = new List<Vector3>(l); r.Reverse(); return r; }

    // Point de dépôt le plus rapide : le nid, ou un entrepôt qui a de la place
    Vector3 DropPoint(Worker w, out int store)
    {
        // Recalculé à chaque dépôt. Compromis : on garde les 2 entrepôts (pas pleins) les plus proches
        // à vol d'oiseau, puis on compare leur VRAI trajet (galeries comprises) avec celui du nid.
        // Au plus 2 cartes d'entrepôt à tenir à jour, quel que soit leur nombre. Le trajet suivi
        // ensuite est toujours optimal (sortie et retour par l'entrée la plus proche).
        store = -1;
        var nest = new Vector3(Nest.X, 0, Nest.Y);
        CakeSim K = w.At >= 0 ? Cakes[w.At] : null;
        bool inside = K != null && w.P >= 0 && !float.IsPositiveInfinity(K.Galleries().Dist[w.P]);
        var me = new Vector2(w.Pos.X, w.Pos.Z);
        float Cost(Vector2 to, bool isNest)
        {
            if (inside && DropMode != 0)
            {
                // sortie par l'entrée la plus proche de la destination, puis la couverture
                if (isNest) return K.Galleries().Dist[w.P] + (K.Index > 0 ? Layout.PathLength(Layout.RouteFromNest(K.Index)) : 0);
                return K.GalleriesFrom(to).Dist[w.P];
            }
            if (isNest && K != null && K.Index > 0) return me.DistanceTo(Layout.Door(K.Index)) + Layout.PathLength(Layout.RouteFromNest(K.Index));
            return me.DistanceTo(to);
        }
        var best = nest;
        float bestC = Cost(Nest, true);
        if (StoreFor != null)
        {
            var cands = StoreFor(w);
            if (DropMode == 1 && cands.Count > 2)
            {
                cands.Sort((a, b) => me.DistanceSquaredTo(new Vector2(a.Item2.X, a.Item2.Z)).CompareTo(me.DistanceSquaredTo(new Vector2(b.Item2.X, b.Item2.Z))));
                cands.RemoveRange(2, cands.Count - 2);
            }
            foreach (var (id, pos) in cands)
            {
                float c = Cost(new Vector2(pos.X, pos.Z), false);
                if (c < bestC) { bestC = c; store = id; best = pos; }
            }
        }
        DropStats[store >= 0 ? 1 : 0]++;
        return best;
    }

    // renseigné par la logistique : les entrepôts candidats (pas pleins, près du gâteau) (id, position)
    public Func<Worker, List<(int, Vector3)>> StoreFor;
    public Action<Worker, int> DepositToStore;

    // emmurée ? on creuse d'abord jusqu'au réseau (jamais à travers la pâte en marchant)
    bool TryEscape(Worker w)
    {
        if (w.At < 0 || w.P < 0) return false;
        var K = Cakes[w.At];
        if (K.PathOut(w.P) != null) return false;
        var e = K.EscapeStep(w.P);
        if (e == null) return false;
        w.Step = e; w.StepI = 0; w.Escaping = true;
        if (e.Via is { Count: > 0 } via)
        {
            // d'abord les cases déjà vides du passage, à pied
            var pts = via.ConvertAll(K.MPos);
            PathTag = "evasion";
            SetPath(w, pts, 1, () => { w.P = e.From; w.State = WorkerState.Dig; });
        }
        else w.State = WorkerState.Dig;
        return true;
    }

    void GoDrop(Worker w)
    {
        if (TryEscape(w)) return;
        var path = new List<Vector3>();
        var target = DropPoint(w, out int store);
        w.Store = store;
        if (w.At >= 0 && w.P >= 0)
        {
            // vers un entrepôt : on sort par l'entrée la plus proche de LUI (pas par celle côté nid)
            var K = Cakes[w.At];
            path.AddRange(K.PathOut(w.P, store >= 0 ? new Vector2(target.X, target.Z) : null) ?? new List<Vector3>());
        }
        if (store < 0)
        {
            // le nid : sortir par la porte du gâteau puis l'autoroute
            if (w.At > 0)
            {
                var start = path.Count > 0 ? path[^1] : w.Pos;
                var K = Cakes[w.At];
                var door = Layout.Door(w.At);
                var appr = Reverse(K.Approach(door, K.MAt(start) >= 0 ? K.MAt(start) : 0));
                path.AddRange(appr);
                path.AddRange(Reverse(Layout.RouteFromNest(w.At)));
            }
            else
            {
                if (w.At == 0 && path.Count > 0)
                {
                    var K = Cakes[0];
                    path.AddRange(Reverse(K.Approach(Nest, K.MAt(path[^1]))));
                }
                path.Add(target);
            }
        }
        else
        {
            // de l'entrée jusqu'à l'entrepôt, en longeant le gâteau si besoin (jamais à travers)
            if (w.At >= 0 && path.Count > 0)
            {
                var K = Cakes[w.At];
                int me = K.MAt(path[^1]);
                if (me >= 0) path.AddRange(Reverse(K.Approach(new Vector2(target.X, target.Z), me)));
            }
            path.Add(target);
        }
        w.P = -1;
        PathTag = w.P >= 0 || path.Count > 0 ? (float.IsPositiveInfinity(w.At >= 0 && w.P >= 0 ? Cakes[w.At].Galleries().Dist[w.P] : 0) ? "drop-perdu" : "drop") : "drop";
        SetPath(w, path, 1, () => Unload(w));
        w.Dropping = true;
    }

    void Unload(Worker w)
    {
        if (w.Load > 0) { UnloadSum += w.Load; UnloadN++; }
        if (w.Load > 0)
        {
            if (w.Store >= 0 && DepositToStore != null) DepositToStore(w, w.Store);
            else
            {
                SellItem("crumb", w.LoadV > 0 ? w.LoadV : w.Load, 1, "workers");
                Emit(new GameEvent("workerSold") { Pos = w.Pos });
            }
        }
        w.Load = 0; w.LoadV = 0;
        // sa base devient l'entrepôt où elle vient de déposer (ses galeries partiront de ce côté)
        if (w.Store >= 0) { w.Base = new Vector2(w.Pos.X, w.Pos.Z); w.BaseCake = w.At; }
        else { w.Base = null; w.BaseCake = -1; }
        w.At = w.Store >= 0 ? w.At : -1;
        w.Store = -1;
        w.State = WorkerState.Rest;
        w.IdleT = 0.3f;
    }

    // Aller au gâteau d'affectation puis à un chantier
    void GoWork(Worker w)
    {
        if (w.Cake < 0) return;
        // encore dans les galeries d'un AUTRE gâteau : on en sort d'abord (jamais à travers la pâte)
        if (w.At >= 0 && w.At != w.Cake && w.P >= 0) { GoDrop(w); return; }
        var K = Cakes[w.Cake];
        if (K.Remaining <= 0) { if (w.Load > 0) GoDrop(w); return; }
        DigStep step = null;
        // 1. finir la section commencée au voyage précédent (si personne ne l'a reprise entre-temps)
        if (w.Resume != null && w.SiteCake == w.Cake)
        {
            var r = w.Resume; w.Resume = null;
            r.R.RemoveAll(b => K.Cake[b] == 0);
            K.ReserveFaces(Workers, w);
            if (r.Cake == K.Index && r.R.Count > 0 && K.StandableM(r.From) && !float.IsPositiveInfinity(K.Galleries().Dist[r.From]) && !r.R.Exists(K.IsReserved)) step = r;
        }
        if (w.SiteCake != w.Cake) w.Site = -1;
        // 2. sinon prolonger sa galerie, ou un nouveau chantier
        step ??= K.PlanFor(w, Workers);
        if (step == null) PlanNull++; else PlanOk++;
        K.NotePlan(step != null);
        if (step == null)
        {
            if (w.Load > 0) { GoDrop(w); return; } // plus rien à creuser par ici : elle rapporte ce qu'elle a
            w.IdleT = 2 + (float)Rng.NextDouble() * 2; // rien de creusable pour l'instant : on réessaie
            return;
        }
        var path = new List<Vector3>();
        if (w.At == w.Cake && w.P >= 0 && w.P != step.From)
        {
            var local = K.LocalPath(w.P, step.From);
            if (local != null) path.AddRange(local);
            else
            {
                var po = K.PathOut(w.P);
                if (po == null) { if (TryEscape(w)) return; po = new List<Vector3> { w.Pos }; }
                path.AddRange(po); path.AddRange(K.PathTo(step.From, new Vector2(path[^1].X, path[^1].Z)));
            }
        }
        else if (w.At != w.Cake || w.P < 0)
        {
            var from = new Vector2(w.Pos.X, w.Pos.Z);
            if (w.At != w.Cake && w.Cake > 0)
            {
                // l'autoroute depuis le nid (ou l'entrepôt d'où l'on part, en ligne droite vers la porte)
                path.AddRange(Layout.Direct(new Vector2(w.Pos.X, w.Pos.Z), Layout.Door(w.Cake), w.Cake));
                from = Layout.Door(w.Cake);
            }
            path.AddRange(K.PathTo(step.From, from));
        }
        w.Step = step; w.StepI = 0;
        w.LastDir = step.Dir;
        PathTag = w.At == w.Cake && w.P >= 0 ? "work-local" : "work-entree";
        // le gâteau est figé à l'instant du départ : si on la réaffecte en route, elle arrive quand même
        // sur CE gâteau (les indices du chantier lui appartiennent), puis Dig la renvoie proprement
        int cake = w.Cake;
        SetPath(w, path, 1, () => { w.At = cake; w.P = step.From; w.State = WorkerState.Dig; });
        if (path.Count == 0) { w.At = cake; w.P = step.From; w.State = WorkerState.Dig; }
    }

    // ───── Pas de simulation ─────
    void TickWorkers(float dt)
    {
        var ws = WStats();
        foreach (var w in Workers)
        {
            w.PrevPos = w.Pos;
            w.Moving = false;
            w.BiteT = MathF.Max(0, w.BiteT - dt * 4);
            if (w.Stun > 0)
            {
                w.Stun -= dt;
                if (w.Stun <= 0) AfterStun(w);
                continue;
            }
            switch (w.State)
            {
                case WorkerState.Rest:
                    w.IdleT -= dt;
                    if (w.IdleT > 0) break;
                    // sac plein (ou plus sur son gâteau) : elle rentre déposer ; sac entamé : elle enchaîne
                    // sur la section suivante — la capacité affichée est vraiment utilisée
                    if (w.Load > 0 && (DropEach || w.Load >= ws.load || w.Cake < 0 || w.At != w.Cake)) { GoDrop(w); break; }
                    if (w.Cake >= 0) GoWork(w);
                    else if (w.Load > 0) { GoDrop(w); break; }
                    else
                    {
                        // au repos : elle flâne autour de la fourmilière
                        float a = (float)(Rng.NextDouble() * Math.Tau), r = 7.5f + (float)Rng.NextDouble() * 4;
                        var p = new Vector3(Nest.X + MathF.Cos(a) * r, 0, Nest.Y + MathF.Sin(a) * r);
                        var path = new List<Vector3>();
                        if (w.At >= 0 || w.Pos.DistanceTo(new Vector3(Nest.X, 0, Nest.Y)) > 15) path.AddRange(Layout.Direct(new Vector2(w.Pos.X, w.Pos.Z), Nest, Math.Max(0, w.At)));
                        path.Add(p);
                        w.At = -1;
                        PathTag = "repos";
                        SetPath(w, path, 0.5f, () => { w.State = WorkerState.Rest; w.IdleT = 2 + (float)Rng.NextDouble() * 4; });
                    }
                    break;
                case WorkerState.Travel:
                    Follow(w, ws.speed, dt);
                    break;
                case WorkerState.Dig:
                    Dig(w, ws, dt);
                    break;
            }
        }
    }

    // fin d'étourdissement : elle reprend exactement ce qu'elle faisait (trajet ou creusage).
    // Seules exceptions : emmurée par l'éboulis → elle se dégage ; elle allait déposer mais a
    // perdu sa charge → elle retourne directement au chantier.
    void AfterStun(Worker w)
    {
        if (RubbleAt(w.Pos) is { } K)
        {
            // l'éboulis l'a recouverte : on repart de la case où elle est vraiment
            w.At = K.Index;
            int m = K.MAt(w.Pos);
            if (m >= 0) w.P = m;
            w.Path = null; w.OnArrive = null; w.Step = null; w.Escaping = false; w.Dropping = false;
            if (!TryEscape(w)) { w.State = WorkerState.Rest; w.IdleT = 0; }
            return;
        }
        if (w.State == WorkerState.Travel && w.Dropping && w.Load <= 0)
        {
            w.Path = null; w.OnArrive = null; w.Dropping = false; w.Store = -1;
            w.State = WorkerState.Rest; w.IdleT = 0;
        }
    }

    void Follow(Worker w, float speed, float dt)
    {
        if (w.Path == null || w.PathI >= w.Path.Count)
        {
            var cb = w.OnArrive; w.OnArrive = null;
            w.State = WorkerState.Rest; w.IdleT = 0;
            cb?.Invoke();
            return;
        }
        float budget = speed * w.PathSpeedMul * dt;
        while (budget > 0 && w.PathI < w.Path.Count)
        {
            var t = w.Path[w.PathI];
            // un éboulis est tombé sur le passage : elle s'arrête devant et replanifie
            // (déblayer, passer au-dessus ou à côté), au lieu de traverser la pâte ou de se figer
            if (w.PathI > 0 && w.Blocked < 4 && RubbleAt(t) is { } K)
            {
                w.Blocked++;
                w.At = K.Index;
                int here = K.MAt(w.Path[w.PathI - 1]);
                w.P = here >= 0 ? here : K.MAt(w.Pos);
                w.Path = null; w.OnArrive = null; w.Step = null;
                w.State = WorkerState.Rest; w.IdleT = 0.3f;
                return;
            }
            var d = t - w.Pos;
            float l = d.Length();
            if (l <= budget) { w.Pos = t; budget -= l; w.PathI++; }
            else { w.Pos += d / l * budget; budget = 0; }
            if (GhostCheck != null && GhostCheck(w.Pos)) { Ghost.TryGetValue(w.Tag, out int g); Ghost[w.Tag] = g + 1; }
            var h = new Vector2(d.X, d.Z);
            if (h.LengthSquared() > 1e-6f) w.Yaw = MathF.Atan2(h.X, h.Y);
            w.Moving = true;
        }
    }

    // la case d'un point de passage dans un gâteau est-elle (re)devenue pleine ?
    CakeSim RubbleAt(Vector3 t)
    {
        var K = CakeNear(t.X, t.Z, 0);
        if (K == null) return null;
        int y = Mathf.FloorToInt(t.Y + 0.1f);
        for (int dz = -1; dz <= 0; dz++)
        for (int dx = -1; dx <= 0; dx++)
        {
            int bx = Mathf.FloorToInt(t.X + 0.5f) + dx, bz = Mathf.FloorToInt(t.Z + 0.5f) + dz;
            if (K.SolidAt(bx, y, bz) && K.IdxW(bx, y, bz) != K.Feve) return K; // la fève se traverse
        }
        return null;
    }

    static void Remember(Worker w, DigStep s)
    {
        w.Resume = s; w.Site = s.From; w.SiteCake = w.At;
    }

    void Dig(Worker w, (long load, double rate, float speed) ws, float dt)
    {
        var K = Cakes[w.At];
        var s = w.Step;
        if (s == null) { w.State = WorkerState.Rest; w.IdleT = 0; return; }
        // changement d'affectation : on rentre déposer
        if (w.Cake != w.At || s.Cake != w.At) { w.Step = null; w.Escaping = false; GoDrop(w); return; }
        while (w.StepI < s.R.Count && K.Cake[s.R[w.StepI]] == 0) w.StepI++;
        if (w.StepI >= s.R.Count)
        {
            // section creusée : on n'y entre que si c'est une vraie case de galerie (sol plein, plafond) ;
            // sinon (montée sous un dessus trop mince…) on reste où l'on est
            K.InvalidateGalleries();
            if (!K.FeveHint && K.Feve >= 0 && !K.Found && System.Linq.Enumerable.Contains(K.MBlocks(s.T), K.Feve))
            { K.FeveHint = true; Emit(new GameEvent("feveHint") { Cake = K.Index, Pos = K.WPos(K.Feve) + new Vector3(0.5f, 0.5f, 0.5f) }); }
            w.Step = null;
            w.Resume = null; w.SiteCake = K.Index;
            bool enter = K.StandableM(s.T) || w.Escaping;
            if (w.Escaping)
            {
                // passage rouvert : elle y entre en marchant
                w.Escaping = false; w.Site = s.T;
                PathTag = "evasion";
                SetPath(w, new List<Vector3> { K.MPos(s.T) }, 1, () => { w.P = s.T; w.State = WorkerState.Rest; w.IdleT = 0.05f; });
                return;
            }
            w.Site = enter ? s.T : s.From;
            if (w.Load >= ws.load) { GoDrop(w); return; }
            if (!enter) { w.State = WorkerState.Rest; w.IdleT = 0.05f; return; }
            // on y avance en marchant (pas de saut)
            PathTag = "avance";
            SetPath(w, new List<Vector3> { K.MPos(s.T) }, 1, () => { w.P = s.T; w.State = WorkerState.Rest; w.IdleT = 0.05f; });
            return;
        }
        int b = s.R[w.StepI];
        // la tête contre la paroi : on se tourne vers le bloc
        var bc = K.WPos(b) + new Vector3(0, 0.5f, 0);
        var dir = bc - w.Pos;
        w.Yaw = MathF.Atan2(dir.X, dir.Z);
        w.Blocked = 0;
        w.EatAcc += ws.rate * dt;
        long want = (long)w.EatAcc;
        if (want <= 0) return;
        w.EatAcc -= want;
        long room = w.Escaping ? long.MaxValue / 4 : ws.load - w.Load; // se dégager : on avale tout (au pire, le surplus tombe)
        long taken = K.Eat(b, Math.Min(want, room), "worker");
        if (taken > 0)
        {
            long keep = Math.Min(taken, Math.Max(0, ws.load - w.Load));
            w.Load += keep; w.LoadV += keep * K.Vm; w.LoadC = K.Index;
            w.BiteT = 1;
            if (Rng.NextDouble() < 0.15) Emit(new GameEvent("workerBite") { Cake = K.Index, Pos = bc, N = (int)K.WPos(b).Y });
        }
        if (w.Load >= ws.load && !w.Escaping) { Remember(w, s); w.Step = null; GoDrop(w); }
    }

    void CheckEmptyCakes()
    {
        for (int k = 0; k < NbCakes; k++)
        {
            if (Alloc[k] == 0 || Cakes[k].Remaining > 0) continue;
            int to = -1;
            for (int j = NbCakes - 1; j >= 0; j--) if (Cakes[j].Unlocked && Cakes[j].Remaining > 0) { to = j; break; }
            if (to < 0) continue;
            Alloc[to] += Alloc[k]; Alloc[k] = 0;
            Emit(new GameEvent("info") { Text = L.F("Gâteau {0} entièrement mangé : ses ouvrières partent sur le gâteau {1}", k + 1, to + 1) });
            SyncWorkers();
        }
    }
}
