// ─────────────────────────────────────────────────────────────
// CRUMBANTS — équilibrage et constantes (cf. GAME_DESIGN.md)
// Tout l'équilibrage est ici : si vous changez un chiffre,
// mettez aussi à jour le document de game design.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;

namespace Crumbants;

public static class Cfg
{
    // ── Monde ── (1 unité ≈ 2 mm)
    public const float PlateR = 60f;
    public const float CamMaxDist = 60f;
    public const float MoveLimit = 58.5f;
    // clairière d'herbe rase autour du nid : disque de centre (0, ClearCZ) et de rayon ClearR
    public const float ClearCZ = 12f, ClearR = 60f;
    public const int CakeR = 38;
    public const int Layers = 12;
    public const int CW = 78;            // largeur de la grille d'un gâteau (cases)
    public const int Off = 39;           // colonne i ↔ x local = i - Off
    public const int Area = CW * CW;
    public static readonly Vector2 Nest = new(0.5f, 55.5f);
    // Guichets de livraison de la fourmilière : seuls points où les tapis déposent leurs objets.
    // (case du guichet, direction d'où doit arriver le tapis = vers l'extérieur)
    public static readonly (Vector2I cell, Vector2I outward)[] NestDocks =
    {
        (new(0, 48), new(0, -1)), (new(7, 55), new(1, 0)), (new(0, 62), new(0, 1)), (new(-7, 55), new(-1, 0)),
    };
    public const float NestKeepR = 6.6f; // rayon (centre des cases) où l'on ne construit pas autour du nid
    public const float NestR = 3f, NestSellR = 4.8f;
    public static readonly Vector2 Spawn = new(4, 46);

    // Couverture de pique-nique (bornes en x et z)
    public const float BlanketX0 = -222, BlanketX1 = 201, BlanketZ0 = -320, BlanketZ1 = CakeR + 4.6f;
    public const float ClocheR = CakeR + 5;

    // Les six gâteaux (positions des centres)
    public static readonly Vector2I[] CakePos =
    {
        new(0, 0), new(119, -43), new(106, -175), new(-29, -162), new(-134, -77), new(-140, -239),
    };
    public static int NbCakes => CakePos.Length;

    // ── Gâteau ──
    public const long Cpv = 60;
    public const int CpvMaxPow = 22;
    public static long CpvOf(int n) => Cpv * (1L << Math.Min(n - 1, CpvMaxPow));
    public static double ValueMultOfCake(int n) => Math.Pow(2, n - 1);

    // ── Fourmi joueuse ──
    public const float Reach = 3.4f;
    public const float AntR = 0.28f, AntH = 0.55f;
    public const float Gravity = 22f, MaxFall = 20f, Jump = 6.5f, Climb = 0.7f;
    public const float EyeH = 0.3f, FpFwd = 0.12f, FpFov = 72f;

    // ── Simulation ──
    public const double Tick = 1.0 / 20.0;

    // ── Effondrements ──
    public const int Support = 4;
    public const float RubbleKeep = 0.5f, FallG = 32f, StunMe = 1.6f, StunWorker = 3f;

    // ── Trouvailles rares (seulement en croquant à la main) ──
    public record Drop(string Id, string NameFr, double P, double Value) { public string Name => L.T(NameFr); }
    public static readonly Drop[] Drops =
    {
        new("pepite", "Pépite de chocolat", 1.0 / 300, 50),
        new("amande", "Amande entière", 1.0 / 1500, 300),
    };

    // ── Trophées : un par gâteau mangé à 100 %, chacun +10 % sur tous les gains ──
    public const double TrophyBonus = 0.10;

    // ── Couleur des miettes de chaque gâteau : elle les suit partout (sacs, entrepôts, tapis,
    // machines, boulettes, petits gâteaux, gâteaux royaux) ──
    public static readonly Color[] CrumbColors =
    {
        new("#e09a32"), // 1 galette frangipane : ambre doré
        new("#5b3020"), // 2 forêt-noire : chocolat
        new("#f2768c"), // 3 fraisier : rose fraise
        new("#7fb84e"), // 4 matcha : vert thé
        new("#f4e04a"), // 5 citron : jaune citron
        new("#7a58c8"), // 6 myrtille : violet
    };
    public static Color CrumbColor(int cake) => CrumbColors[Math.Clamp(cake, 0, CrumbColors.Length - 1)];

    // ── Améliorations ──
    public record Upgrade(string Id, string NameFr, string DescFr, double Base, double Growth, int Max,
                          Func<Dictionary<string, int>, bool> Req = null, string ReqTextFr = null)
    {
        public string Name => L.T(NameFr);
        public string Desc => L.T(DescFr);
        public string ReqText => L.T(ReqTextFr);
    }

    public static readonly Upgrade[] Upgrades =
    {
        new("mand", "Mandibules affûtées", "Miettes par morsure", 15, 1.75, 40),
        new("sac", "Sac à dos", "Capacité du sac", 10, 1.8, 40),
        new("mach", "Mâchoire nerveuse", "Morsures plus rapides", 25, 1.9, 12),
        new("pattes", "Pattes de sprinteuse", "Vitesse de course", 30, 2.0, 10),
        new("recruit", "Recruter une ouvrière", "+1 ouvrière", 60, 1.3, 60),
        new("train", "Entraîner les ouvrières", "Ouvrières plus fortes", 300, 2.1, 20, u => u["recruit"] >= 1, "1 ouvrière"),
        new("eng", "Ingénierie fourmi", "Débloque tapis, grues, entrepôts et roues à fourmi", 400, 1, 1),
        new("turbo", "Grues turbo", "+50 % de cadence des grues, +1 de portée", 2500, 2.2, 15, u => u["eng"] >= 1, "Ingénierie"),
        new("oil", "Tapis huilés", "+30 % de vitesse des tapis", 3000, 2.4, 8, u => u["eng"] >= 1, "Ingénierie"),
        new("planComp", "Plan : Compacteur", "Débloque le compacteur", 4000, 1, 1, u => u["eng"] >= 1, "Ingénierie"),
        new("planOven", "Plan : Four", "Débloque le four", 30000, 1, 1, u => u["planComp"] >= 1, "Compacteur"),
        new("planDeco", "Plan : Décorateur", "Débloque le décorateur", 250000, 1, 1, u => u["planOven"] >= 1, "Four"),
        new("chefs", "Chefs pâtissiers", "+40 % de vitesse des usines", 20000, 2.5, 10, u => u["planComp"] >= 1, "Compacteur"),
        new("pub", "Pub à la télé fourmi", "+25 % sur tous les prix", 1000, 2.3, 30),
    };
    public static readonly Dictionary<string, Upgrade> Upg = new();
    static Cfg() { foreach (var u in Upgrades) Upg[u.Id] = u; }
    public static double UpgCost(string id, int lvl) => Math.Round(Upg[id].Base * Math.Pow(Upg[id].Growth, lvl));

    // ── Effets des améliorations ──
    public static class Fx
    {
        public static int Bite(int l) => (int)Math.Round((1 + l) * Math.Pow(1.2, l));
        public static int Cap(int l) => (int)Math.Round((3 + 3 * l) * Math.Pow(1.25, l));
        public static float Cooldown(int l) => 0.45f * MathF.Pow(0.9f, l);  // ≥ +11 % de cadence par niveau
        public static float Speed(int l) => 4.5f * MathF.Pow(1.1f, l);       // +10 % par niveau
        public static int WLoad(int l) => (int)Math.Round(4 * Math.Pow(1.6, l));
        public static double WRate(int l) => 1.5 * Math.Pow(1.5, l);
        public static float WSpeed(int l) => 3 + 0.3f * l;
        public static double HRate(int l) => 3 * Math.Pow(1.5, l);
        public static float HRange(int l) => 5 + l;
        public static double Belt(int l) => 2 * Math.Pow(1.3, l);
        public static double Factory(int l) => Math.Pow(1.4, l);
    }

    // ── Objets ──
    public record Item(string NameFr, double Value, int Crumbs) { public string Name => L.T(NameFr); }
    public static readonly Dictionary<string, Item> Items = new()
    {
        ["crumb"] = new("Miette", 1, 1),
        ["ball"] = new("Boulette de pâte", 40, 25),
        ["small"] = new("Petit gâteau", 260, 100),
        ["royal"] = new("Gâteau royal", 1500, 300),
    };

    // ── Thèmes (un par gâteau) ──
    public record CakeTheme(string NameFr, Color Crust, Color Glaze, Color Fill, Color Accent, Color Chunk) { public string Name => L.T(NameFr); }
    public static readonly CakeTheme[] Themes =
    {
        new("Galette frangipane", new("#d9a03c"), new("#e0a640"), new("#f0d48c"), new("#f6c93c"), new("#b9783e")),
        new("Forêt-noire", new("#3e2418"), new("#2c160e"), new("#5d3424"), new("#f6f0e6"), new("#9a1830")),
        new("Fraisier", new("#e6c387"), new("#f7dfe2"), new("#f7d8db"), new("#e23e58"), new("#c4203c")),
        new("Matcha impérial", new("#d3af72"), new("#8fb45e"), new("#9cc06a"), new("#f4eed4"), new("#55823a")),
        new("Citron meringué", new("#d6aa64"), new("#f6ecd2"), new("#f2df58"), new("#fdfaf2"), new("#e4c244")),
        new("Myrtille cosmique", new("#5a3a8c"), new("#4a2e7a"), new("#7b5cb6"), new("#c8b5ee"), new("#1c1f50")),
    };
    public static CakeTheme ThemeOf(int n) => Themes[(n - 1) % Themes.Length];

    public static readonly Color[] AntColors =
    {
        new("#c0392b"), new("#2c3e50"), new("#8e44ad"), new("#27ae60"), new("#e67e22"),
        new("#2980b9"), new("#d35400"), new("#16a085"), new("#e84393"), new("#f1c40f"),
    };

    // ── Développement ── ⚠️ à remettre à 0 / false avant de publier
    public const double DevStartMoney = 100_000_000;
    public const bool DevShowFeve = false;

    // ── Formatage des nombres ──
    static readonly string[] Suffix = { "", "k", "M", "Md", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };
    static string Suf(int i) => i == 3 ? L.Billion : Suffix[i];
    public static string Fmt(double n)
    {
        if (double.IsInfinity(n) || double.IsNaN(n)) return "∞";
        bool neg = n < 0; n = Math.Abs(n);
        string s;
        if (n < 1000) s = n < 10 && n % 1 != 0 ? n.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', L.Dec) : Math.Floor(n).ToString("0");
        else
        {
            int i = 0;
            while (n >= 1000 && i < Suffix.Length - 1) { n /= 1000; i++; }
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            s = (n < 10 ? n.ToString("0.00", ic) : n < 100 ? n.ToString("0.0", ic) : n.ToString("0", ic)).Replace('.', L.Dec) + (L.Billion.Contains(' ') && i == 3 ? " " : "") + Suf(i);
        }
        return (neg ? "-" : "") + s;
    }
}
