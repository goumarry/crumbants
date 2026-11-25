// ─────────────────────────────────────────────────────────────
// État de la partie : argent, améliorations, fourmi joueuse, gâteaux,
// fève, revenus, sauvegarde. Pas de rendu ici.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Sim;

public sealed class GameEvent
{
    public string Type;
    public int Cake = -1, N;
    public string Who;
    public Vector3 Pos;
    public double Value;
    public string Text;
    public GameEvent(string type) { Type = type; }
}

public enum FeveState { Ground, Carried }

public sealed class FeveObject
{
    public int Cake;
    public Vector3 Pos;
    public float Vy;
    public FeveState State;
}

public sealed class PlayerState
{
    public Vector3 Pos = new(Spawn.X, 0, Spawn.Y);
    public long Bag;
    public double BagV;        // valeur cumulée du sac (miettes × valeur de leur gâteau)
    public long[] BagC = new long[CakePos.Length]; // miettes du sac, par gâteau d'origine
    // gâteau dont viennent la plupart des miettes du sac (couleur affichée)
    public int BagCake { get { int b = 0; for (int c = 1; c < BagC.Length; c++) if (BagC[c] > BagC[b]) b = c; return b; } }
    public string Name = "Fourmi";
    public int Color;
}

public struct BiteResult
{
    public long Taken;
    public bool Full, OnFeve;
    public List<(Drop drop, int fig, bool isNew, bool all, double gain)> Found;
}

public sealed partial class GameState
{
    public readonly Random Rng = new();
    public readonly CakeSim[] Cakes;
    public readonly PlayerState Player = new();
    public readonly Dictionary<string, int> UpgLvl = new();
    public double Money = DevStartMoney;
    public double Earned;
    public long CrumbsEaten;
    public int DropCount;
    public double PlayTime;
    public bool[] Trophies = new bool[CakePos.Length]; // gâteau mangé à 100 %
    public int Crowns;
    public FeveObject FeveObj;
    public readonly List<GameEvent> Events = new();

    // revenus glissants sur 10 s, par source
    readonly Dictionary<string, double[]> _rev = new() { ["me"] = new double[10], ["workers"] = new double[10], ["belts"] = new double[10] };
    int _revSlot = -1;
    // miettes récoltées (croquées) sur 10 s glissantes, par source : toi, ouvrières, grues
    readonly Dictionary<string, double[]> _crumbs = new() { ["me"] = new double[10], ["worker"] = new double[10], ["harv"] = new double[10] };

    public void NoteCrumbs(string who, long n)
    {
        if (n > 0 && _crumbs.TryGetValue(who, out var a)) a[(int)Math.Floor(PlayTime) % 10] += n;
    }

    public (double me, double workers, double cranes) CrumbRate() =>
        (_crumbs["me"].Sum() / 10, _crumbs["worker"].Sum() / 10, _crumbs["harv"].Sum() / 10);

    public GameState()
    {
        foreach (var u in Upgrades) UpgLvl[u.Id] = 0;
        Cakes = new CakeSim[NbCakes];
        for (int i = 0; i < NbCakes; i++) { Cakes[i] = new CakeSim(this, i); Cakes[i].Init(); }
    }

    public void Emit(GameEvent e) => Events.Add(e);

    // ───── Requêtes spatiales ─────
    public CakeSim CakeAtCell(int x, int z)
    {
        foreach (var K in Cakes) if (K.OnCake(x, z)) return K;
        return null;
    }

    public CakeSim CakeNear(float x, float z, float margin = 2)
    {
        foreach (var K in Cakes) if (Mathf.Sqrt((x - K.Cx) * (x - K.Cx) + (z - K.Cz) * (z - K.Cz)) <= CakeR + margin) return K;
        return null;
    }

    public bool SolidAt(int x, int y, int z)
    {
        var K = CakeAtCell(x, z);
        return K != null && K.SolidAt(x, y, z);
    }

    public bool InsideCloche(float x, float z, float margin = 0)
    {
        foreach (var K in Cakes)
            if (!K.Unlocked && new Vector2(x - K.Cx, z - K.Cz).Length() < ClocheR + margin) return true;
        return false;
    }

    // Gâteau débloqué le plus proche (pour le HUD)
    public CakeSim CurrentCake(float x, float z)
    {
        CakeSim best = null; float bd = float.MaxValue;
        foreach (var K in Cakes)
        {
            if (!K.Unlocked) continue;
            float d = new Vector2(x - K.Cx, z - K.Cz).Length();
            if (d < bd) { bd = d; best = K; }
        }
        return best;
    }

    // Le gâteau dont on cherche la fève : le premier débloqué dont la fève n'est pas rapportée
    public CakeSim FeveCake() => Cakes.FirstOrDefault(K => K.Unlocked && !K.Found);

    // ───── Économie ─────
    public int TrophyCount => Trophies.Count(t => t);
    public double GMult() => (1 + 0.25 * UpgLvl["pub"]) * (1 + TrophyBonus * TrophyCount);

    // un gâteau entièrement mangé : son trophée (+10 % sur tous les gains)
    void CheckTrophies()
    {
        for (int k = 0; k < NbCakes; k++)
        {
            if (Trophies[k] || !Cakes[k].Unlocked || Cakes[k].Remaining > 0.5) continue;
            Trophies[k] = true;
            Emit(new GameEvent("trophy") { Cake = k, N = TrophyCount });
        }
    }

    public void Earn(double amount, string src)
    {
        Money += amount;
        Earned += amount;
        if (src != null) _rev[src][(int)Math.Floor(PlayTime) % 10] += amount;
    }

    public (double me, double workers, double belts, double total) Income()
    {
        double me = _rev["me"].Sum() / 10, w = _rev["workers"].Sum() / 10, b = _rev["belts"].Sum() / 10;
        return (me, w, b, me + w + b);
    }

    public double SellItem(string type, double q, double m, string src)
    {
        double v = Items[type].Value * q * m * GMult();
        Earn(v, src);
        return v;
    }

    public (int bite, int cap, float cooldown, float speed) Stats() =>
        (Fx.Bite(UpgLvl["mand"]), Fx.Cap(UpgLvl["sac"]), Fx.Cooldown(UpgLvl["mach"]), Fx.Speed(UpgLvl["pattes"]));

    // ───── Fourmi joueuse ─────
    public BiteResult PlayerBite(CakeSim K, int b)
    {
        var p = Player;
        var st = Stats();
        long room = st.cap - p.Bag;
        var res = new BiteResult { Found = new() };
        if (K == null || !K.Unlocked) return res;
        // La fève se dégage par gros morceaux (≈ 6 bouchées), même sac plein. Ses miettes ne vont pas
        // dans le sac : elles sont vendues sur-le-champ, comme une amande.
        if (b == K.Feve && !K.Found)
        {
            long want = Math.Max(st.bite, (long)Math.Ceiling(K.CpvL / 6.0));
            var fp = K.WPos(b) + new Vector3(0, 1.2f, 0);
            long t = K.Eat(b, want, "me");
            CrumbsEaten += t;
            if (t > 0)
            {
                double v = SellItem("crumb", t, K.Vm, "me");
                Emit(new GameEvent("feveCrumbs") { Value = v, Pos = fp });
            }
            res.Taken = t; res.Full = p.Bag >= st.cap; res.OnFeve = true;
            return res;
        }
        if (room <= 0) { res.Full = true; return res; }
        long taken = K.Eat(b, Math.Min(st.bite, room), "me");
        p.Bag += taken; p.BagV += taken * K.Vm; p.BagC[K.Index] += taken;
        CrumbsEaten += taken;
        // Trouvailles rares : un tirage par miette croquée
        for (long n = 0; n < taken; n++)
        {
            double r = Rng.NextDouble(), acc = 0;
            foreach (var d in Drops)
            {
                acc += d.P;
                if (r < acc) { res.Found.Add(DoDrop(d, K)); break; }
            }
        }
        res.Taken = taken; res.Full = p.Bag >= st.cap;
        return res;
    }

    (Drop, int, bool, bool, double) DoDrop(Drop d, CakeSim K)
    {
        DropCount++;
        double gain = d.Value * GMult() * K.Vm;
        Earn(gain, "me");
        return (d, -1, false, false, gain);
    }

    public double PlayerSell()
    {
        var p = Player;
        if (p.Bag <= 0) return 0;
        double v = SellItem("crumb", p.BagV > 0 ? p.BagV : p.Bag, 1, "me");
        p.Bag = 0; p.BagV = 0; Array.Clear(p.BagC);
        return v;
    }

    public bool NearNest(float x, float z) => new Vector2(x - Nest.X, z - Nest.Y).Length() <= NestSellR;

    // ───── Fève ─────
    public bool PickupFeve()
    {
        var o = FeveObj;
        if (o == null || o.State != FeveState.Ground) return false;
        o.State = FeveState.Carried;
        Emit(new GameEvent("fevePickup") { Cake = o.Cake, Pos = o.Pos });
        return true;
    }

    void TickFeveObj(float dt)
    {
        var o = FeveObj;
        if (o == null) return;
        if (o.State == FeveState.Carried)
        {
            o.Pos = Player.Pos + new Vector3(0, 0.6f, 0);
            if (NearNest(Player.Pos.X, Player.Pos.Z)) FeveFound(Cakes[o.Cake]);
            return;
        }
        // posée : elle tombe s'il n'y a rien dessous, remonte si un éboulis la recouvre
        var K = Cakes[o.Cake];
        int x = Mathf.FloorToInt(o.Pos.X), z = Mathf.FloorToInt(o.Pos.Z);
        int y = Mathf.FloorToInt(o.Pos.Y + 0.01f);
        if (K.SolidAt(x, y, z)) { o.Pos.Y = y + 1; o.Vy = 0; return; }
        float floor = 0;
        for (int yy = Math.Min(Layers - 1, y); yy >= 0; yy--) if (K.SolidAt(x, yy, z)) { floor = yy + 1; break; }
        if (o.Pos.Y > floor)
        {
            o.Vy = MathF.Min(20, o.Vy + Gravity * dt);
            o.Pos.Y = MathF.Max(floor, o.Pos.Y - o.Vy * dt);
        }
        else o.Vy = 0;
    }

    public void FeveFound(CakeSim K)
    {
        if (K.Found) return;
        K.Found = true;
        FeveObj = null;
        Crowns++;
        double bonus = Math.Max(1000, 0.3 * K.Remaining) * K.Vm * GMult();
        Earn(bonus, null);
        int next = K.Index + 1;
        bool unlocked = next < NbCakes && !Cakes[next].Unlocked;
        if (unlocked) Cakes[next].Unlocked = true;
        if (unlocked && Alloc.Sum() == 0 && WantedWorkers == 0) RecruitTo = 0;
        Emit(new GameEvent("feveFound") { Cake = K.Index, Value = bonus, N = unlocked ? next : -1, Pos = Player.Pos });
        if (Cakes.All(c => c.Found)) Emit(new GameEvent("victory"));
    }

    // ───── Boutique ─────
    public (bool ok, string why, double cost) CanBuyUpg(string id)
    {
        var u = Upg[id];
        int lvl = UpgLvl[id];
        if (lvl >= u.Max) return (false, "max", 0);
        if (u.Req != null && !u.Req(UpgLvl)) return (false, "req", 0);
        double cost = UpgCost(id, lvl);
        if (Money < cost) return (false, "money", cost);
        return (true, null, cost);
    }

    public bool BuyUpg(string id)
    {
        var c = CanBuyUpg(id);
        if (!c.ok) return false;
        Money -= c.cost;
        UpgLvl[id]++;
        if (id == "recruit") SyncWorkers();
        Emit(new GameEvent("buy") { Text = Upg[id].Name, Value = c.cost });
        return true;
    }

    // ───── Pas de simulation (20 Hz) ─────
    public void Tick(float dt)
    {
        PlayTime += dt;
        int slot = (int)Math.Floor(PlayTime) % 10;
        if (slot != _revSlot) { _revSlot = slot; foreach (var a in _rev.Values) a[slot] = 0; foreach (var a in _crumbs.Values) a[slot] = 0; }
        foreach (var K in Cakes)
        {
            K.FeveGravity();
            K.ProcessStability();
            K.TickFalling(dt);
        }
        TickFeveObj(dt);
        TickWorkers(dt);
        TickBuildings?.Invoke(dt);
        if ((int)(PlayTime * 2) != (int)((PlayTime - dt) * 2)) { CheckTrophies(); CheckEmptyCakes(); }
    }

    public Action<float> TickBuildings;

    // ───────────── Sauvegarde ─────────────
    const string SavePath = "user://save_v1.json";

    sealed class SaveData
    {
        public int V { get; set; } = 1;
        public double Money { get; set; }
        public double Earned { get; set; }
        public long Crumbs { get; set; }
        public int Drops { get; set; }
        public double Time { get; set; }
        public int Crowns { get; set; }
        public bool[] Trophies { get; set; }
        public long[] BagC { get; set; }
        public Dictionary<string, int> Upg { get; set; }
        public float[] Player { get; set; }
        public long Bag { get; set; }
        public double BagV { get; set; }
        public string Name { get; set; }
        public int Color { get; set; }
        public List<CakeSave> Cakes { get; set; }
        public float[] Feve { get; set; } // cake, x, y, z, carried
        public int[] Alloc { get; set; }
        public int RecruitTo { get; set; }
        public string Buildings { get; set; }
    }

    sealed class CakeSave
    {
        public bool Unlocked { get; set; }
        public bool Found { get; set; }
        public bool Hint { get; set; }
        public int Feve { get; set; }
        public string Data { get; set; }   // blocs compressés (deflate, base64)
        public string Crust { get; set; }
    }

    static string Pack(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Fastest)) z.Write(raw, 0, raw.Length);
        return Convert.ToBase64String(ms.ToArray());
    }

    static byte[] Unpack(string s)
    {
        using var ms = new MemoryStream(Convert.FromBase64String(s));
        using var z = new DeflateStream(ms, CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }

    public void Save()
    {
        foreach (var K in Cakes) K.SettleForSave();
        var d = new SaveData
        {
            Money = Money, Earned = Earned, Crumbs = CrumbsEaten, Drops = DropCount, Time = PlayTime, Crowns = Crowns,
            Trophies = Trophies, BagC = Player.BagC, Upg = new(UpgLvl),
            Player = new[] { Player.Pos.X, Player.Pos.Y, Player.Pos.Z }, Bag = Player.Bag, BagV = Player.BagV,
            Name = Player.Name, Color = Player.Color,
            Cakes = Cakes.Select(K =>
            {
                var raw = new byte[K.Cake.Length * 4];
                Buffer.BlockCopy(K.Cake, 0, raw, 0, raw.Length);
                return new CakeSave { Unlocked = K.Unlocked, Found = K.Found, Hint = K.FeveHint, Feve = K.Feve, Data = Pack(raw), Crust = Pack(K.Crust) };
            }).ToList(),
            Alloc = Alloc, RecruitTo = RecruitTo, Buildings = SaveBuildings?.Invoke(),
            Feve = FeveObj == null ? null : new[] { FeveObj.Cake, FeveObj.Pos.X, FeveObj.Pos.Y, FeveObj.Pos.Z, FeveObj.State == FeveState.Carried ? 1 : 0 },
        };
        using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(d));
    }

    public bool Load(string path = null)
    {
        path ??= SavePath;
        if (!Godot.FileAccess.FileExists(path)) return false;
        try
        {
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            var d = JsonSerializer.Deserialize<SaveData>(f.GetAsText());
            if (d == null || d.V != 1) return false;
            Money = d.Money; Earned = d.Earned; CrumbsEaten = d.Crumbs; DropCount = d.Drops; PlayTime = d.Time; Crowns = d.Crowns;
            if (d.Trophies?.Length == NbCakes) Trophies = d.Trophies;
            foreach (var (k, v) in d.Upg) if (UpgLvl.ContainsKey(k)) UpgLvl[k] = Math.Min(v, Upg[k].Max);
            Player.Pos = new Vector3(d.Player[0], d.Player[1], d.Player[2]);
            Player.Bag = d.Bag; Player.BagV = d.BagV; Player.Name = d.Name ?? "Fourmi"; Player.Color = d.Color;
            if (d.BagC?.Length == NbCakes) Player.BagC = d.BagC; else Player.BagC[0] = d.Bag; // ancienne sauvegarde
            for (int i = 0; i < Math.Min(Cakes.Length, d.Cakes.Count); i++)
            {
                var c = d.Cakes[i];
                var raw = Unpack(c.Data);
                var data = new uint[CW * CW * Layers];
                Buffer.BlockCopy(raw, 0, data, 0, Math.Min(raw.Length, data.Length * 4));
                var K = Cakes[i];
                K.Init(data, c.Feve);
                var crust = Unpack(c.Crust);
                if (crust.Length == K.Crust.Length) Array.Copy(crust, K.Crust, crust.Length);
                K.Unlocked = c.Unlocked || i == 0; K.Found = c.Found; K.FeveHint = c.Hint;
            }
            if (d.Alloc?.Length == NbCakes) Alloc = d.Alloc;
            RecruitTo = d.RecruitTo;
            PendingBuildings = d.Buildings;
            if (d.Feve != null)
                FeveObj = new FeveObject { Cake = (int)d.Feve[0], Pos = new Vector3(d.Feve[1], d.Feve[2], d.Feve[3]), State = d.Feve[4] > 0 ? FeveState.Carried : FeveState.Ground };
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"Sauvegarde illisible : {e.Message}");
            return false;
        }
    }

    public Func<string> SaveBuildings;
    public string PendingBuildings;

    public static void DeleteSave()
    {
        if (Godot.FileAccess.FileExists(SavePath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
    }
}
