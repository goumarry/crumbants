// ─────────────────────────────────────────────────────────────
// CRUMBANTS — chef d'orchestre : construit le monde, fait tourner la
// simulation à 20 Hz, relie fourmi, caméra, effets et interface.
//
// Arguments de développement (après « -- » sur la ligne de commande) :
//   --shot=<fichier.png>  capture d'écran puis quitte
//   --delay=<s>           délai avant la capture (défaut 4)
//   --cam=x,y,z,tx,ty,tz  caméra imposée
//   --new                 nouvelle partie (ignore la sauvegarde)
//   --demo                creuse un cratère et une galerie (test visuel)
//   --money=<n>           pièces au départ
//   --fp / --bite / --press=<action>  vue fourmi, croquer, maintenir une action
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Crumbants.Player;
using Crumbants.Render;
using Crumbants.Sim;
using Crumbants.UI;
using static Crumbants.Cfg;

namespace Crumbants;

public partial class Main : Node3D
{
    GameState _game;
    readonly List<CakeView> _views = new();
    readonly List<CakeToppings> _toppings = new();
    NestDocksView _docks;
    int _beltSoldN;
    Vector3? _feveCam;
    PlayerController _player;
    CameraRig _cam;
    Effects _fx;
    Hud _hud;
    WorkersView _wv;
    BuildingsView _bv;
    BuildController _build;
    PropsView _props;
    Image _occ; ImageTexture _occTex; int _occGen = -1;
    float _time;
    double _acc, _saveT;
    float _bagMsgT = -10;
    bool _collapseSeen;
    readonly Dictionary<string, string> _args = new();

    public override void _EnterTree()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var s = a.TrimStart('-');
            int i = s.IndexOf('=');
            if (i < 0) _args[s] = "1"; else _args[s[..i]] = s[(i + 1)..];
        }
        SetupInput();
    }

    static void Key(string action, params Godot.Key[] keys)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        foreach (var k in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    // Touches physiques : ZQSD sur AZERTY = WASD sur QWERTY
    static void SetupInput()
    {
        Key("move_forward", Godot.Key.W, Godot.Key.Up);
        Key("move_back", Godot.Key.S, Godot.Key.Down);
        Key("move_left", Godot.Key.A);
        Key("move_right", Godot.Key.D);
        Key("cam_left", Godot.Key.Left);
        Key("cam_right", Godot.Key.Right);
        Key("jump", Godot.Key.Space);
        Key("shop", Godot.Key.B);
        Key("help", Godot.Key.H);
        Key("view", Godot.Key.V);
        Key("interact", Godot.Key.E);
        Key("mute", Godot.Key.M);
        Key("cancel", Godot.Key.Escape);
        Key("xray", Godot.Key.G);
        Key("prices", Godot.Key.P);
        Key("fps", Godot.Key.F3);
        // manette
        Axis("move_forward", JoyAxis.LeftY, -1); Axis("move_back", JoyAxis.LeftY, 1);
        Axis("move_left", JoyAxis.LeftX, -1); Axis("move_right", JoyAxis.LeftX, 1);
        Pad("jump", JoyButton.A); Pad("interact", JoyButton.X); Pad("shop", JoyButton.Y);
        Pad("help", JoyButton.Back); Pad("cancel", JoyButton.Start); Pad("view", JoyButton.RightStick);
        Pad("xray", JoyButton.LeftShoulder);
        Axis("bite_pad", JoyAxis.TriggerRight, 1);
        Pad("bite_pad", JoyButton.RightShoulder);
    }

    static void Pad(string action, JoyButton b)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = b });
    }

    static void Axis(string action, JoyAxis a, float v)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.25f);
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = a, AxisValue = v });
    }

    // nouvelle partie demandée depuis l'écran titre (survit au rechargement de la scène)
    static (string name, int color)? _pendingNew;
    Menus _menus;
    Music _music;
    XrayView _xray;
    float _titleT;
    int _wlogT = -1;
    readonly Dictionary<int, Vector3> _wlogLast = new();
    bool _to2Done;

    public override void _Ready()
    {
        AddChild(new Sfx { Name = "Sfx" });
        LegacyData.Migrate();
        UI.Settings.Load();
        // test : profil de qualité imposé (--quality=0|1|2), sans l'enregistrer
        if (_args.TryGetValue("lang", out var lg)) L.Set(lg); // test : langue imposée, sans l'enregistrer
        if (_args.ContainsKey("showfps")) UI.Settings.ShowFps = true; // test : compteur affiché sans l'enregistrer
        if (_args.ContainsKey("novsync")) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); // mesures de perf
        if (_args.TryGetValue("quality", out var qs) && int.TryParse(qs, out var qv)) UI.Settings.Quality = Math.Clamp(qv, 0, 2);
        _game = new GameState();
        bool fresh = _args.ContainsKey("new") || _pendingNew != null;
        // --save=<fichier> : charger une autre sauvegarde (test ; jamais réécrite en mode capture)
        bool hasSave = !fresh && _game.Load(_args.TryGetValue("save", out var svp) ? svp : null);
        if (_pendingNew is { } pn) { _game.Player.Name = pn.name; _game.Player.Color = pn.color; }
        if (_args.TryGetValue("money", out var m) && double.TryParse(m, out var mv)) _game.Money = mv;
        if (_args.TryGetValue("train", out var tn) && int.TryParse(tn, out var tl)) _game.UpgLvl["train"] = tl;
        if (_args.TryGetValue("workers", out var wn) && int.TryParse(wn, out var wc)) { _game.UpgLvl["recruit"] = wc; _game.Alloc[0] = wc; }
        PropField.Build();
        _game.Blocked = Props.Blocked;
        if (_args.ContainsKey("wlog")) _game.GhostCheck = p => { foreach (var K in _game.Cakes) if (K.SolidAt(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y + 0.5f), Mathf.FloorToInt(p.Z))) return true; return false; };
        _game.SetupLogistics();
        _game.SyncWorkers();

        WorldBuilder.Build(this, _game, UI.Settings.Quality);
        var cakeShader = GD.Load<Shader>("res://Shaders/cake.gdshader");
        foreach (var K in _game.Cakes)
        {
            var v = CakeView.Create(K, cakeShader);
            AddChild(v);
            _views.Add(v);
            var tp = CakeToppings.Create(K);
            AddChild(tp);
            _toppings.Add(tp);
        }
        if (_args.ContainsKey("demo")) Demo();
        if (_args.ContainsKey("feveview")) FeveView();
        if (_args.ContainsKey("factory")) DemoFactory();
        if (_args.ContainsKey("items")) DemoItems();
        // test : entrepôt(s) posé(s) (--store=x,z;x,z) — ingénierie débloquée
        if (_args.TryGetValue("store", out var sts))
        {
            _game.UpgLvl["eng"] = 1;
            foreach (var one in sts.Split(';'))
            {
                var v = one.Split(',');
                var r = _game.Build("store", int.Parse(v[0]), int.Parse(v[1]), 0);
                GD.Print($"[store] {one} : {(r.ok ? "posé" : r.why)}");
            }
        }
        // test : le 1er gâteau entièrement mangé (trophée)
        if (_args.ContainsKey("eatall")) { var K0 = _game.Cakes[0]; for (int b = 0; b < K0.Cake.Length; b++) if (K0.Cake[b] != 0) K0.Eat(b, long.MaxValue, "demo"); }
        foreach (var v in _views) v.RebuildAll();

        _fx = new Effects { Name = "Effects" };
        AddChild(_fx);
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        _hud.Setup(_game);
        // machines d'une ancienne sauvegarde (1 × 1) qui n'ont plus la place en 3 × 3 : remboursées
        if (_game.Refunded.Count > 0)
        {
            var names = _game.Refunded.GroupBy(t => t).Select(g => $"{g.Count()} {L.Lower(GameState.BuildingTypes[g.Key].Name)}");
            _hud.Feed(L.F("Les machines font maintenant 3 × 3 cases : remboursées faute de place ({0})", string.Join(", ", names)), new Color("#ffcf9a"), "warn");
        }
        _fx.Setup(_game, _hud.FontBold);
        _hud.ResetRequested += () =>
        {
            GameState.DeleteSave();
            _resetting = true;
            GetTree().ReloadCurrentScene();
        };

        _wv = new WorkersView { Name = "Workers" };
        AddChild(_wv);
        _wv.Setup(_game);
        _player = new PlayerController { Name = "Player" };
        AddChild(_player);
        _cam = new CameraRig { Name = "CameraRig" };
        AddChild(_cam);
        _player.Setup(_game, _views, _cam);
        _cam.Setup(_game, _player);
        _props = new PropsView { Name = "Props" };
        AddChild(_props);
        _props.Setup(_game);
        _docks = new NestDocksView { Name = "NestDocks" };
        AddChild(_docks);
        _docks.Setup();
        _occ = Image.CreateEmpty(128, 64, false, Image.Format.R8);
        _occTex = ImageTexture.CreateFromImage(_occ);
        WorldBuilder.GrassMat?.SetShaderParameter("occupancy", _occTex);
        _bv = new BuildingsView { Name = "Buildings" };
        AddChild(_bv);
        _bv.Setup(_game);
        _build = new BuildController { Name = "Build" };
        AddChild(_build);
        _build.Setup(_game, _cam);
        _build.Message += m => _hud.Feed(m, new Color("#ffcf9a"), "warn");
        _hud.BuildToolbar();
        _hud.ToolPicked += m => _build.SetMode(m);
        _hud.SideAction += a =>
        {
            switch (a)
            {
                case "shop": _hud.ToggleShop(); break;
                case "prices": _hud.TogglePrices(); break;
                case "help": _hud.ToggleHelp(); break;
                case "xray": _hud.Feed(_xray.Cycle(), null, "eye"); _hud.SetSideOn("xray", _xray.Mode != 0); break;
                case "sound": Sfx.Muted = !Sfx.Muted; _hud.SetSideOn("sound", false, Sfx.Muted ? "mute" : "sound"); break;
            }
        };

        if (_args.TryGetValue("cam", out var c))
        {
            var f = c.Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            _cam.Override = new Vector3(f[0], f[1], f[2]);
            _cam.OverrideLook = new Vector3(f[3], f[4], f[5]);
        }
        // test : caméra face au panneau du gâteau k (--signcam=k[,distance])
        if (_args.TryGetValue("signcam", out var scs))
        {
            var a = scs.Split(',');
            int k = int.Parse(a[0]);
            float dist = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 6;
            var (sp, sd) = Props.SignOf(k);
            var side = new Vector2(-sd.Y, sd.X);
            var eye = sp + sd * dist + side * dist * 0.25f;
            _cam.Override = new Vector3(eye.X, 2.6f, eye.Y);
            _cam.OverrideLook = new Vector3(sp.X, 2.0f, sp.Y);
            _args["cam"] = "1"; // garde la caméra imposée au lancement de la partie
        }
        // réglage de l'environnement en test : --env=tonemap_mode:3,tonemap_exposure:1.1
        if (_args.TryGetValue("env", out var envs))
        {
            var env = GetNodeOrNull<WorldEnvironment>("Env")?.Environment;
            foreach (var kv in envs.Split(','))
            {
                var p = kv.Split(':');
                if (env != null && p.Length == 2) env.Set(p[0], float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        if (_feveCam is { } fc) { _cam.Override = fc + new Vector3(0.8f, 2.6f, 2.8f); _cam.OverrideLook = fc + new Vector3(0, 0.2f, 0); }
        if (_args.TryGetValue("shot", out var shot))
        {
            float delay = _args.TryGetValue("delay", out var d) ? float.Parse(d, System.Globalization.CultureInfo.InvariantCulture) : 4;
            GetTree().CreateTimer(delay).Timeout += () =>
            {
                var img = GetViewport().GetTexture().GetImage();
                // --crop=x,y,w,h[,zoom] : agrandir une zone (vérifier l'interface au pixel près)
                if (_args.TryGetValue("crop", out var cr))
                {
                    var v = cr.Split(',').Select(t => int.Parse(t)).ToArray();
                    img = img.GetRegion(new Rect2I(v[0], v[1], v[2], v[3]));
                    int z = v.Length > 4 ? v[4] : 2;
                    img.Resize(v[2] * z, v[3] * z, Image.Interpolation.Nearest);
                }
                img.SavePng(shot);
                int dig = _game.Workers.Count(w => w.State == WorkerState.Dig), trav = _game.Workers.Count(w => w.State == WorkerState.Travel);
                GD.Print($"[shot] {shot} · {Engine.GetFramesPerSecond()} fps · fourmi {_game.Player.Pos.X:0.0},{_game.Player.Pos.Y:0.00},{_game.Player.Pos.Z:0.0} · ouvrières {_game.Workers.Count} (creusent {dig}, marchent {trav}) · gagné {Fmt(_game.Earned)} · miettes restantes {Fmt(_game.Cakes[0].Remaining)} · t={_game.PlayTime:0}s · blocs vidés {DugBlocks()} · éboulis {_collapses} · livraisons tapis {_beltSoldN} · bâtiments {string.Join(",", _game.Buildings.Where(b => b.Type != "belt" || !b.Powered || b.It != null).Select(b => $"{b.Type}@{b.X},{b.Z}:{b.State}{(b.Powered ? "" : "!")}"))}");
                GetTree().Quit();
            };
        }
        // tests automatiques : maintenir une action (--press=move_forward) et/ou croquer (--bite)
        if (_args.ContainsKey("fp")) _cam.SetFirstPerson(true);
        // --pos=x,z : poser la fourmi ; --yaw=a : orienter la caméra (et donc la marche)
        if (_args.TryGetValue("pos", out var ps))
        {
            var q = ps.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            _game.Player.Pos = new Vector3(q[0], Terrain.BaseGroundAt(q[0], q[1]) + 0.05f, q[1]);
        }
        PlayerController.Debug = _args.ContainsKey("plog");
        CakeSim.NoClear = _args.ContainsKey("noclear");
        CakeSim.DoorOnly = _args.ContainsKey("dooronly");
        GameState.DropEach = _args.ContainsKey("dropeach");
        GameState.DropMode = _args.ContainsKey("straightdrop") ? 0 : _args.ContainsKey("pathdrop") ? 2 : 1;
        if (_args.ContainsKey("routes"))
            for (int k = 0; k < NbCakes; k++)
            {
                var r = Layout.RouteFromNest(k);
                bool bad = false;
                foreach (var q in r) for (int j = 0; j < NbCakes; j++) if (j != k && new Vector2(q.X - CakePos[j].X, q.Z - CakePos[j].Y).Length() < Layout.RingR - 1) bad = true;
                GD.Print($"[route] gâteau {k + 1} : {r.Count} points, {Layout.PathLength(r):0} unités{(bad ? " TRAVERSE UN GÂTEAU" : "")}");
            }
        if (_args.TryGetValue("yaw", out var yw)) _cam.Yaw = float.Parse(yw, System.Globalization.CultureInfo.InvariantCulture);
        if (_args.TryGetValue("speed", out var spd)) _simSpeed = float.Parse(spd, System.Globalization.CultureInfo.InvariantCulture);
        if (_args.TryGetValue("press", out var act)) foreach (var a in act.Split(',')) Input.ActionPress(a);
        _xray = new XrayView { Name = "Xray" };
        AddChild(_xray);
        _xray.Setup(_game);
        if (_args.TryGetValue("xray", out var xr) && int.TryParse(xr, out var xn)) for (int i = 0; i < xn; i++) _xray.Cycle();
        if (_args.ContainsKey("trailer")) TrailerSetup();
        _music = new Music { Name = "Music" };
        AddChild(_music);
        _menus = new Menus { Name = "Menus" };
        AddChild(_menus);
        _menus.Setup(_hud, _game, hasSave);
        _menus.SettingsChanged += ApplySettings;
        _menus.StartRequested += isNew =>
        {
            if (isNew)
            {
                GameState.DeleteSave();
                _pendingNew = (_game.Player.Name, _game.Player.Color);
                _resetting = true;
                GetTree().ReloadCurrentScene();
                return;
            }
            BeginPlay();
        };
        ApplySettings();
        bool test = (_args.ContainsKey("shot") || _args.ContainsKey("trailer") || _args.ContainsKey("press") || _args.ContainsKey("cam")) && !_args.ContainsKey("title");
        if (_pendingNew != null || test) { _pendingNew = null; BeginPlay(); }
        else _menus.ShowTitle();
        if (_args.TryGetValue("open", out var tab)) _hud.OpenTab(tab);
        if (_args.ContainsKey("options")) _menus.ShowOptions();
        // test : outil de construction sélectionné (--mode=comp), la souris placée au centre de l'écran
        if (_args.TryGetValue("mode", out var md)) _build.SetMode(md);
        if (_args.ContainsKey("dragtest"))
        {
            // glisser en diagonale : 1 case à la fois puis un saut de 3 cases d'un coup
            _game.UpgLvl["eng"] = 1;
            _build.SetMode("belt");
            BuildController.TestHover = new Vector2I(18, 41);
            _build.Press();
            foreach (var dc in new[] { new Vector2I(19, 42), new Vector2I(20, 43), new Vector2I(21, 44), new Vector2I(24, 47), new Vector2I(25, 47) }) _build.DragTo(dc);
            _build.Release();
            BuildController.TestHover = null;
            GD.Print($"[dragtest] tapis posés : {_game.Buildings.Count(b => b.Type == "belt")} · " + string.Join(" ", _game.Buildings.Where(b => b.Type == "belt").Select(b => $"{b.X},{b.Z}:{b.Dir}")));
        }
        if (_args.ContainsKey("demolishtest"))
        {
            // marteau maintenu : on glisse le long de la rangée de tapis de la démo d'usine (y = 42)
            _build.SetMode("demolish");
            int before = _game.Buildings.Count;
            BuildController.TestHover = new Vector2I(-15, 42);
            _build.Hover = BuildController.TestHover;
            _build.Press();
            for (int x = -14; x <= -9; x++) _build.DragTo(new Vector2I(x, 42));
            _build.DragTo(new Vector2I(-9, 42)); // même case : rien de plus
            _build.Release();
            BuildController.TestHover = null;
            GD.Print($"[demolishtest] démolis {before - _game.Buildings.Count} (attendu 7) · restants sur y=42 entre -15 et -9 : {string.Join(" ", _game.Buildings.Where(b => b.Z == 42 && b.X >= -15 && b.X <= -9).Select(b => $"{b.Type}@{b.X}"))}");
        }
        if (_args.TryGetValue("hover", out var hv)) { var h = hv.Split(','); BuildController.TestHover = new Vector2I(int.Parse(h[0]), int.Parse(h[1])); }
    }

    void ApplySettings()
    {
        var env = GetNodeOrNull<WorldEnvironment>("Env")?.Environment;
        UI.Settings.Apply(GetViewport(), env, GetNodeOrNull<DirectionalLight3D>("Sun"));
        // la densité et les ombres de l'herbe suivent la qualité (reconstruite si elle change)
        if (WorldBuilder.GrassQuality != UI.Settings.Quality) WorldBuilder.RebuildGrass(this, UI.Settings.Quality);
        _hud?.SetFpsVisible(UI.Settings.ShowFps);
        // mesure : l'ANCIEN réglage « rapide » (avant l'optimisation), pour comparer (--legacy)
        if (_args.ContainsKey("legacy") && env != null)
        {
            if (WorldBuilder.GrassQuality != 2) WorldBuilder.RebuildGrass(this, 2); // toute l'herbe, avec ses ombres
            var vp = GetViewport(); vp.Scaling3DScale = 0.85f; vp.Msaa3D = Viewport.Msaa.Msaa2X; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
            env.GlowEnabled = true; env.SsaoEnabled = false; env.SsilEnabled = false;
            var sun = GetNodeOrNull<DirectionalLight3D>("Sun");
            if (sun != null) { sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits; sun.DirectionalShadowMaxDistance = 120; }
            RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
            RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
            Player.CameraRig.LampShadows = true;
        }
        _cam.Sensitivity = UI.Settings.Sensitivity;
    }

    void BeginPlay()
    {
        if (!_args.ContainsKey("cam")) _cam.Override = null;
        _hud.Banner("Crumbants", _game.Crowns > 0 ? L.F("Bon retour, {0} !", PlayerName) + " " + L.P(_game.Crowns, "{0} fève trouvée", "{0} fèves trouvées") : L.F("Bienvenue, {0} ! Croque le gâteau, vends au nid, trouve la fève", PlayerName), 4f);
        if (_game.Crowns == 0 && _game.Earned < 1) _hud.Feed("Clic gauche maintenu sur le gâteau pour croquer, puis rapporte ton sac à la fourmilière.", new Color("#fff0c8"), "harvest");
    }

    bool _resetting;
    bool _managerMet;
    float _simSpeed = 1;
    ulong _beltMsg;
    float _padAimT;
    bool _padHeld;

    // Test visuel : un cratère sur le dessus et une galerie depuis le bord sud
    void Demo()
    {
        var K = _game.Cakes[0];
        for (int b = 0; b < K.Cake.Length; b++)
        {
            var p = K.WPos(b);
            float dx = p.X - 6, dz = p.Z - 22, dy = p.Y - 13;
            bool crater = dx * dx + dz * dz + dy * dy * 2.2f < 64;
            bool tunnel = MathF.Abs(p.X - 2.5f) < 1.1f && p.Y < 2 && p.Z > 14;
            bool room = new Vector2(p.X - 2.5f, p.Z - 16).Length() < 2.6f && p.Y < 3;
            if (crater || tunnel || room) K.Eat(b, uint.MaxValue, "demo");
        }
        K.ProcessStability();
        K.SettleForSave();
    }

    // Test : ouvre un puits jusqu'à la fève du 1er gâteau et y place la caméra
    void FeveView()
    {
        var K = _game.Cakes[0];
        var f = K.WPos(K.Feve);
        for (int b = 0; b < K.Cake.Length; b++)
        {
            var p = K.WPos(b);
            if (b == K.Feve) continue;
            float dx = p.X - f.X, dz = p.Z - f.Z - 1.5f;
            if (dx * dx + dz * dz < 5 && p.Y >= f.Y - 0.5f) K.Eat(b, uint.MaxValue, "demo");
        }
        K.SettleForSave();
        _feveCam = f;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        _args["cam"] = string.Join(",", new[] { f.X + 0.8f, f.Y + 2.6f, f.Z + 2.8f, f.X, f.Y + 0.2f, f.Z }.Select(v => v.ToString(ci)));
        GD.Print($"[feve] {f.X},{f.Y},{f.Z}");
    }

    // Test : une grue au bord du gâteau, des tapis jusqu'au nid, une roue, un compacteur
    // Test : chaque objet (rangées) de chaque gâteau (colonnes), posé sur des tapis à l'arrêt
    void DemoItems()
    {
        _game.UpgLvl["eng"] = 1;
        string[] types = { "crumb", "ball", "small", "royal" };
        for (int r = 0; r < types.Length; r++)
        for (int c = 0; c < NbCakes; c++)
        {
            int x = 18 + c, z = 43 + r;
            if (!_game.Build("belt", x, z, 1).ok) continue;
            var b = _game.At(x, z);
            b.It = new Crumbants.Sim.Item { T = types[r], Q = 1, C = c, Ex = -1, Ez = 0, P = 0.5f };
        }
    }

    void DemoFactory()
    {
        _game.Money = 1e7;
        foreach (var id in new[] { "eng", "planComp", "planOven", "planDeco" }) _game.UpgLvl[id] = 1;
        string Log(string what, (bool ok, string why) r) { if (!r.ok) GD.Print($"[demo] {what} : {r.why}"); return what; }
        // grue 2 × 2 : sa sortie (case avant gauche, vers le sud) donne sur le tapis -15,40
        Log("grue", _game.Build("harv", -16, 38, 2));
        for (int z = 40; z <= 41; z++) Log($"tapis -15,{z}", _game.Build("belt", -15, z, 2));
        for (int x = -15; x <= -2; x++) Log($"tapis {x}", _game.Build("belt", x, 42, 1));
        // descente, puis le dernier tapis arrive EN BIAIS sur la case devant le guichet nord (0,47) :
        // posé dans le sens du geste (vers +x), il se tourne tout seul vers le guichet
        for (int z = 42; z <= 46; z++) Log($"tapis -1,{z}", _game.Build("belt", -1, z, 2));
        Log("tapis -1,47 → est", _game.Build("belt", -1, 47, 1));
        Log("tapis 0,47", _game.Build("belt", 0, 47, 1));
        // un tapis qui rejoint la ligne par le côté (jonction en T)
        for (int z = 46; z >= 43; z--) Log($"tapis -9,{z}", _game.Build("belt", -9, z, 0));
        Log("roue 1", _game.Build("wheel", -17, 40, 0));
        Log("roue 2", _game.Build("wheel", -6, 44, 0));
        Log("roue 3", _game.Build("wheel", 9, 45, 0));
        Log("roue 4", _game.Build("wheel", 4, 43, 0));
        // entrepôt 3 × 3 : sa sortie (avant, vers l'est) donne sur la ligne de tapis
        Log("entrepôt", _game.Build("store", -19, 42, 1));
        for (int x = -17; x <= -16; x++) Log($"tapis {x},42", _game.Build("belt", x, 42, 1));
        // chaîne compacteur → four → décorateur (machines 3 × 3, tapis de liaison d'une case)
        Log("compacteur", _game.Build("comp", 8, 48, 1));
        Log("tapis 10,48", _game.Build("belt", 10, 48, 1));
        Log("four", _game.Build("oven", 12, 48, 1));
        Log("tapis 14,48", _game.Build("belt", 14, 48, 1));
        Log("décorateur", _game.Build("deco", 16, 48, 1));
        Log("tapis 18,48", _game.Build("belt", 18, 48, 1));
        // alimentation du compacteur par son entrée arrière (ouest)
        for (int x = 4; x <= 6; x++) Log($"tapis {x},48", _game.Build("belt", x, 48, 1));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_args.ContainsKey("shot")) _game?.Save();
    }

    public override void _ExitTree()
    {
        if (!_resetting && !_args.ContainsKey("shot") && !_args.ContainsKey("new")) _game?.Save();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_menus.Open)
        {
            if (e.IsActionPressed("cancel")) _menus.TogglePause();
            return;
        }
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed && _cam.FirstPerson && Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
            if (_build.Building && !_cam.FirstPerson) { if (mb.Pressed) _build.Press(); else _build.Release(); }
            else _player.BiteHeld = mb.Pressed;
        }
        if (e is InputEventKey k && k.Pressed && !k.Echo && !_hud.PanelOpen)
        {
            int n = k.PhysicalKeycode switch
            {
                Godot.Key.Key1 or Godot.Key.Kp1 => 1, Godot.Key.Key2 or Godot.Key.Kp2 => 2, Godot.Key.Key3 or Godot.Key.Kp3 => 3,
                Godot.Key.Key4 or Godot.Key.Kp4 => 4, Godot.Key.Key5 or Godot.Key.Kp5 => 5, Godot.Key.Key6 or Godot.Key.Kp6 => 6,
                Godot.Key.Key7 or Godot.Key.Kp7 => 7, Godot.Key.Key8 or Godot.Key.Kp8 => 8, Godot.Key.Key9 or Godot.Key.Kp9 => 9, _ => 0,
            };
            if (n == 1) _build.Mode = "harvest";
            else if (n >= 2 && n <= 8) _build.SetMode(GameState.BuildOrder[n - 2]);
            else if (n == 9) _build.SetMode("demolish");
            if (k.PhysicalKeycode == Godot.Key.R) _build.RotateKey();
        }
        if (e.IsActionPressed("shop")) _hud.ToggleShop();
        else if (e.IsActionPressed("help")) _hud.ToggleHelp();
        else if (e.IsActionPressed("view")) _cam.CycleView();
        else if (e.IsActionPressed("fps")) { UI.Settings.ShowFps = !UI.Settings.ShowFps; UI.Settings.Save(); _hud.SetFpsVisible(UI.Settings.ShowFps); _menus.SyncFps(); }
        else if (e.IsActionPressed("mute")) { Sfx.Muted = !Sfx.Muted; _hud.SetSideOn("sound", false, Sfx.Muted ? "mute" : "sound"); _hud.Feed(Sfx.Muted ? "Son coupé" : "Son activé", null, Sfx.Muted ? "mute" : "sound"); }
        else if (e.IsActionPressed("interact")) TryPickup();
        else if (e.IsActionPressed("xray")) { _hud.Feed(_xray.Cycle(), null, "eye"); _hud.SetSideOn("xray", _xray.Mode != 0); }
        else if (e.IsActionPressed("prices")) _hud.TogglePrices();
        else if (e.IsActionPressed("cancel"))
        {
            if (_hud.PanelOpen) _hud.CloseAll();
            else if (_build.Building) _build.Mode = "harvest";
            else if (Input.MouseMode == Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Visible;
            else _menus.TogglePause();
        }
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton mb0 && mb0.ButtonIndex == MouseButton.Left && !mb0.Pressed) _build?.Release();
        // relâcher le clic n'importe où (même sur l'interface) arrête de croquer
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !mb.Pressed) _player.BiteHeld = false;
    }

    // nom par défaut traduit (« Fourmi » → « Ant »…), sinon le nom choisi
    string PlayerName => _game.Player.Name == "Fourmi" ? L.T("Fourmi") : _game.Player.Name;

    bool NearManager() => new Vector2(_game.Player.Pos.X - Layout.Manager.X, _game.Player.Pos.Z - Layout.Manager.Y).Length() <= Layout.ManagerTalkR;

    void TryPickup()
    {
        if (NearManager() && (_game.FeveObj == null || _game.FeveObj.State != FeveState.Ground))
        {
            if (!ManagerAwake) { _game.Emit(new GameEvent("info") { Text = "Elle dort profondément… Rapporte la fève du gâteau 1 pour la réveiller." }); Sfx.Play("error"); return; }
            _hud.ToggleManager(); _managerMet = true; return;
        }
        var o = _game.FeveObj;
        if (o != null && o.State == FeveState.Ground && (o.Pos - _game.Player.Pos).Length() <= 2.2f) _game.PickupFeve();
    }

    public override void _Process(double delta)
    {
        float dt = (float)Math.Min(delta, 0.1);
        _player.InputLocked = _hud.PanelOpen || _menus.Open;
        _hud.Visible = !_menus.TitleOpen && !Trailer;
        if (_args.ContainsKey("bite")) _player.BiteHeld = true;
        // test : à t secondes, le gâteau 2 s'ouvre et toutes les ouvrières y partent (--to2=60)
        if (!_to2Done && _args.TryGetValue("to2", out var t2s) && _game.PlayTime >= float.Parse(t2s, System.Globalization.CultureInfo.InvariantCulture))
        {
            _to2Done = true;
            _game.Cakes[1].Unlocked = true;
            _game.AllWorkersTo(1);
            GD.Print($"[to2] t={_game.PlayTime:0} toutes au gâteau 2");
        }
        if (_args.ContainsKey("wlog") && (int)(_game.PlayTime / 10) != _wlogT)
        {
            _wlogT = (int)(_game.PlayTime / 10);
            var ws = _game.Workers;
            int nest = ws.Count(w => w.State == WorkerState.Rest && w.At < 0), restCake = ws.Count(w => w.State == WorkerState.Rest && w.At >= 0);
            {
                var K0 = _game.Cakes[0]; int dug = 0, far = 0; var ys = new int[Layers]; var radii = new int[5];
                for (int b = 0; b < K0.Cake.Length; b++)
                {
                    if (K0.Cake[b] != 0 || K0.H0[b % Area] == 0 || b / Area >= K0.H0[b % Area]) continue;
                    dug++; ys[b / Area]++;
                    var wp = K0.WPos(b); float r = new Vector2(wp.X - K0.Cx, wp.Z - K0.Cz).Length();
                    radii[Math.Min(4, (int)(r / 8))]++; if (wp.Z - K0.Cz < -8) far++;
                }
                var sites = _game.Workers.Where(w => w.State == WorkerState.Dig && w.Step != null).Select(w => w.Step.T).Distinct().Count();
                GD.Print($"[ghost] {string.Join(" ", _game.Ghost.Select(kv => kv.Key + "=" + kv.Value))}");
                GD.Print($"[dig] vidés {dug} · côté opposé {far} · par couche {string.Join(",", ys)} · par rayon(0-8-16-24-32+) {string.Join(",", radii)} · chantiers actifs {sites}");
            }
            int still = 0;
            foreach (var w in ws)
            {
                if (_wlogLast.TryGetValue(w.Id, out var lp) && lp.DistanceTo(w.Pos) < 0.05f && !(w.State == WorkerState.Rest && w.At < 0) && w.State != WorkerState.Dig)
                {
                    if (still++ < 4) GD.Print($"[still] #{w.Id} {w.State} at {w.At} P {w.P} site {w.Site} load {w.Load} idle {w.IdleT:0.0} path {w.Path?.Count}/{w.PathI} tag {w.Tag} pos {w.Pos} esc {w.Escaping} step {(w.Step == null ? "-" : w.Step.R.Count.ToString())}");
                }
                _wlogLast[w.Id] = w.Pos;
            }
            if (_game.TripStats.Count > 0) { var ts = _game.TripStats; GD.Print($"[trips] entrepôt↔chantier : {ts.Count} trajets · détour moyen ×{ts.Average():0.00} · pire ×{ts.Max():0.00} · >×2 : {ts.Count(v => v > 2)} · longueur moyenne {_game.TripLen.Average():0} · dépôts nid {_game.DropStats[0]} / entrepôt {_game.DropStats[1]}"); ts.Clear(); _game.TripLen.Clear(); _game.DropStats[0] = _game.DropStats[1] = 0; }
            GD.Print($"[perf] cartes des galeries : {CakeSim.MapCount} calculées en 10 s · {CakeSim.MapMs:0} ms au total ({CakeSim.MapMs / 10:0.0} ms par seconde de jeu)"); CakeSim.MapMs = 0; CakeSim.MapCount = 0;
            GD.Print($"[cakes] vidés par gâteau {string.Join(",", _game.Cakes.Select(K => { int n = 0; for (int b = 0; b < K.Cake.Length; b++) if (K.Cake[b] == 0 && K.H0[b % Area] != 0 && b / Area < K.H0[b % Area]) n++; return n; }))} · affectées {string.Join(",", _game.Alloc)} · immobiles {still} · éboulis {_collapses} · règles {string.Join(",", _game.Cakes.Select(K => K.Relax))} · trophées {_game.TrophyCount} · fève g1 {(_game.FeveObj != null ? "déterrée" : _game.Cakes[0].Feve >= 0 ? "enfouie" : "-")} · restants hors fève (g1) {_game.Cakes[0].Cake.Select((v, b) => v != 0 && b != _game.Cakes[0].Feve ? 1 : 0).Sum()}");
            if (_game.UnloadN > 0) { GD.Print($"[load] charge moyenne déposée {_game.UnloadSum / _game.UnloadN} (capacité {_game.WStats().load}) sur {_game.UnloadN} dépôts"); _game.UnloadSum = 0; _game.UnloadN = 0; }
            GD.Print($"[wlog] t={_game.PlayTime:0} repos-nid {nest} repos-gâteau {restCake} trajet {ws.Count(w => w.State == WorkerState.Travel)} creuse {ws.Count(w => w.State == WorkerState.Dig)} sonnée {ws.Count(w => w.Stun > 0)} · plans ok {_game.PlanOk} vides {_game.PlanNull} · gagné {Fmt(_game.Earned)}");
        }
        // manette : gâchette droite = croquer (visée au centre de l'écran)
        bool pad = Input.IsActionPressed("bite_pad");
        _player.PadAim = pad || (_padAimT > 0);
        if (pad) { _player.BiteHeld = true; _padAimT = 0.3f; _padHeld = true; }
        else if (_padHeld) { _player.BiteHeld = false; _padHeld = false; }
        _padAimT = MathF.Max(0, _padAimT - dt);
        // écran titre : la caméra tourne lentement autour du pique-nique
        if (_menus.TitleOpen)
        {
            // plan cinématique : on tourne lentement autour de la galette, les autres gâteaux sous cloche derrière
            _titleT += dt * 0.035f;
            float ta = 2.6f + _titleT;
            _cam.Override = new Vector3(MathF.Sin(ta) * 66, 26 + MathF.Sin(_titleT * 1.7f) * 4, MathF.Cos(ta) * 66);
            _cam.OverrideLook = new Vector3(MathF.Sin(ta + 0.5f) * 10, 4, MathF.Cos(ta + 0.5f) * 10);
        }
        // simulation à pas fixe (20 Hz), rattrapage jusqu'à 0,5 s
        _acc = Math.Min(_acc + delta * _simSpeed, 0.5 * _simSpeed);
        while (_acc >= Tick) { _game.Tick((float)Tick); _acc -= Tick; }

        if (Trailer) TrailerFrame(dt);
        _player.UpdatePlayer(dt);
        // --camfollow=dx,dy,dz : caméra fixe par rapport à la fourmi (tests d'escalade)
        if (_args.TryGetValue("camfollow", out var cf))
        {
            var q = cf.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            _cam.Override = _game.Player.Pos + new Vector3(q[0], q[1], q[2]); _cam.OverrideLook = _game.Player.Pos + new Vector3(0, 0.3f, 0);
        }
        if (_args.ContainsKey("camcrane") && BuildingsView.LastCraneRing is { } rg) { _cam.Override = rg + new Vector3(1.6f, 0.2f, 1.4f); _cam.OverrideLook = rg + new Vector3(0, -0.5f, 0); }
        _cam.UpdateCamera(dt);
        foreach (var tp in _toppings) tp.UpdateFrame(dt, _game.Player.Pos, _fx);
        _player.UpdateAim();
        _player.UpdateHarvest(dt);
        HandleEvents();

        // tranches modifiées : le gâteau le plus proche d'abord, 4 par image au plus
        var me = _game.Player.Pos;
        int budget = 4;
        foreach (var v in _views.OrderBy(v => new Vector2(v.Sim.Cx - me.X, v.Sim.Cz - me.Z).LengthSquared()))
        {
            v.CollectDirty();
            budget -= v.Flush(budget);
        }

        bool showAim = !Trailer && !_hud.PanelOpen && !_cam.FirstPerson && _player.Aim != null && !_build.Building;
        _fx.SetAim(showAim ? new AimHitView { X = _player.Aim.Value.X, Y = _player.Aim.Value.Y, Z = _player.Aim.Value.Z, InRange = _player.Aim.Value.InRange } : null);
        _fx.UpdateFrame(dt, _cam.Camera.GlobalPosition);
        _time += dt;
        // test : des miettes posées sur le tapis du guichet nord (--feed)
        // test : la chaîne d'usines de --factory alimentée en miettes (entrée arrière du compacteur)
        if (_args.ContainsKey("feedcomp") && _game.At(4, 48) is { Type: "belt" } fcb && fcb.It == null)
            fcb.It = new Crumbants.Sim.Item { T = "crumb", Q = 400, Ex = -1, Ez = 0, C = 1, M = ValueMultOfCake(2) };
        if (_args.ContainsKey("feed") && _game.At(-15, 40) is { } fb && fb.It == null && (int)(_time * 1.5f) % 2 == 0)
        {
            int fc = (int)(_time / 3) % NbCakes; // miettes de chaque gâteau tour à tour (couleurs, valeurs)
            fb.It = new Crumbants.Sim.Item { T = "crumb", Q = 5, Ex = 0, Ez = -1, C = fc, M = ValueMultOfCake(fc + 1) };
        }
        StaffPoses();
        _docks.UpdateFrame(dt);
        _build.UpdateFrame(_hud.PanelOpen, _game.Player.Pos);
        _bv.UpdateFrame(dt, (float)(_acc / Tick), _wv.StaffPoses);
        _wv.UpdateFrame(dt, (float)(_acc / Tick));
        _hud.UpdateToolbar(_build.Mode);
        _props.UpdateFrame();
        _xray.UpdateFrame(dt);
        _wv.SetXray(_xray.Ants);
        _music.Underground = Mathf.Lerp(_music.Underground, _player.Underground() ? 1 : 0, Mathf.Min(1, dt * 2));
        if (_occGen != _game.Buildings.Count * 7 + _game.PowerGen)
        {
            // l'herbe ne pousse pas à travers les bâtiments
            _occGen = _game.Buildings.Count * 7 + _game.PowerGen;
            _occ.Fill(new Color(0, 0, 0));
            // le bureau, la chaise et le portique du nid
            for (int z = -2; z <= 3; z++) for (int x = -3; x <= 3; x++)
            {
                int ox = (int)Layout.Manager.X + x + 64, oz = (int)Layout.Manager.Y + z - 36;
                if (ox >= 0 && oz >= 0 && ox < 128 && oz < 64) _occ.SetPixel(ox, oz, new Color(1, 0, 0));
            }
            // les guichets du nid et leurs tunnels
            foreach (var (cell, o) in NestDocks)
                for (int k = -1; k <= 3; k++) for (int s2 = -1; s2 <= 1; s2++)
                {
                    int x = cell.X - o.X * k + (o.Y != 0 ? s2 : 0) + 64, z = cell.Y - o.Y * k + (o.X != 0 ? s2 : 0) - 36;
                    if (x >= 0 && z >= 0 && x < 128 && z < 64) _occ.SetPixel(x, z, new Color(1, 0, 0));
                }
            foreach (var b in _game.Buildings)
            {
                int ox = b.X + 64, oz = b.Z - 36;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx != 0 || dz != 0) && b.Type == "belt") continue;
                    int x = ox + dx, z = oz + dz;
                    if (x >= 0 && z >= 0 && x < 128 && z < 64) _occ.SetPixel(x, z, new Color(1, 0, 0));
                }
            }
            _occTex.Update(_occ);
        }
        var tip = _hud.PanelOpen || _cam.FirstPerson ? null : _build.HoverTip();
        if (tip is { } tt) _hud.SetTip(tt.title, tt.body, tt.state, tt.kind, tt.icon, GetViewport().GetMousePosition());
        else _hud.SetTip(null, null, null, null, null, Vector2.Zero);
        WorldHints();
        _hud.SetCrosshair(_cam.FirstPerson, _player.Aim?.InRange == true);
        WorldBuilder.GrassMat?.SetShaderParameter("player_pos", _player.DisplayPos);
        UpdateHint();
        _hud.UpdateHud(dt);

        _saveT += delta;
        if (_saveT > 30 && !_args.ContainsKey("shot")) { _saveT = 0; _game.Save(); }
    }

    // La gestionnaire dort tant que la fève du gâteau 1 n'est pas rapportée
    bool ManagerAwake => _game.Cakes.Count(k => k.Unlocked) > 1;
    Node3D _glasses, _pencil;
    Label3D _zzz;

    void BuildManagerProps()
    {
        var gold = new StandardMaterial3D { AlbedoColor = new Color("#e2b44c"), Metallic = 0.9f, Roughness = 0.25f };
        var lens = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.95f, 1f, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.02f, MetallicSpecular = 1 };
        _glasses = new Node3D { Name = "Glasses" };
        foreach (int sd in new[] { -1, 1 })
        {
            // monture ronde devant chaque œil, verre légèrement bombé
            _glasses.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.075f, OuterRadius = 0.095f, Rings = 24, RingSegments = 8 }, MaterialOverride = gold,
                Position = new Vector3(sd * 0.1f, 0.03f, 0.2f), Rotation = new Vector3(MathF.PI / 2, 0, sd * 0.35f) });
            _glasses.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.085f, Height = 0.04f, RadialSegments = 16, Rings = 4 }, MaterialOverride = lens,
                Position = new Vector3(sd * 0.1f, 0.03f, 0.2f), Rotation = new Vector3(MathF.PI / 2, 0, sd * 0.35f) });
            // branche jusqu'à l'arrière de la tête
            _glasses.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 0.2f, RadialSegments = 6 }, MaterialOverride = gold,
                Position = new Vector3(sd * 0.175f, 0.04f, 0.1f), Rotation = new Vector3(MathF.PI / 2, 0, 0) });
        }
        _glasses.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 0.06f, RadialSegments = 6 }, MaterialOverride = gold,
            Position = new Vector3(0, 0.05f, 0.22f), Rotation = new Vector3(0, 0, MathF.PI / 2) });
        AddChild(_glasses);
        // crayon calé derrière l'antenne : bois jaune, mine, bague dorée, gomme rose
        _pencil = new Node3D { Name = "Pencil" };
        var yel = new StandardMaterial3D { AlbedoColor = new Color("#f2c21c"), Roughness = 0.5f };
        _pencil.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.32f, RadialSegments = 6 }, MaterialOverride = yel });
        _pencil.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.022f, Height = 0.06f, RadialSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#e8cfa0") }, Position = new Vector3(0, 0.19f, 0) });
        _pencil.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.024f, BottomRadius = 0.024f, Height = 0.03f, RadialSegments = 10 }, MaterialOverride = gold, Position = new Vector3(0, -0.175f, 0) });
        _pencil.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.023f, BottomRadius = 0.023f, Height = 0.04f, RadialSegments = 10 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("#e98aa0"), Roughness = 0.8f }, Position = new Vector3(0, -0.21f, 0) });
        AddChild(_pencil);
        _zzz = new Label3D { Text = "z", FontSize = 96, Modulate = new Color("#e9e4ff"), OutlineSize = 14, OutlineModulate = new Color("#4a3a8a"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Font = UI.UiSkin.Bold, NoDepthTest = false, PixelSize = 0.0035f };
        AddChild(_zzz);
    }

    void StaffPoses()
    {
        if (_glasses == null) BuildManagerProps();
        bool awake = ManagerAwake;
        var me = _game.Player.Pos;
        float yaw = awake ? MathF.Atan2(me.X - Layout.Manager.X, me.Z - (Layout.Manager.Y - 1.4f)) : MathF.PI;
        // endormie : tête posée sur le bureau, respiration lente
        var pose = new AntPose
        {
            Pos = new Vector3(Layout.Manager.X, 0.8f, Layout.Manager.Y + 2.15f), Yaw = Mathf.Clamp(Mathf.Wrap(yaw, -MathF.PI, MathF.PI), -MathF.PI, MathF.PI),
            Pitch = awake ? -0.15f : 0.5f + 0.03f * MathF.Sin(_time * 1.2f), Scale = 1.05f, Time = awake ? _time : _time * 0.3f, Color = new Color("#7b4fb8"),
            Bite = awake ? (MathF.Sin(_time * 1.3f) > 0.95f ? 0.5f : 0) : 0,
        };
        _wv.StaffPoses.Add(pose);
        Transform3D head = default;
        AntRig.Compute(pose, (t, k, tr) => { if (t == AntRig.PartHead) head = tr; });
        _glasses.GlobalTransform = head;
        _pencil.GlobalTransform = head * new Transform3D(new Basis(Vector3.Forward, 1.2f) * new Basis(Vector3.Right, 0.25f), new Vector3(0.16f, 0.12f, 0.02f));
        _zzz.Visible = !awake;
        if (!awake)
        {
            float c = (_time * 0.5f) % 1f;
            _zzz.GlobalPosition = head.Origin + new Vector3(0.2f + c * 0.4f, 0.5f + c * 1.1f, 0);
            _zzz.Modulate = new Color(0.92f, 0.9f, 1f, MathF.Sin(c * MathF.PI));
            _zzz.FontSize = 60 + (int)(c * 50);
            _zzz.Text = c < 0.33f ? "z" : c < 0.66f ? "zZ" : "zZz";
        }
        Staff?.Invoke(_wv.StaffPoses, _time);
    }

    public Action<List<AntPose>, float> Staff;

    Vector2? Screen(Vector3 p, out bool on)
    {
        var cam = _cam.Camera;
        on = !cam.IsPositionBehind(p);
        var s = cam.UnprojectPosition(p);
        var vp = GetViewport().GetVisibleRect().Size;
        // passage des pixels de la fenêtre aux unités de l'interface (mode canvas_items)
        var ui = _hud.Root.Size;
        s *= new Vector2(ui.X / vp.X, ui.Y / vp.Y);
        if (!on) s = ui - s;
        on = on && s.X > 0 && s.Y > 0 && s.X < ui.X && s.Y < ui.Y;
        return s;
    }

    void WorldHints()
    {
        bool canShow = !_hud.PanelOpen && !_menus.Open;
        var o = _game.FeveObj;
        if (canShow && NearManager() && (o == null || o.State != FeveState.Ground))
        {
            var s = Screen(new Vector3(Layout.Manager.X, 2.6f, Layout.Manager.Y + 2.15f), out bool on);
            _hud.SetWorldHint(ManagerAwake ? "Parler à la gestionnaire" : "Elle dort · rapporte la fève n°1", on ? s : null);
        }
        else if (canShow && o != null && o.State == FeveState.Ground && (o.Pos - _game.Player.Pos).Length() <= 2.2f)
            _hud.SetWorldHint("Ramasser la fève", Screen(o.Pos + Vector3.Up, out bool on2) is { } p2 && on2 ? p2 : null);
        else _hud.SetWorldHint(null, null);
        // indicateur de la fève (déterrée, portée, ou signalée par une ouvrière)
        Vector3? target = null; string label = null;
        var me = _game.Player.Pos;
        if (o != null && o.State == FeveState.Ground) { target = o.Pos + Vector3.Up; label = $"{(o.Pos - me).Length():0} m"; }
        else if (o != null) { target = new Vector3(Nest.X, 3, Nest.Y); label = L.F("Au nid : {0} m", new Vector2(Nest.X - me.X, Nest.Y - me.Z).Length().ToString("0")); }
        else if (_game.FeveCake() is { FeveHint: true, Feve: >= 0 } FK) { target = FK.WPos(FK.Feve) + new Vector3(0, 0.5f, 0); label = L.F("Fève signalée · {0} m", (target.Value - me).Length().ToString("0")); }
        if (target != null && canShow)
        {
            var s = Screen(target.Value, out bool on);
            _hud.SetFeveIndicator(label, s, on);
        }
        else _hud.SetFeveIndicator(null, null, false);
    }

    void UpdateHint()
    {
        if (NearManager() && !_hud.PanelOpen) { _hud.SetHint(""); return; }
        var o = _game.FeveObj;
        var me = _game.Player.Pos;
        _hud.SetHint(o != null && o.State == FeveState.Carried ? "Rapporte la fève à la fourmilière !" : "");
    }

    void HandleEvents()
    {
        var me = _game.Player.Pos;
        foreach (var e in _game.Events)
        {
            switch (e.Type)
            {
                case "bite":
                    _fx.Burst(e.Pos, Effects.LayerColor(_game.Cakes[e.Cake], e.N + 0.5f), 12, 1);
                    Sfx.Play("crunch", -4);
                    break;
                case "drop":
                {
                    Sfx.Play("find");
                    var col = e.Text == "pepite" ? new Color("#ffb070") : e.Text == "amande" ? new Color("#ffe0a8") : Colors.White;
                    _fx.Sparkle(e.Pos, col, e.Text == "pepite" ? 25 : 60, 1.2f);
                    _fx.FloatText($"{L.T(e.Text == "pepite" ? "Pépite" : "Amande")} +{Fmt(e.Value)}", e.Pos, col, e.Text == "amande");
                    if (e.Text == "amande") _hud.Feed(L.F("Amande entière : +{0}", Fmt(e.Value)), new Color("#ffe0a8"), "amande");
                    break;
                }
                case "sold":
                    Sfx.Play("coin");
                    _fx.FloatText($"+{Fmt(e.Value)}", e.Pos + new Vector3(0, 1.2f, 0), new Color("#ffd34d"), true);
                    _fx.Sparkle(new Vector3(Nest.X, 2.6f, Nest.Y), new Color("#ffd34d"), 24, 1);
                    break;
                case "bagFull":
                    if (Time.GetTicksMsec() / 1000f - _bagMsgT > 2.5f)
                    {
                        _bagMsgT = Time.GetTicksMsec() / 1000f;
                        Sfx.Play("full");
                        _hud.Feed("Sac plein ! Va vendre à la fourmilière", new Color("#ffcf9a"), "bag");
                        _hud.FlashBag();
                    }
                    break;
                case "feveReveal":
                    Sfx.Play("fanfare");
                    string by = e.Who == "worker" ? "Une ouvrière a déterré la fève !" : e.Who == "harv" ? "Une grue a déterré la fève !" : "Tu as déterré la fève !";
                    _hud.Banner(by, e.Who == "me" ? "Ramasse-la (E) et rapporte-la au nid" : "Va la ramasser (E) et rapporte-la au nid", 3.5f, "feve_found");
                    _fx.Sparkle(e.Pos, new Color("#ffd66b"), 60, 1.5f);
                    break;
                case "feveCrumbs":
                    Sfx.Play("coin", -6, 1.2f);
                    _fx.FloatText($"{L.T("Fève")} +{Fmt(e.Value)}", e.Pos, new Color("#ffe0a8"));
                    break;
                case "fevePickup":
                    Sfx.Play("pickup");
                    _hud.Feed("Tu portes la fève ! Rapporte-la à la fourmilière", new Color("#ffe39a"), "feve_found");
                    break;
                case "feveFound":
                    Sfx.Play("fanfare");
                    _fx.Sparkle(e.Pos + Vector3.Up, new Color("#ffd66b"), 80, 2);
                    _hud.Banner(L.F("Fève n°{0} trouvée !", e.Cake + 1), $"+{Fmt(e.Value)} · " + L.T("une couronne pour la colonie"), 4);
                    if (e.N >= 0)
                    {
                        _hud.Feed(L.F("Nouveau gâteau débloqué : {0}", _game.Cakes[e.N].Theme.Name), new Color("#ffe39a"), "cake");
                        LiftCloche(e.N);
                    }
                    break;
                case "trophy":
                    Sfx.Play("fanfare");
                    _hud.Banner(L.F("Trophée : {0} !", _game.Cakes[e.Cake].Theme.Name), L.F("Gâteau {0} mangé à 100 % · +10 % sur tous les gains (total +{1} %)", e.Cake + 1, e.N * 10), 4.5f, "trophy");
                    _hud.Feed(L.F("Trophée du gâteau {0} : +10 % sur tous les gains", e.Cake + 1), new Color("#ffe39a"), "trophy");
                    break;
                case "enraged":
                    _hud.Feed(L.F("Gâteau {0} : plus de galerie sûre, ses ouvrières sont ENRAGÉES ! Elles l'attaquent par le dessus jusqu'à la dernière miette.", e.Cake + 1), new Color("#ffb09a"), "warn");
                    break;
                case "victory":
                    _hud.Banner("Les six fèves sont à toi !", "La colonie est royale. Tu peux continuer à jouer.", 6);
                    break;
                case "collapse":
                {
                    _collapses += e.N;
                    float d = (e.Pos - me).Length();
                    _cam.Shake(Mathf.Clamp(1.6f - d / 25f, 0, 1.4f));
                    Sfx.Play("rumble", Mathf.Clamp(-d / 4, -24, 0));
                    if (!_collapseSeen && d < 40) { _collapseSeen = true; _hud.Banner("Ça s'effondre !", "Mange par le dessus, ou garde des piliers", 2.5f, "warn"); }
                    break;
                }
                case "land":
                    if ((e.Pos - me).Length() < 40) _fx.Burst(e.Pos + new Vector3(0, 0.2f, 0), new Color("#d8c2a0"), 4, 0.6f);
                    break;
                case "crush":
                    if (e.Who == "me")
                    {
                        _player.StunT = StunMe;
                        _hud.Feed(L.F("Écrasée par un éboulis ! -{0} miettes", Fmt(e.N)), new Color("#ffb0a0"), "warn");
                    }
                    break;
                case "feveHint":
                    Sfx.Play("find");
                    _hud.Banner("Une ouvrière a trouvé la fève !", L.F("Gâteau {0} : suis le halo doré et déterre-la", e.Cake + 1), 3.5f, "feve_hunt");
                    break;
                case "info":
                    _hud.Feed(e.Text, null, "ant_worker");
                    break;
                case "workerBite":
                    if ((e.Pos - me).Length() < 25) _fx.Burst(e.Pos, Effects.LayerColor(_game.Cakes[e.Cake], e.N + 0.5f), 5, 0.6f);
                    break;
                case "deposit":
                    Sfx.Play("coin", -8, 0.8f);
                    _fx.FloatText(L.F("{0} au stock", Fmt(e.N)), e.Pos, new Color("#fff0d0"));
                    break;
                case "rotated":
                    if (e.Who == "key") _bv.FlashArrows(e.N);
                    break;
                case "built":
                    _fx.Burst(e.Pos + new Vector3(0, 0.3f, 0), new Color("#e8d8b8"), 10, 0.8f);
                    break;
                case "beltSold":
                    _docks.Deliver(e.Pos, e.Text, Math.Max(0, e.Cake)); _beltSoldN++;
                    if (Time.GetTicksMsec() - _beltMsg > 350 && (e.Pos - me).Length() < 40)
                    {
                        _beltMsg = Time.GetTicksMsec();
                        _fx.FloatText($"+{Fmt(e.Value)}", e.Pos + new Vector3(0, 1.5f, 0), new Color("#ffd34d"));
                        Sfx.Play("coin", -10);
                    }
                    break;
                case "portIn":
                    _bv.PortIn(e.N, e.Pos, e.Text, Math.Max(0, e.Cake));
                    break;
                case "produced":
                    if ((e.Pos - me).Length() < 30) _fx.Sparkle(e.Pos, e.Text == "royal" ? new Color("#ffd34d") : new Color("#fff0d0"), 12, 0.6f);
                    break;
                case "buy":
                    _hud.Feed(L.F("Achat : {0}", e.Text), null, "cart");
                    break;
            }
        }
        _game.Events.Clear();
    }

    int _collapses;
    int DugBlocks()
    {
        var K = _game.Cakes[0]; int n = 0;
        for (int c = 0; c < Area; c++) for (int y = 0; y < K.H0[c]; y++) if (K.Cake[c + y * Area] == 0) n++;
        return n;
    }

    void LiftCloche(int i)
    {
        if (!WorldBuilder.Cloches.TryGetValue(i, out var c)) return;
        WorldBuilder.Cloches.Remove(i);
        var tw = c.CreateTween();
        tw.TweenProperty(c, "rotation", new Vector3(0.06f, 0, -0.05f), 0.25f);
        tw.TweenProperty(c, "rotation", new Vector3(-0.05f, 0, 0.06f), 0.25f);
        tw.TweenProperty(c, "position", c.Position + new Vector3(0, 160, 0), 3.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(c.QueueFree));
    }
}
