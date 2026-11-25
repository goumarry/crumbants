// ─────────────────────────────────────────────────────────────
// Bâtiments et logistique : tapis roulants (1 objet empilé par case),
// grues-mandibules, roues à fourmi (électricité), entrepôts, et la
// chaîne d'usines compacteur → four → décorateur.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Sim;

public sealed class Item
{
    static int _ids = 1;
    public int Id = _ids++;
    public string T;           // crumb, ball, small, royal
    public double Q;           // quantité (pile)
    public double M = 1;       // multiplicateur de valeur (celui du gâteau d'origine)
    public int C;              // gâteau d'origine (couleur des miettes, conservée à travers les machines)
    public float P;            // progression sur la case (0..1)
    public int Ex, Ez;         // bord d'entrée
    public Vector2 Pos, PrevPos;
    public bool Fresh = true;
}

public sealed class Building
{
    public int Id;
    public string Type;
    public int X, Z, Dir;
    public Item It;            // objet sur le tapis
    public double Stock;                              // total (affichage)
    public double[] StockC = new double[Cfg.CakePos.Length]; // stock par gâteau d'origine (jamais mélangé)
    public float Timer, Acc;
    public double Frac;
    public double Batch; public int BatchC;
    public Item Out, Buf;
    public int Target = -1, Tk = -1;
    public int LastC;
    public string State = "ok";
    public long Stamp = -1;
    public float Anim;
    public bool Powered = true;
    public int Source = -1;
    public Vector3 CraneTarget; // pour l'animation de la grue
    // machines 3 × 3 : tapis de sortie intégré (l'objet y avance avant de passer au tapis suivant)
    public Item OutIt;
    public float OutP;
    public int H => GameState.HalfOf(Type);          // ports : 0 (1 × 1) ou 1 (machine 3 × 3)
    public int Size => GameState.SizeOf(Type);       // emprise : 1, 2 (roue) ou 3 (machines)
    // centre au sol (monde) : milieu de la case pour 1 × 1 et 3 × 3, coin commun des 4 cases en 2 × 2
    public float Cx => X + 0.5f + GameState.CenterOff(Type);
    public float Cz => Z + 0.5f + GameState.CenterOff(Type);
    public float Ext => Size / 2f;                   // demi-largeur au sol
}

public static class Dirs
{
    public static readonly (int dx, int dz)[] D = { (0, -1), (1, 0), (0, 1), (-1, 0) }; // N E S O
}

public sealed partial class GameState
{
    public readonly List<Building> Buildings = new();
    readonly Dictionary<(int, int), Building> _grid = new();
    long _tickN;
    int _bid = 1;
    public int PowerGen;

    public record BuildInfo(string NameFr, string DescFr, double Cost, double Growth, string Req, string Key) { public string Name => L.T(NameFr); public string Desc => L.T(DescFr); }
    public static readonly Dictionary<string, BuildInfo> BuildingTypes = new()
    {
        ["belt"] = new("Tapis roulant", "Transporte 1 objet par case (électrique)", 5, 1, "eng", "2"),
        ["harv"] = new("Grue", "Grue pilotée par une fourmi (2 × 2 cases) : grignote le gâteau par le dessus et dépose les miettes sur son tapis de sortie (électrique)", 250, 1.24, "eng", "3"),
        ["store"] = new("Entrepôt", "Les ouvrières y déposent leurs miettes s'il est plus près que le nid ; une magasinière les renvoie par le tapis de sortie (électrique)", 400, 1.35, "eng", "4"),
        ["wheel"] = new("Roue à fourmi", "Une fourmi court dans la roue (2 × 2 cases) : électricité pour les grues, tapis et usines à moins de 9 cases", 300, 1.3, "eng", "5"),
        ["comp"] = new("Compacteur", "25 miettes → 1 boulette (électrique)", 2000, 1.3, "planComp", "6"),
        ["oven"] = new("Four", "4 boulettes → 1 petit gâteau (électrique)", 15000, 1.3, "planOven", "7"),
        ["deco"] = new("Décorateur", "3 petits gâteaux → 1 gâteau royal (électrique)", 120000, 1.3, "planDeco", "8"),
    };
    public static readonly string[] BuildOrder = { "belt", "harv", "store", "wheel", "comp", "oven", "deco" };
    public static readonly Dictionary<string, (string inp, int need, string outp, float time, double max)> Recipes = new()
    {
        ["comp"] = ("crumb", 25, "ball", 1, 1250),
        ["oven"] = ("ball", 4, "small", 2, 200),
        ["deco"] = ("small", 3, "royal", 3, 150),
    };
    public const int Batch = 50;
    public const float HarvPackets = 1.5f;
    public const int PowerR = 9, CraneMinGap = 10;
    public const double StoreMax = 20000, StoreRate = 3, StorePackMin = 10, StorePackFrac = 0.08;
    // (l'entrepôt : son tapis de sortie ne tourne qu'avec le courant ; les ouvrières y déposent toujours)
    static bool NeedsPower(string t) => t is "belt" or "harv" or "comp" or "oven" or "deco" or "store";

    public Building At(int x, int z) => _grid.TryGetValue((x, z), out var b) ? b : null;

    // ───── Emprise : les machines (entrepôt, compacteur, four, décorateur) font 3 × 3 cases ─────
    // (X, Z) est la case CENTRALE. Une entrée au milieu de chaque côté, la sortie au milieu de l'avant.
    public static bool Big(string t) => t is "store" or "comp" or "oven" or "deco";
    public static int HalfOf(string t) => Big(t) ? 1 : 0;
    // la roue à fourmi fait 2 × 2 : la case visée et ses voisines à l'est et au sud
    public static int SizeOf(string t) => Big(t) ? 3 : t is "wheel" or "harv" ? 2 : 1;
    static int LoOf(int size) => -((size - 1) / 2);
    public static float CenterOff(string t) { int sz = SizeOf(t), lo = LoOf(sz); return (lo + lo + sz - 1) / 2f; }
    static IEnumerable<(int x, int z)> Cells(int x, int z, int size)
    {
        int lo = LoOf(size);
        for (int dz = lo; dz < lo + size; dz++)
            for (int dx = lo; dx < lo + size; dx++) yield return (x + dx, z + dz);
    }
    public static IEnumerable<(int x, int z)> CellsOf(Building b) => Cells(b.X, b.Z, b.Size);
    // la case DE la machine qui porte son tapis de sortie : milieu de l'avant (3 × 3),
    // case avant gauche (grue 2 × 2), la case elle-même (1 × 1)
    public static (int x, int z) PortCell(Building b)
    {
        var (dx, dz) = Dirs.D[b.Dir];
        if (b.Size == 3) return (b.X + dx, b.Z + dz);
        if (b.Size == 2)
        {
            int lx = dz, lz = -dx; // à gauche quand on regarde vers (dx, dz)
            return ((int)MathF.Floor(b.Cx + (dx + lx) * 0.5f), (int)MathF.Floor(b.Cz + (dz + lz) * 0.5f));
        }
        return (b.X, b.Z);
    }
    // la case (hors machine) où sort sa production
    public static (int x, int z) OutCell(Building b)
    {
        var (dx, dz) = Dirs.D[b.Dir];
        var (px, pz) = PortCell(b);
        return (px + dx, pz + dz);
    }

    // (affichage) le tapis intégré n° i est-il branché ? 0 = sortie ; 1, 2, 3 = entrées gauche, droite, arrière
    public bool PortLinked(Building b, int i)
    {
        var (dx, dz) = Dirs.D[b.Dir];
        if (i == 0)
        {
            var (px, pz) = PortCell(b);
            var (ox, oz) = OutCell(b);
            if (ReservedCell(ox, oz) == "dock")
            {
                foreach (var (cell, o) in NestDocks) if (cell.X == ox && cell.Y == oz && px == ox + o.X && pz == oz + o.Y) return true;
                return false;
            }
            var n = At(ox, oz);
            if (n == null || n == b) return false;
            if (n.Type == "belt") { var (ndx, ndz) = Dirs.D[n.Dir]; return !(n.X + ndx == px && n.Z + ndz == pz); }
            return InPortFrom(n, ox, oz, px, pz) && BuildingsAccepts(n);
        }
        if (b.H == 0) return false;
        int lx = dz, lz = -dx;
        var (ex, ez) = i == 1 ? (lx, lz) : i == 2 ? (-lx, -lz) : (-dx, -dz);
        int cx = b.X + ex, cz = b.Z + ez;             // case d'entrée de la machine
        var m = At(cx + ex, cz + ez);                 // la case juste à l'extérieur
        if (m == null || m == b) return false;
        if (m.Type == "belt") { var (mdx, mdz) = Dirs.D[m.Dir]; return m.X + mdx == cx && m.Z + mdz == cz; }
        var (mx, mz) = OutCell(m);
        return mx == cx && mz == cz;
    }
    // point du repère local du bâtiment (x à droite, z vers l'arrière ; l'avant est −z) → monde
    public static Vector2 LocalToWorld(Building b, float lx, float lz)
    {
        float a = -b.Dir * MathF.PI / 2, c = MathF.Cos(a), sn = MathF.Sin(a);
        return new Vector2(b.Cx + lx * c + lz * sn, b.Cz - lx * sn + lz * c);
    }
    // la grue 2 × 2 : son mât est au centre de la case arrière droite (l'avant gauche porte la sortie)
    public const float MastOff = 0.5f;
    public static Vector2 MastOf(Building b) => LocalToWorld(b, MastOff, MastOff);

    static bool BuildingsAccepts(Building n) => n.Type is "comp" or "oven" or "deco";
    // une case d'entrée de la machine b : milieu d'un côté autre que la sortie ; side = direction vers l'extérieur
    public static bool IsInPort(Building b, int x, int z, out (int dx, int dz) side)
    {
        int ex = x - b.X, ez = z - b.Z;
        side = (Math.Sign(ex), Math.Sign(ez));
        if (b.Type == "store") return false; // l'entrepôt n'a pas d'entrée : ce sont les ouvrières qui le remplissent
        if (b.H == 0) return true;
        if (Math.Abs(ex) + Math.Abs(ez) != 1) return false;
        var (fx, fz) = Dirs.D[b.Dir];
        return !(ex == fx && ez == fz);
    }
    public bool CanPlaceType(string type, int x, int z)
    {
        foreach (var (cx, cz) in Cells(x, z, SizeOf(type))) if (!CanPlace(cx, cz)) return false;
        return true;
    }
    public int CountOf(string type) => Buildings.Count(b => b.Type == type);
    public double CostOf(string type) => Math.Round(BuildingTypes[type].Cost * Math.Pow(BuildingTypes[type].Growth, CountOf(type)));
    public bool BuildingUnlocked(string type) => UpgLvl[BuildingTypes[type].Req] >= 1;

    // ───── Où peut-on construire ? ─────
    public string ReservedCell(int x, int z)
    {
        float cx = x + 0.5f, cz = z + 0.5f;
        foreach (var (cell, _) in NestDocks) if (cell.X == x && cell.Y == z) return "dock";
        if (new Vector2(cx - Nest.X, cz - Nest.Y).Length() <= NestKeepR) return "nest";
        if (MathF.Abs(cx - Layout.Manager.X) < 2.6f && MathF.Abs(cz - (Layout.Manager.Y + 0.6f)) < 2.4f) return "desk";
        if (Blocked != null && Blocked(cx, cz, 0.5f)) return "prop";
        return null;
    }

    // renseigné par le décor (objets géants) : (x, z, rayon) → bloqué ?
    public Func<float, float, float, bool> Blocked;

    public bool InBuildArea(float x, float z) => Render.Terrain.EdgeDist(x, z) >= 1.2f || Render.Terrain.ClearDist(x, z) <= ClearR - 2.5f;

    public bool PlaceOk(int x, int z)
    {
        if (!InBuildArea(x + 0.5f, z + 0.5f)) return false;
        foreach (var K in Cakes) if (K.OnCake(x, z)) return false;
        // pas sur les assiettes des gâteaux (le rebord n'est pas plat)
        foreach (var p in CakePos) if (new Vector2(x + 0.5f - p.X, z + 0.5f - p.Y).Length() < Render.Terrain.PlateOuterR + 0.3f) return false;
        if (ReservedCell(x, z) != null) return false;
        if (InsideCloche(x + 0.5f, z + 0.5f, 1)) return false;
        return true;
    }

    public bool CanPlace(int x, int z) => PlaceOk(x, z) && At(x, z) == null;
    public bool CraneTooClose(int x, int z) => Buildings.Any(b => b.Type == "harv" && new Vector2(b.Cx - (x + 1), b.Cz - (z + 1)).Length() < CraneMinGap);

    public (bool ok, string why) Build(string type, int x, int z, int dir)
    {
        if (!BuildingUnlocked(type)) return (false, L.T("Verrouillé"));
        if (!CanPlaceType(type, x, z)) return (false, SizeOf(type) > 1 ? L.F("Pas assez de place ({0} × {0} cases)", SizeOf(type)) : L.T("Emplacement impossible"));
        if (type == "harv" && CraneTooClose(x, z)) return (false, L.F("Trop près d'une autre grue ({0} cases minimum)", CraneMinGap));
        double cost = CostOf(type);
        if (Money < cost) return (false, L.T("Pas assez de pièces"));
        Money -= cost;
        var b = new Building { Id = _bid++, Type = type, X = x, Z = z, Dir = dir & 3 };
        Buildings.Add(b);
        foreach (var c in CellsOf(b)) _grid[c] = b;
        RecomputePower();
        Emit(new GameEvent("built") { Text = type, Pos = new Vector3(b.Cx, 0, b.Cz), Value = cost });
        if (type == "belt") SnapDocks();
        return (true, null);
    }

    public double Demolish(int x, int z)
    {
        var b = At(x, z);
        if (b == null) return 0;
        double refund = Math.Round(BuildingTypes[b.Type].Cost * Math.Pow(BuildingTypes[b.Type].Growth, CountOf(b.Type) - 1) * 0.5);
        Buildings.Remove(b);
        foreach (var c in CellsOf(b)) _grid.Remove(c);
        RecomputePower();
        Money += refund;
        Emit(new GameEvent("demolished") { Text = b.Type, Pos = new Vector3(b.Cx, 0, b.Cz), Value = refund });
        return refund;
    }

    public bool Rotate(int x, int z)
    {
        var b = At(x, z);
        if (b == null) return false;
        b.Dir = (b.Dir + 1) & 3;
        Emit(new GameEvent("rotated") { Pos = new Vector3(b.Cx, 0, b.Cz), N = b.Id, Who = "key" });
        return true;
    }

    public void SetDir(Building b, int dir) { if (b.Dir != dir) { b.Dir = dir; Emit(new GameEvent("rotated") { N = b.Id }); } SnapDocks(); }

    // Un tapis posé sur la case devant un guichet du nid, qui ne mène nulle part (bout de ligne,
    // arrivé en biais en glissant), se tourne de lui-même vers le guichet : il devient un virage
    // qui entre de face dans le nid.
    public void SnapDocks()
    {
        foreach (var (cell, o) in NestDocks)
        {
            var b = At(cell.X + o.X, cell.Y + o.Y);
            if (b == null || b.Type != "belt") continue;
            var (dx, dz) = Dirs.D[b.Dir];
            int tx = b.X + dx, tz = b.Z + dz;
            if (tx == cell.X && tz == cell.Y) continue;           // déjà tourné vers le guichet
            if (At(tx, tz) != null) continue;                     // il alimente autre chose : on n'y touche pas
            for (int d = 0; d < 4; d++)
                if (Dirs.D[d].dx == -o.X && Dirs.D[d].dz == -o.Y) { b.Dir = d; Emit(new GameEvent("rotated") { N = b.Id }); }
        }
    }

    void RecomputePower()
    {
        var wheels = Buildings.Where(b => b.Type == "wheel").ToList();
        foreach (var b in Buildings)
        {
            if (!NeedsPower(b.Type)) { b.Powered = true; b.Source = -1; continue; }
            Building best = null; float bd = float.MaxValue;
            foreach (var w in wheels)
            {
                // du centre de la roue jusqu'au bord de la machine
                float d = MathF.Max(0, new Vector2(w.Cx - b.Cx, w.Cz - b.Cz).Length() - (b.Ext - 0.5f));
                if (d <= PowerR && d < bd) { bd = d; best = w; }
            }
            b.Powered = best != null;
            b.Source = best?.Id ?? -1;
        }
        PowerGen++;
    }

    // ───── Échanges entre cases ─────
    bool Accept(int x, int z, Item item, int fx, int fz)
    {
        var res = ReservedCell(x, z);
        if (res == "dock")
        {
            // le guichet n'accepte que ce qui arrive de face (le tapis de la case devant, tourné vers le nid)
            foreach (var (cell, o) in NestDocks)
                if (cell.X == x && cell.Y == z && fx == x + o.X && fz == z + o.Y) { Sold(item, x, z); return true; }
            return false;
        }
        if (res == "nest") return false;
        var t = At(x, z);
        if (t == null) return false;
        switch (t.Type)
        {
            case "belt":
            {
                if (t.It != null) return false;
                var (dx, dz) = Dirs.D[t.Dir];
                if (t.X + dx == fx && t.Z + dz == fz) return false; // pointe vers la source
                item.P = 0;
                int ex = Math.Sign(fx - t.X), ez = Math.Sign(fz - t.Z);
                if (Math.Abs(ex) + Math.Abs(ez) == 1) { item.Ex = ex; item.Ez = ez; } else { item.Ex = -dx; item.Ez = -dz; }
                t.It = item;
                t.Stamp = _tickN;
                return true;
            }
            case "store":
                return false; // pas d'entrée par tapis : ce sont les ouvrières (et toi) qui le remplissent
            case "comp": case "oven": case "deco":
            {
                if (!InPortFrom(t, x, z, fx, fz)) return false;
                var r = Recipes[t.Type];
                if (item.T != r.inp || t.Stock >= r.max) return false;
                AddStock(t, item.C, item.Q);
                PortIn(t, x, z, item);
                return true;
            }
        }
        return false;
    }

    // l'objet arrive-t-il par une entrée de la machine (de la case juste à l'extérieur) ?
    static bool InPortFrom(Building t, int x, int z, int fx, int fz)
    {
        if (!IsInPort(t, x, z, out var side)) return false;
        return t.H == 0 || (fx == x + side.dx && fz == z + side.dz);
    }

    // (affichage) l'objet continue sur le tapis d'entrée intégré, jusque sous le capot
    void PortIn(Building t, int x, int z, Item item)
    {
        if (t.H == 0) return;
        Emit(new GameEvent("portIn") { N = t.Id, Text = item.T, Cake = item.C, Pos = new Vector3(x - t.X, 0, z - t.Z) });
    }

    void Sold(Item item, int x, int z)
    {
        // payé au prix du gâteau d'où viennent ces miettes
        double v = SellItem(item.T, item.Q, ValueMultOfCake(item.C + 1), "belts");
        Emit(new GameEvent("beltSold") { Value = v, Text = item.T, Cake = item.C, Pos = new Vector3(x + 0.5f, 1, z + 0.5f) });
    }

    static void AddStock(Building b, int cake, double q) { b.StockC[cake] += q; b.Stock += q; }
    // le gâteau dont la machine a le plus de stock (au moins `need`), -1 sinon
    static int RichestStock(Building b, double need)
    {
        int best = -1;
        for (int c = 0; c < b.StockC.Length; c++) if (b.StockC[c] >= need && (best < 0 || b.StockC[c] > b.StockC[best])) best = c;
        return best;
    }
    static Item NewItem(string t, double q, int cake) => new() { T = t, Q = q, C = cake, M = ValueMultOfCake(cake + 1) };

    // Sortie d'une machine : devant elle, sinon (grue) un tapis collé.
    bool Output(Building b, Item item)
    {
        var (dx, dz) = Dirs.D[b.Dir];
        // depuis la case qui porte le tapis de sortie vers la case suivante (seule sortie possible)
        var (px, pz) = PortCell(b);
        return Accept(px + dx, pz + dz, item, px, pz);
    }

    // Une machine 3 × 3 pose sa production sur son tapis de sortie intégré (s'il est libre) ;
    // une machine 1 × 1 la donne directement à la case devant elle.
    bool Push(Building b, Item item)
    {
        if (b.Size == 1) return Output(b, item);
        if (b.OutIt != null) return false;
        b.OutIt = item; b.OutP = 0;
        return true;
    }

    void TickOutBelt(Building b, float dt, double speed)
    {
        if (!b.Powered) { b.State = "nopower"; return; } // tapis de sortie à l'arrêt sans courant
        var it = b.OutIt;
        if (it == null) return;
        b.OutP = MathF.Min(1, b.OutP + (float)(speed * dt));
        if (b.OutP < 1) return;
        if (Output(b, it)) { b.OutIt = null; b.OutP = 0; }
        else b.State = "blocked";
    }

    // ───── Pas de simulation ─────
    public void TickLogistics(float dt)
    {
        _tickN++;
        double beltSpeed = Fx.Belt(UpgLvl["oil"]);
        double fSpeed = Fx.Factory(UpgLvl["chefs"]);
        double hRate = Fx.HRate(UpgLvl["turbo"]);
        float hRange = Fx.HRange(UpgLvl["turbo"]);
        foreach (var b in Buildings)
        {
            if (b.It != null) b.It.PrevPos = b.It.Pos;
            if (b.Anim > 0 && b.Type != "comp" && b.Type != "oven" && b.Type != "deco") b.Anim = MathF.Max(0, b.Anim - dt * 3);
        }
        foreach (var b in Buildings.ToList())
        {
            switch (b.Type)
            {
                case "harv": TickHarv(b, dt, hRate, hRange); TickOutBelt(b, dt, beltSpeed); break;
                case "belt": TickBelt(b, dt, beltSpeed); break;
                case "store": TickStore(b, dt); TickOutBelt(b, dt, beltSpeed); break;
                case "comp": case "oven": case "deco": TickFactory(b, dt, fSpeed); TickOutBelt(b, dt, beltSpeed); break;
            }
        }
        // positions des objets : courbe de Bézier bord d'entrée → centre → bord de sortie
        foreach (var b in Buildings)
        {
            var it = b.It;
            if (it == null) continue;
            var (dx, dz) = Dirs.D[b.Dir];
            float t = MathF.Min(it.P, 1), a = (1 - t) * (1 - t), c = t * t;
            it.Pos = new Vector2(b.X + 0.5f + (a * it.Ex + c * dx) * 0.5f, b.Z + 0.5f + (a * it.Ez + c * dz) * 0.5f);
            if (it.Fresh) { it.PrevPos = it.Pos; it.Fresh = false; }
        }
    }

    void TickBelt(Building b, float dt, double speed)
    {
        if (b.Stamp == _tickN) return;
        b.Stamp = _tickN;
        if (!b.Powered) return;
        var it = b.It;
        if (it == null) return;
        it.P += (float)(speed * dt);
        if (it.P < 1) return;
        var (dx, dz) = Dirs.D[b.Dir];
        int nx = b.X + dx, nz = b.Z + dz;
        var nb = At(nx, nz);
        if (nb != null && nb.Type == "belt" && nb.It != null && nb.Stamp != _tickN) TickBelt(nb, dt, speed);
        float carry = it.P - 1;
        if (Accept(nx, nz, it, b.X, b.Z))
        {
            b.It = null;
            if (nb != null && nb.Type == "belt" && nb.It == it) it.P = MathF.Min(carry, 0.9f);
        }
        else it.P = 1;
    }

    void TickHarv(Building b, float dt, double rate, float range)
    {
        if (!b.Powered) { b.State = "nopower"; return; }
        if (b.State == "nopower") b.State = "ok";
        if (b.Buf != null)
        {
            if (Push(b, b.Buf)) { b.Buf = null; b.State = "ok"; }
            else { b.State = "blocked"; return; }
        }
        float period = 1 / HarvPackets;
        b.Acc += dt;
        while (b.Acc >= period)
        {
            b.Acc -= period;
            b.Frac += rate / HarvPackets;
            long want = (long)Math.Floor(b.Frac);
            if (want <= 0) continue;
            long got = Harvest(b, want, range);
            b.Frac -= want;
            if (got <= 0) { b.State = "idle"; b.Acc = 0; b.Frac = 0; break; }
            var item = NewItem("crumb", got, b.LastC);
            b.Anim = 1;
            if (!Push(b, item)) { b.Buf = item; b.State = "blocked"; break; }
            b.State = "ok";
        }
    }

    CakeSim HarvCake(Building b, float range)
    {
        CakeSim best = null; float bd = float.MaxValue;
        foreach (var K in Cakes)
        {
            if (!K.Unlocked) continue;
            var mp = MastOf(b);
            float d = new Vector2(mp.X - K.Cx, mp.Y - K.Cz).Length();
            if (d < CakeR + range + 1.5f && d < bd) { bd = d; best = K; }
        }
        return best;
    }

    long Harvest(Building b, long need, float range)
    {
        long got = 0;
        var K = b.Tk >= 0 ? Cakes[b.Tk] : null;
        int guard = 0;
        while (need > 0 && guard++ < 50)
        {
            if (K == null || b.Target < 0 || K.Cake[b.Target] == 0)
            {
                K = HarvCake(b, range);
                var mp = MastOf(b);
                b.Target = K != null ? K.PickTopTarget((int)MathF.Floor(mp.X), (int)MathF.Floor(mp.Y), range) : -1;
                b.Tk = K != null && b.Target >= 0 ? K.Index : -1;
                if (b.Target < 0) break;
                b.CraneTarget = K.WPos(b.Target) + new Vector3(0, 1, 0);
            }
            if (b.Target == K.Feve && !K.Found)
            {
                // la grue déterre la fève : on vide le bloc et elle apparaît
                long t0 = K.Eat(b.Target, long.MaxValue, "harv");
                got += t0; need -= t0;
                continue;
            }
            long t = K.Eat(b.Target, need, "harv");
            got += t; need -= t;
        }
        b.LastC = K?.Index ?? 0;
        return got;
    }

    void TickStore(Building b, float dt)
    {
        if (b.Out != null)
        {
            if (Push(b, b.Out)) { b.Out = null; b.State = "ok"; }
            else { b.State = "blocked"; return; }
        }
        if (b.Stock <= 0) { b.State = b.OutIt != null && b.OutP >= 1 ? "blocked" : "idle"; b.Acc = 0; return; }
        b.Acc += dt;
        float period = (float)(1 / StoreRate);
        while (b.Acc >= period && b.Stock > 0)
        {
            b.Acc -= period;
            // une pile d'un seul gâteau à la fois : les couleurs ne se mélangent jamais
            int c = RichestStock(b, 1e-9);
            if (c < 0) { b.Stock = 0; break; }
            double q = Math.Min(b.StockC[c], Math.Max(StorePackMin, Math.Ceiling(b.Stock * StorePackFrac)));
            b.StockC[c] -= q; b.Stock = Math.Max(0, b.Stock - q);
            var item = NewItem("crumb", q, c);
            b.Anim = 1;
            if (!Push(b, item)) { b.Out = item; b.State = "blocked"; return; }
            b.State = "ok";
        }
    }

    void TickFactory(Building b, float dt, double speed)
    {
        var r = Recipes[b.Type];
        if (!b.Powered) { b.State = "nopower"; return; }
        if (b.State == "nopower") b.State = "ok";
        if (b.Out != null)
        {
            if (Push(b, b.Out)) { b.Out = null; b.State = "ok"; }
            else { b.State = "blocked"; return; }
        }
        if (b.Batch > 0)
        {
            b.Timer -= (float)(dt * speed);
            b.Anim = MathF.Min(1, b.Anim + dt * 4);
            if (b.Timer <= 0)
            {
                b.Out = NewItem(r.outp, b.Batch, b.BatchC);
                b.Batch = 0;
                Emit(new GameEvent("produced") { Text = r.outp, Pos = new Vector3(b.X + 0.5f, 1.5f, b.Z + 0.5f) });
                if (Push(b, b.Out)) b.Out = null; else b.State = "blocked";
            }
            return;
        }
        b.Anim = MathF.Max(0, b.Anim - dt * 2);
        // une fournée = des ingrédients d'un même gâteau (sa couleur et sa valeur passent au produit)
        int c = RichestStock(b, r.need);
        if (c >= 0)
        {
            b.Batch = Math.Min(Batch, Math.Floor(b.StockC[c] / r.need));
            double used = b.Batch * r.need;
            b.StockC[c] -= used; b.Stock = Math.Max(0, b.Stock - used);
            b.BatchC = c;
            b.Timer = r.time;
        }
    }

    // ───── Entrepôts et ouvrières ─────
    public void SetupLogistics()
    {
        TickBuildings = TickLogistics;
        StoreFor = w =>
        {
            var list = new List<(int, Vector3)>();
            if (w.At < 0) return list;
            var K = Cakes[w.At];
            foreach (var b in Buildings)
            {
                if (b.Type != "store" || b.Stock >= StoreMax * 0.95) continue;
                var p = new Vector3(b.X + 0.5f, 0, b.Z + 0.5f);
                // pas trop loin du gâteau
                if (new Vector2(p.X - K.Cx, p.Z - K.Cz).Length() > CakeR + 25) continue;
                // on dépose au bord de l'entrepôt, côté gâteau (pas au milieu de la machine)
                var toK = new Vector2(K.Cx - p.X, K.Cz - p.Z);
                if (toK.LengthSquared() > 1e-4f) { toK = toK.Normalized() * (b.H + 0.75f); p += new Vector3(toK.X, 0, toK.Y); }
                list.Add((b.Id, p));
            }
            return list;
        };
        DepositToStore = (w, id) =>
        {
            var b = Buildings.FirstOrDefault(x => x.Id == id);
            if (b == null) { SellItem("crumb", w.LoadV > 0 ? w.LoadV : w.Load, 1, "workers"); return; }
            AddStock(b, Math.Max(0, w.LoadC), w.Load);
            b.Anim = 1;
        };
        // [type, x, z, dir, stock total, stock gâteau 1 … gâteau 6]
        SaveBuildings = () => JsonSerializer.Serialize(Buildings.Select(b => new[] { BuildIndex(b.Type), b.X, b.Z, b.Dir, (int)Math.Min(int.MaxValue, b.Stock) }
            .Concat(b.StockC.Select(v => (int)Math.Min(int.MaxValue, v)))).ToList());
        if (!string.IsNullOrEmpty(PendingBuildings))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<int[]>>(PendingBuildings);
                foreach (var a in list)
                {
                    var b = new Building { Id = _bid++, Type = BuildOrder[a[0]], X = a[1], Z = a[2], Dir = a[3] };
                    if (a.Length >= 5 + NbCakes) for (int c = 0; c < NbCakes; c++) AddStock(b, c, a[5 + c]);
                    else AddStock(b, 0, a[4]); // ancienne sauvegarde : stock sans origine
                    // mal placé (ou machine devenue 3 × 3 qui n'a plus la place) : remboursé
                    if (!CellsOf(b).All(c => PlaceOk(c.x, c.z) && At(c.x, c.z) == null)) { Money += CostOf(b.Type); Refunded.Add(b.Type); continue; }
                    Buildings.Add(b);
                    foreach (var c in CellsOf(b)) _grid[c] = b;
                }
            }
            catch (Exception e) { GD.PushWarning("Bâtiments illisibles : " + e.Message); }
            PendingBuildings = null;
            RecomputePower();
        }
    }

    static int BuildIndex(string t) => Array.IndexOf(BuildOrder, t);
    public readonly List<string> Refunded = new(); // au chargement : machines qui n'avaient plus la place (remboursées)

    // le joueur vide son sac dans un entrepôt qu'il touche
    public Building StoreNear(float x, float z) =>
        Buildings.FirstOrDefault(b => b.Type == "store" && MathF.Abs(b.X + 0.5f - x) < 1.3f + b.H && MathF.Abs(b.Z + 0.5f - z) < 1.3f + b.H && b.Stock < StoreMax);

    public long PlayerDeposit(Building b)
    {
        var p = Player;
        if (p.Bag <= 0) return 0;
        long q = (long)Math.Min(p.Bag, StoreMax - b.Stock);
        // gâteau par gâteau : chaque miette garde son origine
        long left = q;
        for (int c = 0; c < NbCakes && left > 0; c++)
        {
            long t = Math.Min(left, p.BagC[c]);
            if (t <= 0) continue;
            AddStock(b, c, t);
            p.BagC[c] -= t; p.BagV -= t * ValueMultOfCake(c + 1);
            left -= t;
        }
        p.Bag -= q - left;
        if (p.Bag <= 0) { p.Bag = 0; p.BagV = 0; }
        q -= left;
        b.Anim = 1;
        return q;
    }
}
