// ─────────────────────────────────────────────────────────────
// Mode construction : barre d'outils (1 = récolter, 2–8 = bâtiments,
// 9 = démolir), fantôme vert/rouge, grille des cases constructibles,
// cercles de portée, tapis orientés en glissant, rotation (R),
// infobulles sur les bâtiments survolés.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Render;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Player;

public partial class BuildController : Node3D
{
    public GameState Game;
    public CameraRig Cam;
    public string Mode = "harvest";   // harvest | belt | harv | … | demolish
    public int Dir;                   // orientation des prochains bâtiments
    public Vector2I? Hover;
    public bool Dragging;
    Vector2I? _lastDrag;
    Node3D _ghost;
    string _ghostType;
    StandardMaterial3D _ghostOk, _ghostBad;
    MeshInstance3D _grid, _ringReach, _ringGap, _ringPower;
    ShaderMaterial _gridMat;
    ImageTexture _gridTex;
    Image _gridImg;
    Vector2I _gridAt = new(int.MinValue, 0);
    int _gridGen = -1;
    const int GridN = 25;
    public event Action<string> Message;

    public void Setup(GameState game, CameraRig cam)
    {
        Game = game; Cam = cam;
        _ghostOk = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 1f, 0.5f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _ghostBad = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.3f, 0.25f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _gridImg = Image.CreateEmpty(GridN, GridN, false, Image.Format.R8);
        _gridTex = ImageTexture.CreateFromImage(_gridImg);
        _gridMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/grid.gdshader") };
        _gridMat.SetShaderParameter("cells", _gridTex);
        _gridMat.SetShaderParameter("n", (float)GridN);
        _grid = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(GridN, GridN) }, MaterialOverride = _gridMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_grid);
        _ringReach = Ring(new Color(1f, 0.85f, 0.2f));
        _ringGap = Ring(new Color(1f, 0.25f, 0.2f));
        _ringPower = Ring(new Color(0.35f, 0.65f, 1f));
    }

    MeshInstance3D Ring(Color c)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ring.gdshader") };
        m.SetShaderParameter("color", c);
        var mi = new MeshInstance3D { Mesh = new PlaneMesh { Size = Vector2.One * 2 }, MaterialOverride = m, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mi);
        return mi;
    }

    void ShowRing(MeshInstance3D r, Vector3 c, float radius)
    {
        r.Visible = true;
        r.GlobalPosition = new Vector3(c.X, 0.04f, c.Z);
        r.Scale = new Vector3(radius, 1, radius);
        ((ShaderMaterial)r.MaterialOverride).SetShaderParameter("radius", radius);
    }

    public bool Building => Mode != "harvest";

    public void SetMode(string m)
    {
        if (m != "harvest" && m != "demolish" && !Game.BuildingUnlocked(m))
        {
            Message?.Invoke(L.F("{0} : débloque d'abord « {1} » dans la boutique ({2})", GameState.BuildingTypes[m].Name, Upg[GameState.BuildingTypes[m].Req].Name, L.KeyName(Key.B)));
            Sfx.Play("error");
            return;
        }
        Mode = Mode == m ? "harvest" : m;
    }

    // case du sol sous la souris
    public static Vector2I? TestHover;
    void UpdateHover()
    {
        Hover = null;
        if (Cam.FirstPerson) return;
        if (TestHover is { } th) { Hover = th; return; } // développement : case survolée imposée (--hover=x,z)
        var mp = GetViewport().GetMousePosition();
        var o = Cam.Camera.ProjectRayOrigin(mp);
        var d = Cam.Camera.ProjectRayNormal(mp);
        if (d.Y >= -1e-4f) return;
        float t = (0.2f - o.Y) / d.Y;
        var p = o + d * t;
        Hover = new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z));
    }

    void EnsureGhost(string type)
    {
        if (_ghostType == type) return;
        _ghost?.QueueFree();
        _ghost = new Node3D();
        AddChild(_ghost);
        _ghostType = type;
        string[] meshes = type switch
        {
            "harv" => new[] { "crane_base", "crane_top" },
            "wheel" => new[] { "wheel_base", "wheel_rotor" },
            "store" => new[] { "store_base" },
            "comp" => new[] { "comp_base" },
            "oven" => new[] { "oven_base" },
            "deco" => new[] { "deco_base" },
            _ => Array.Empty<string>(),
        };
        foreach (var m in meshes)
        {
            var mesh = ModelLib.Get("buildings", m);
            if (mesh != null) _ghost.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = m == "wheel_rotor" ? new Vector3(0, BuildingsView.WheelAxle, 0) : m == "crane_top" ? new Vector3(GameState.MastOff, 0, GameState.MastOff) : Vector3.Zero });
        }
        // machines 3 × 3 : leurs tapis d'entrée et de sortie font partie de l'aperçu
        if (BuildingsView.HasPortBelts(type)) BuildingsView.AddPortBelts(_ghost, null, type);
        if (type == "belt" || type == "demolish")
            _ghost.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.96f, type == "belt" ? 0.2f : 0.05f, 0.96f) }, Position = new Vector3(0, 0.1f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // flèches de sortie (verte) et d'entrée (rouges), comme sur la machine posée
        if (type != "demolish") BuildingsView.AddPortArrows(_ghost, type);
    }

    void SetGhostMat(Material m)
    {
        foreach (var c in _ghost.GetChildren())
        {
            if (c is MeshInstance3D mi) mi.MaterialOverride = m; // (les flèches sont dans leur conteneur : elles gardent leurs couleurs)
            else if (c is Node3D box && box.Name == "PortBelts")
                foreach (var cc in box.GetChildren()) if (cc is MeshInstance3D pmi) pmi.MaterialOverride = m;
        }
    }

    public void UpdateFrame(bool panelOpen, Vector3 playerPos)
    {
        UpdateHover();
        _ringReach.Visible = _ringGap.Visible = _ringPower.Visible = false;
        bool show = Building && !panelOpen && Hover != null;
        _grid.Visible = show && Mode != "demolish";
        if (_ghost != null) _ghost.Visible = false;
        if (!show)
        {
            // survol en mode récolte : portée d'une grue ou d'une roue
            if (Hover is { } hc && !panelOpen && !Cam.FirstPerson)
            {
                var hb = Game.At(hc.X, hc.Y);
                if (hb?.Type == "harv") { var mp = GameState.MastOf(hb); ShowRing(_ringReach, new Vector3(mp.X, 0, mp.Y), Fx.HRange(Game.UpgLvl["turbo"]) + 0.5f); }
                if (hb?.Type == "wheel") ShowRing(_ringPower, new Vector3(hb.Cx, 0, hb.Cz), GameState.PowerR);
            }
            return;
        }
        var h = Hover.Value;
        // centre de l'emprise (roue 2 × 2 : le coin commun de ses 4 cases)
        float co = Mode == "demolish" ? 0 : GameState.CenterOff(Mode);
        var center = new Vector3(h.X + 0.5f + co, 0, h.Y + 0.5f + co);
        // grille des cases constructibles autour du curseur
        if (Mode != "demolish")
        {
            if (_gridAt != h || _gridGen != Game.Buildings.Count + Game.PowerGen * 1000)
            {
                _gridAt = h; _gridGen = Game.Buildings.Count + Game.PowerGen * 1000;
                int half = GridN / 2;
                for (int z = 0; z < GridN; z++)
                for (int x = 0; x < GridN; x++)
                {
                    int cx = h.X - half + x, cz = h.Y - half + z;
                    _gridImg.SetPixel(x, z, new Color(Game.CanPlace(cx, cz) ? 1 : 0, 0, 0));
                }
                _gridTex.Update(_gridImg);
            }
            _grid.GlobalPosition = new Vector3(h.X + 0.5f, 0.03f, h.Y + 0.5f);
        }
        EnsureGhost(Mode);
        _ghost.Visible = true;
        _ghost.GlobalPosition = center;
        _ghost.Rotation = new Vector3(0, -Dir * MathF.PI / 2, 0);
        bool ok;
        if (Mode == "demolish") ok = Game.At(h.X, h.Y) != null;
        else ok = Game.CanPlaceType(Mode, h.X, h.Y) && (Mode != "harv" || !Game.CraneTooClose(h.X, h.Y)) && Game.Money >= Game.CostOf(Mode);
        SetGhostMat(ok ? _ghostOk : _ghostBad);
        if (Mode == "harv")
        {
            // depuis le mât (case arrière droite de la grue, selon l'orientation)
            float a = -Dir * MathF.PI / 2;
            var mast = center + new Vector3(GameState.MastOff * (MathF.Cos(a) + MathF.Sin(a)), 0, GameState.MastOff * (MathF.Cos(a) - MathF.Sin(a)));
            ShowRing(_ringReach, mast, Fx.HRange(Game.UpgLvl["turbo"]) + 0.5f);
            ShowRing(_ringGap, center, GameState.CraneMinGap);
        }
        if (Mode == "wheel") ShowRing(_ringPower, center, GameState.PowerR);
        else if (Mode is "belt" or "harv" or "comp" or "oven" or "deco")
        {
            // la roue la plus proche : on voit si le bâtiment sera alimenté
            Building best = null; float bd = 999;
            foreach (var b in Game.Buildings) if (b.Type == "wheel") { float d = new Vector2(b.Cx - center.X, b.Cz - center.Z).Length(); if (d < bd) { bd = d; best = b; } }
            if (best != null && bd < GameState.PowerR + 6) ShowRing(_ringPower, new Vector3(best.Cx, 0, best.Cz), GameState.PowerR);
        }
        // pose en continu (glisser des tapis)
        if (Dragging && Mode == "belt") Place(h, true);
        // marteau maintenu : on démolit en chaîne tout ce que survole le curseur
        if (Dragging && Mode == "demolish" && _lastDrag != h) DemolishAt(h, true);
    }

    double _chainRefund; int _chainN;
    void DemolishAt(Vector2I h, bool drag)
    {
        _lastDrag = h;
        if (Game.At(h.X, h.Y) == null) return;
        double r = Game.Demolish(h.X, h.Y);
        Sfx.Play("buy", -6, drag ? 0.6f + 0.15f * (float)GD.Randf() : 0.7f);
        _chainRefund += r; _chainN++;
        if (!drag) Message?.Invoke(L.F("Démoli · +{0} remboursés", Fmt(r)));
    }

    public void Press()
    {
        if (Hover is not { } h) return;
        Dragging = true;
        _lastDrag = null;
        if (Mode == "demolish") { _chainRefund = 0; _chainN = 0; DemolishAt(h, false); return; }
        Place(h, false);
    }

    public void Release()
    {
        // fin d'une démolition en chaîne : un seul message récapitulatif
        if (Dragging && Mode == "demolish" && _chainN > 1) Message?.Invoke(L.F("{0} constructions démolies · +{1} remboursés", _chainN, Fmt(_chainRefund)));
        _chainN = 0; _chainRefund = 0;
        Dragging = false; _lastDrag = null;
    }
    // développement : glisser simulé (--dragtest, --demolishtest)
    public void DragTo(Vector2I h) { if (Mode == "demolish") { if (_lastDrag != h) DemolishAt(h, true); } else Place(h, true); }

    void Place(Vector2I h, bool drag)
    {
        if (drag && _lastDrag == h) return;
        // glisser en diagonale (ou très vite) : la souris saute des cases. On pose d'abord les cases
        // manquantes en « escalier » (on continue tout droit, puis on tourne), pour que la chaîne
        // reste continue et fasse un vrai virage au lieu de se casser.
        if (drag && _lastDrag is { } prev && Math.Abs(h.X - prev.X) + Math.Abs(h.Y - prev.Y) > 1)
            foreach (var c in Bridge(prev, h)) PlaceStep(c, true);
        PlaceStep(h, drag);
    }

    // cases intermédiaires (exclues : départ et arrivée) d'un trajet en L de a vers b
    List<Vector2I> Bridge(Vector2I a, Vector2I b)
    {
        var res = new List<Vector2I>();
        int sx = Math.Sign(b.X - a.X), sz = Math.Sign(b.Y - a.Y);
        if (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) > 40) return res; // saut aberrant : on ne comble pas
        // d'abord dans le sens du tapis en cours (tout droit), puis on tourne ; si la case du virage est
        // prise, on essaie l'autre ordre
        bool xFirst = Dir == 1 || Dir == 3;
        List<Vector2I> Path(bool xf)
        {
            var l = new List<Vector2I>();
            var c = a;
            void Step(int dx, int dz) { c = new Vector2I(c.X + dx, c.Y + dz); if (c != b) l.Add(c); }
            if (xf) { while (c.X != b.X) Step(sx, 0); while (c.Y != b.Y) Step(0, sz); }
            else { while (c.Y != b.Y) Step(0, sz); while (c.X != b.X) Step(sx, 0); }
            return l;
        }
        bool Free(List<Vector2I> l) { foreach (var c in l) { var o = Game.At(c.X, c.Y); if (o != null ? o.Type != "belt" : !Game.CanPlace(c.X, c.Y)) return false; } return true; }
        var p1 = Path(xFirst);
        if (Free(p1)) return p1;
        var p2 = Path(!xFirst);
        return Free(p2) ? p2 : p1;
    }

    void PlaceStep(Vector2I h, bool drag)
    {
        if (drag && _lastDrag == h) return;
        // glisser : le tapis précédent et le nouveau s'orientent dans le sens du geste
        if (drag && _lastDrag is { } prev)
        {
            int ddx = h.X - prev.X, ddz = h.Y - prev.Y;
            if (Math.Abs(ddx) + Math.Abs(ddz) == 1)
            {
                Dir = ddx == 1 ? 1 : ddx == -1 ? 3 : ddz == 1 ? 2 : 0;
                var pb = Game.At(prev.X, prev.Y);
                if (pb != null && pb.Type == "belt") Game.SetDir(pb, Dir);
            }
        }
        _lastDrag = h;
        if (Game.At(h.X, h.Y) != null) return;
        var (ok, why) = Game.Build(Mode, h.X, h.Y, Dir);
        if (ok) Sfx.Play("buy", -4, 1.3f);
        else if (!drag) { Sfx.Play("error"); Message?.Invoke(why); }
    }

    public void RotateKey()
    {
        if (Hover is { } h && !Building && Game.At(h.X, h.Y) != null) { Game.Rotate(h.X, h.Y); return; }
        if (Hover is { } h2 && Building && Game.At(h2.X, h2.Y) != null && Mode != "demolish") { Game.Rotate(h2.X, h2.Y); return; }
        Dir = (Dir + 1) & 3;
    }

    // stock détaillé par gâteau d'origine (« G1 120 · G3 40 »)
    static string Origins(Building b)
    {
        var parts = new List<string>();
        for (int c = 0; c < b.StockC.Length; c++) if (b.StockC[c] >= 1) parts.Add(L.F("G{0}", c + 1) + " " + Fmt(b.StockC[c]));
        return parts.Count > 1 || (parts.Count == 1 && b.Stock > 0) ? "\n" + string.Join(" · ", parts) : "";
    }

    // infobulle du bâtiment survolé : titre, détails, état (ok, idle, blocked, nopower), icône
    public (string title, string body, string state, string kind, string icon)? HoverTip()
    {
        if (Hover is not { } h) return null;
        var b = Game.At(h.X, h.Y);
        if (b == null) return null;
        var info = GameState.BuildingTypes[b.Type];
        string body = "", state = null, kind = "ok";
        switch (b.Type)
        {
            case "harv": body = L.T("Grignote le gâteau par le dessus, la partie la plus proche d'abord, et dépose les miettes sur son tapis de sortie (à brancher)."); break;
            case "store": body = L.F("Stock : {0} / {1} miettes", Fmt(b.Stock), Fmt(GameState.StoreMax)) + Origins(b); break;
            case "wheel": body = L.F("Alimente grues, tapis et usines à moins de {0} cases.", GameState.PowerR); break;
            case "comp": case "oven": case "deco":
            {
                var r = GameState.Recipes[b.Type];
                body = L.F("Stock : {0} / {1} {2}", Fmt(b.Stock), Fmt(r.max), L.Lower(Items[r.inp].Name)) + Origins(b);
                if (b.Batch > 0) body += "\n" + L.F("Fournée : {0} × {1} du gâteau {2} · {3} %", Fmt(b.Batch), L.Lower(Items[r.outp].Name), b.BatchC + 1, (int)(100 * (1 - b.Timer / r.time)));
                break;
            }
            case "belt": body = b.It != null ? L.F("Transporte : {0} × {1} · gâteau {2}", L.Lower(Items[b.It.T].Name), Fmt(b.It.Q), b.It.C + 1) : L.T("Vide"); break;
        }
        if (!b.Powered) { state = L.F("Pas de courant : pose une roue à moins de {0} cases", GameState.PowerR); kind = "nopower"; }
        else if (b.State == "blocked") { state = L.T("Sortie bloquée"); kind = "blocked"; }
        else if (b.State == "idle") { state = L.T(b.Type == "harv" ? "Plus rien à portée" : "En attente"); kind = "idle"; }
        else if (b.Type != "belt") state = L.T("En marche");
        body += (body.Length > 0 ? "\n" : "") + L.F("{0} : tourner · 9 : démolir", L.KeyName(Key.R));
        return (info.Name, body, state, kind, b.Type);
    }
}
