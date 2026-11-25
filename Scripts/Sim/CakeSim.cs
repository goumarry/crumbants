// ─────────────────────────────────────────────────────────────
// Un gâteau sur la couverture : blocs, croûte, solidité, éboulis, fève.
// Grille CW × CW × Layers, centrée en (cx, cz). Case monde x ↔ colonne i = x + IX.
// Aucun rendu ici : la simulation fait autorité.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Sim;

public sealed class FallingBlock
{
    public int I, K;
    public float Y, PY, Y0, Vel;
    public uint V;
    public bool Feve;
    public byte Crust;
}

public sealed partial class CakeSim
{
    public static int Idx(int i, int y, int k) => i + k * CW + y * Area;
    public static int IdxI(int b) => b % CW;
    public static int IdxK(int b) => (b / CW) % CW;
    public static int IdxY(int b) => b / Area;

    public readonly GameState Game;
    public readonly int Index;           // 0 = premier gâteau
    public int N => Index + 1;           // numéro affiché
    public readonly int Cx, Cz, IX, IZ;
    public readonly CakeTheme Theme;
    public readonly long CpvL;           // miettes par bloc
    public readonly double Vm;           // valeur d'une miette
    public bool Unlocked;
    public bool Found;                   // fève rapportée au nid
    public bool FeveHint;

    public uint[] Cake;                  // miettes restantes par bloc
    public byte[] H0;                    // hauteur d'origine des colonnes
    public float[] Hx;                   // hauteur exacte (rendu lisse du bord)
    public sbyte[] ColTop;               // plus haut bloc plein de chaque colonne (-1 : vide)
    public byte[] Crust;                 // 1 = bloc du dessus glacé
    public byte[] Stab;                  // solidité 0..Support
    public int Feve = -1;                // bloc qui contient la fève (-1 : sortie)
    public double Remaining, Total;

    public readonly HashSet<int> Dirty = new();       // blocs modifiés (rendu)
    public readonly List<FallingBlock> Falling = new();
    public readonly HashSet<int> Critical = new();     // plafonds au bord de la rupture
    List<int> _stabQ = new();

    public CakeSim(GameState game, int index)
    {
        Game = game;
        Index = index;
        Cx = CakePos[index].X; Cz = CakePos[index].Y;
        IX = Off - Cx; IZ = Off - Cz;
        Theme = ThemeOf(N);
        CpvL = CpvOf(N);
        Vm = ValueMultOfCake(N);
        Unlocked = index == 0;
    }

    public void Init(uint[] data = null, int? feve = null)
    {
        H0 = new byte[Area];
        Hx = new float[Area];
        var cake = data ?? new uint[CW * CW * Layers];
        var pool = new List<int>();
        for (int k = 0; k < CW; k++)
        for (int i = 0; i < CW; i++)
        {
            float x = i - Off + 0.5f, z = k - Off + 0.5f;
            float r = MathF.Sqrt(x * x + z * z);
            if (r > CakeR) continue;
            float h = Layers * (1 + 0.05f * (1 - (r / CakeR) * (r / CakeR)));
            if (r > CakeR - 4) h *= 1 - 0.45f * ((r - (CakeR - 4)) / 4f);
            Hx[i + k * CW] = MathF.Min(Layers, h);
            int hi = Math.Clamp((int)MathF.Round(h), 1, Layers);
            H0[i + k * CW] = (byte)hi;
            if (data == null) for (int y = 0; y < hi; y++) cake[Idx(i, y, k)] = (uint)Math.Min(CpvL, uint.MaxValue);
            // la fève : n'importe quelle hauteur, jamais dans la couche du sol ni dans le dessus
            if (r <= CakeR - 3 && hi >= 4) for (int y = 1; y < hi - 1; y++) pool.Add(Idx(i, y, k));
        }
        Cake = cake;
        Feve = feve ?? pool[Game.Rng.Next(pool.Count)];
        Remaining = 0; Total = 0;
        for (int b = 0; b < cake.Length; b++) Remaining += cake[b];
        for (int c = 0; c < Area; c++) Total += H0[c] * (double)CpvL;
        ColTop = new sbyte[Area];
        for (int c = 0; c < Area; c++) RecalcColTop(c);
        Crust = new byte[CW * CW * Layers];
        for (int c = 0; c < Area; c++) if (ColTop[c] >= 0) Crust[c + ColTop[c] * Area] = 1;
        Stab = new byte[CW * CW * Layers];
        Falling.Clear();
        _stabQ.Clear();
        Critical.Clear();
        RecomputeRegion(0, CW - 1, 0, CW - 1, 0, true);
    }

    // centre (monde) du bloc b
    public Vector3 WPos(int b) => new(IdxI(b) - IX + 0.5f, IdxY(b), IdxK(b) - IZ + 0.5f);
    public int IdxW(int x, int y, int z) => Idx(x + IX, y, z + IZ);

    public void RecalcColTop(int c)
    {
        int t = -1;
        for (int y = Layers - 1; y >= 0; y--) if (Cake[c + y * Area] > 0) { t = y; break; }
        ColTop[c] = (sbyte)t;
    }

    // Bloc plein ? (coordonnées de case monde entières)
    public bool SolidAt(int x, int y, int z)
    {
        if (y < 0 || y >= Layers) return false;
        int i = x + IX, k = z + IZ;
        if (i < 0 || k < 0 || i >= CW || k >= CW) return false;
        return Cake[i + k * CW + y * Area] > 0;
    }

    public bool OnCake(int x, int z)
    {
        int i = x + IX, k = z + IZ;
        if (i < 0 || k < 0 || i >= CW || k >= CW) return false;
        return H0[i + k * CW] > 0;
    }

    // Colonne (indice) sous un point monde, ou -1
    public int ColAt(float x, float z)
    {
        int i = Mathf.FloorToInt(x) + IX, k = Mathf.FloorToInt(z) + IZ;
        if (i < 0 || k < 0 || i >= CW || k >= CW) return -1;
        return i + k * CW;
    }

    // ───── La fève ─────
    public void RevealFeve(string who)
    {
        if (Game.FeveObj != null || Feve < 0) return;
        var p = WPos(Feve);
        Game.FeveObj = new FeveObject { Cake = Index, Pos = p, State = FeveState.Ground };
        Feve = -1;
        Game.Emit(new GameEvent("feveReveal") { Cake = Index, Who = who, Pos = p + new Vector3(0, 0.3f, 0) });
    }

    // Mange n miettes du bloc b. Renvoie le nombre réellement pris.
    public long Eat(int b, long n, string who)
    {
        uint v = Cake[b];
        if (v == 0 || n <= 0) return 0;
        // (les ouvrières mangent aussi le bloc de la fève, comme les grues : vidé, il libère la fève,
        // qui tombe au sol ; seule la joueuse la ramasse et la rapporte au nid)
        long take = Math.Min(v, n);
        Cake[b] = (uint)(v - take);
        Remaining -= take;
        Game.NoteCrumbs(who, take);
        Dirty.Add(b);
        if (take == v)
        {
            RecalcColTop(b % Area);
            Crust[b] = 0;
            _stabQ.Add(b);
            MarkGal();
            if (b == Feve && !Found) RevealFeve(who);
        }
        return take;
    }

    // ───── Stabilité ─────
    // Solidité : posé sur l'assiette → Support ; posé sur un bloc → hérite ; en porte-à-faux → meilleur voisin − 1.
    List<int> RecomputeRegion(int i0, int i1, int k0, int k1, int y0, bool silent = false)
    {
        const int K = Support;
        var cake = Cake; var st = Stab;
        i0 = Math.Max(0, i0); k0 = Math.Max(0, k0); i1 = Math.Min(CW - 1, i1); k1 = Math.Min(CW - 1, k1);
        var fell = new List<int>();
        for (int y = Math.Max(0, y0); y < Layers; y++)
        {
            int L = y * Area;
            bool any = false;
            for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
            {
                int b = i + k * CW + L;
                if (cake[b] == 0) { st[b] = 0; continue; }
                any = true;
                st[b] = (byte)(y == 0 ? K : cake[b - Area] != 0 ? st[b - Area] : 0);
            }
            if (!any) continue;
            for (int pass = 0; pass < K; pass++)
            {
                bool changed = false;
                for (int k = k0; k <= k1; k++)
                for (int i = i0; i <= i1; i++)
                {
                    int b = i + k * CW + L;
                    if (cake[b] == 0 || st[b] >= K) continue;
                    int s = st[b];
                    if (i > 0 && cake[b - 1] != 0 && st[b - 1] - 1 > s) s = st[b - 1] - 1;
                    if (i < CW - 1 && cake[b + 1] != 0 && st[b + 1] - 1 > s) s = st[b + 1] - 1;
                    if (k > 0 && cake[b - CW] != 0 && st[b - CW] - 1 > s) s = st[b - CW] - 1;
                    if (k < CW - 1 && cake[b + CW] != 0 && st[b + CW] - 1 > s) s = st[b + CW] - 1;
                    if (s != st[b]) { st[b] = (byte)s; changed = true; }
                }
                if (!changed) break;
            }
            for (int k = k0; k <= k1; k++)
            for (int i = i0; i <= i1; i++)
            {
                int b = i + k * CW + L;
                if (cake[b] == 0) { Critical.Remove(b); continue; }
                if (st[b] == 0) { fell.Add(b); Detach(b, silent); continue; }
                if (st[b] == 1 && y > 0 && cake[b - Area] == 0) Critical.Add(b);
                else Critical.Remove(b);
            }
        }
        return fell;
    }

    // Un bloc se détache : il perd une partie de ses miettes et commence à tomber.
    void Detach(int b, bool silent, bool keepAll = false)
    {
        uint v = Cake[b];
        // enragement : les éboulements que provoquent les ouvrières ne gaspillent rien
        uint kept = keepAll || Relax >= 2 ? v : Math.Max(1u, (uint)(v * RubbleKeep));
        Cake[b] = 0;
        Remaining -= v - kept;
        Stab[b] = 0;
        Critical.Remove(b);
        Dirty.Add(b);
        RecalcColTop(b % Area);
        int y = IdxY(b);
        Falling.Add(new FallingBlock { I = IdxI(b), K = IdxK(b), Y = y, PY = y, Y0 = y, V = kept, Feve = b == Feve, Crust = Crust[b] });
        Crust[b] = 0;
    }

    // La fève a une gravité : elle n'est jamais suspendue au-dessus du vide. Sinon, comme les
    // ouvrières ne la mangent pas, elle porterait indéfiniment les miettes posées dessus, en
    // l'air et hors d'atteinte. Elle tombe entière ; ce qu'elle portait s'effondre à son tour.
    public void FeveGravity()
    {
        if (Feve < 0 || Found || Cake[Feve] == 0) return;
        int y = IdxY(Feve);
        if (y == 0 || Cake[Feve - Area] != 0) return;
        int b = Feve;
        Detach(b, true, keepAll: true);
        _stabQ.Add(b);
    }

    // Traite les blocs vidés depuis le dernier pas (et les réactions en chaîne).
    public void ProcessStability()
    {
        const int K = Support;
        int total = 0, guard = 0;
        float sx = 0, sy = 0, sz = 0;
        while (_stabQ.Count > 0 && guard++ < 32)
        {
            var cols = new Dictionary<int, int>();
            foreach (int b in _stabQ)
            {
                int c = b % Area, y = IdxY(b);
                if (!cols.TryGetValue(c, out int yy) || yy > y) cols[c] = y;
            }
            _stabQ = new List<int>();
            foreach (var (c, y) in cols)
            {
                int i = c % CW, k = c / CW;
                foreach (int b in RecomputeRegion(i - K, i + K, k - K, k + K, y))
                {
                    _stabQ.Add(b);
                    total++; sx += IdxI(b); sy += IdxY(b); sz += IdxK(b);
                }
            }
        }
        if (total > 0)
            Game.Emit(new GameEvent("collapse") { Cake = Index, N = total, Pos = new Vector3(sx / total - IX + 0.5f, sy / total, sz / total - IZ + 0.5f) });
    }

    int GroundUnder(FallingBlock f)
    {
        int col = f.I + f.K * CW;
        for (int y = Math.Min(Layers - 1, Mathf.CeilToInt(f.Y) - 1); y >= 0; y--) if (Cake[col + y * Area] != 0) return y + 1;
        return 0;
    }

    public void TickFalling(float dt)
    {
        if (Falling.Count == 0) return;
        Falling.Sort((a, b) => a.Y.CompareTo(b.Y)); // du plus bas au plus haut
        var still = new List<FallingBlock>();
        foreach (var f in Falling)
        {
            f.PY = f.Y;
            f.Vel = MathF.Min(28, f.Vel + FallG * dt);
            float ny = f.Y - f.Vel * dt;
            int ground = GroundUnder(f);
            if (ny <= ground) Land(f, ground, true);
            else { f.Y = ny; still.Add(f); }
        }
        Falling.Clear();
        Falling.AddRange(still);
    }

    // Pose immédiatement tous les éboulis (avant une sauvegarde).
    public void SettleForSave()
    {
        for (int guard = 0; guard < 40 && Falling.Count > 0; guard++)
        {
            Falling.Sort((a, b) => a.Y.CompareTo(b.Y));
            var list = new List<FallingBlock>(Falling);
            Falling.Clear();
            foreach (var f in list) Land(f, GroundUnder(f), false);
        }
        _stabQ.Clear();
    }

    void Land(FallingBlock f, int ground, bool live)
    {
        int col = f.I + f.K * CW;
        int y = ground;
        while (y < Layers && Cake[col + y * Area] != 0) y++;
        if (y >= Layers) { Remaining -= f.V; return; }
        int b = col + y * Area;
        Cake[b] = f.V;
        Dirty.Add(b);
        MarkGal();
        RecalcColTop(col);
        int s = y == 0 ? Support : Stab[b - Area];
        foreach (int d in new[] { 1, -1, CW, -CW })
        {
            int n = b + d;
            if (n >= 0 && n < Cake.Length && Cake[n] != 0 && Stab[n] - 1 > s) s = Stab[n] - 1;
        }
        Stab[b] = (byte)Math.Max(1, s);
        Critical.Remove(b);
        if (f.Feve) Feve = b;
        Crust[b] = f.Crust;
        if (y > 0 && Crust[b - Area] != 0 && f.Crust != 0) Crust[b - Area] = 0;
        if (!live) return;
        float wx = f.I - IX, wz = f.K - IZ;
        Game.Emit(new GameEvent("land") { Pos = new Vector3(wx + 0.5f, y, wz + 0.5f) });
        // écrasement de la fourmi joueuse
        var p = Game.Player;
        const float R = AntR;
        if (p.Pos.X + R > wx && p.Pos.X - R < wx + 1 && p.Pos.Z + R > wz && p.Pos.Z - R < wz + 1 && p.Pos.Y < y + 1 && p.Pos.Y + AntH > y - 0.2f)
        {
            long lost = p.Bag / 2;
            if (p.Bag > 0)
            {
                double keepK = (double)(p.Bag - lost) / p.Bag;
                p.BagV *= keepK;
                for (int c = 0; c < p.BagC.Length; c++) p.BagC[c] = (long)Math.Round(p.BagC[c] * keepK);
            }
            p.Bag -= lost;
            Game.Emit(new GameEvent("crush") { Who = "me", N = (int)lost, Pos = p.Pos });
        }
        // … et les ouvrières
        foreach (var w in Game.Workers)
        {
            if (w.Stun > 0) continue;
            // enragées (gâteau bloqué) : elles encaissent les éboulements sans broncher
            if (Relax >= 2 && w.Cake == Index) continue;
            if (w.Pos.X + 0.2f > wx && w.Pos.X - 0.2f < wx + 1 && w.Pos.Z + 0.2f > wz && w.Pos.Z - 0.2f < wz + 1 && w.Pos.Y < y + 1 && w.Pos.Y + 0.45f > y - 0.2f)
            {
                // sonnée : elle perd sa charge, puis reprend ce qu'elle faisait
                w.Stun = StunWorker;
                w.Load = 0; w.LoadV = 0;
                Game.Emit(new GameEvent("crush") { Who = "worker", Pos = w.Pos });
            }
        }
    }
}
