// ─────────────────────────────────────────────────────────────
// Bande-annonce (--trailer) : une suite de plans scriptés (caméra,
// fourmi, radiographie, usine, fève, titre) avec des textes animés,
// pensée pour être enregistrée par le Movie Maker de Godot :
//   Godot… --write-movie C:\tmp\trailer\land\f.png --fixed-fps 30
//          --resolution 1920x1080 --path <projet> -- --new --trailer
//          --factory --feedcomp --workers=40 --train=10 --quality=2
// Le même découpage sert au format portrait (2:3) : la caméra garde
// alors sa largeur de champ (on voit plus haut et plus bas).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Render;
using Crumbants.UI;
using static Crumbants.Cfg;

namespace Crumbants;

public partial class Main
{
    sealed class Shot
    {
        public float Len;
        public string Caption;
        public Action<float> Cam;      // t (0..1) → pose la caméra
        public Action Begin;
    }

    readonly List<Shot> _shots = new();
    int _shotI = -1;
    float _shotT, _trailerT;
    CanvasLayer _tLayer;
    Label _tCap, _tTitle, _tSub;
    ColorRect _tFlash;
    Vector3 _feveSpot;
    bool Trailer => _shots.Count > 0;
    int _played, _frameN;

    static float Ease(float t) => t * t * (3 - 2 * t);
    static Vector3 Lerp3(Vector3 a, Vector3 b, float t) => a.Lerp(b, Ease(Mathf.Clamp(t, 0, 1)));

    // ───── préparation : la partie a « vécu » (galeries, usine en marche), puis les plans ─────
    void TrailerSetup()
    {
        // on fait tourner la simulation hors écran : galeries creusées, tapis chargés
        float pre = _args.TryGetValue("prewarm", out var pw) ? float.Parse(pw, System.Globalization.CultureInfo.InvariantCulture) : 320;
        var t0 = Time.GetTicksMsec();
        for (int i = 0; i < (int)(pre / Tick); i++)
        {
            if (i % 2 == 0 && _game.At(4, 48) is { Type: "belt" } fcb && fcb.It == null)
                fcb.It = new Crumbants.Sim.Item { T = "crumb", Q = 400, Ex = -1, Ez = 0, C = 1, M = ValueMultOfCake(2) };
            _game.Tick((float)Tick);
        }
        _game.Events.Clear();
        // la fève du 1er gâteau : un puits ouvert jusqu'à elle (plan 5)
        var K = _game.Cakes[0];
        if (K.Feve >= 0)
        {
            var f = K.WPos(K.Feve);
            for (int b = 0; b < K.Cake.Length; b++)
            {
                var p = K.WPos(b);
                if (b == K.Feve) continue;
                float dx = p.X - f.X, dz = p.Z - f.Z - 1.5f;
                if (dx * dx + dz * dz < 6 && p.Y >= f.Y - 0.5f) K.Eat(b, uint.MaxValue, "demo");
            }
            // la fève dégagée : elle brille au fond du puits
            K.Eat(K.Feve, uint.MaxValue, "demo");
            K.SettleForSave();
            _feveSpot = _game.FeveObj?.Pos + new Vector3(0, 0.2f, 0) ?? f + new Vector3(0.5f, 0.5f, 0.5f);
        }
        else _feveSpot = new Vector3(0, 8, 0);
        foreach (var v in _views) v.RebuildAll();
        GD.Print($"[trailer] préchauffage {pre:0} s de jeu en {Time.GetTicksMsec() - t0} ms · fève {(K.Feve >= 0 ? "enfouie" : "absente")}");

        // taille de rendu imposée (--winsize=1920x1080) : la fenêtre peut dépasser l'écran
        if (_args.TryGetValue("winsize", out var ws))
        {
            var p = ws.Split('x');
            var win = GetWindow();
            win.Mode = Window.ModeEnum.Windowed;
            win.Unresizable = false;
            win.MaxSize = Vector2I.Zero;
            win.Size = new Vector2I(int.Parse(p[0]), int.Parse(p[1]));
            win.Position = new Vector2I(0, 0);
        }
        GD.Print($"[trailer] fenêtre {GetWindow().Size} · rendu {GetViewport().GetVisibleRect().Size}");
        // portrait : on garde la largeur du champ
        var vs = GetViewport().GetVisibleRect().Size;
        bool portrait = vs.Y > vs.X;
        if (portrait) { _cam.Camera.KeepAspect = Camera3D.KeepAspectEnum.Width; }

        // ── les plans ──
        var start = new Vector3(4, 0, 47);
        // 1. au ras du sol, la fourmi marche vers la galette géante qui se dévoile (par l'ouest :
        //    rien de construit de ce côté)
        var west = new Vector3(-49, 0, -6);
        const float yawE = -MathF.PI / 2;               // marche vers +x
        Vector3 Rel(Vector3 a, float side, float up, float back) => a + new Vector3(-back, up, -side); // repère « vers +x »
        _shots.Add(new Shot
        {
            Len = 3.4f, Caption = "Un gâteau géant…",
            Begin = () =>
            {
                _game.Player.Pos = new Vector3(west.X, Terrain.BaseGroundAt(west.X, west.Z) + 0.05f, west.Z);
                _cam.Yaw = yawE; Input.ActionPress("move_forward");
            },
            Cam = t =>
            {
                var a = _game.Player.Pos;
                var o = Lerp3(new Vector3(1.0f, 0.4f, 2.4f), new Vector3(2.2f, 5.5f, 9.5f), t);
                var l = Lerp3(new Vector3(-0.2f, 0.9f, -6), new Vector3(-0.4f, 10f, -12), t);
                _cam.Override = Rel(a, o.X, o.Y, o.Z);
                _cam.OverrideLook = Rel(a, l.X, l.Y, l.Z);
            },
        });
        // 2. elle croque la paroi : miettes qui volent
        var wall = new Vector3(40.6f, 0, 2.0f);
        _shots.Add(new Shot
        {
            Len = 3.0f, Caption = "Croque-le, miette par miette",
            Begin = () =>
            {
                Input.ActionRelease("move_forward");
                _game.Player.Pos = new Vector3(wall.X, Terrain.BaseGroundAt(wall.X, wall.Z) + 0.05f, wall.Z);
                _player.BiteHeld = true;
            },
            Cam = t =>
            {
                var a = _game.Player.Pos;
                _cam.Override = a + Lerp3(new Vector3(1.9f, 1.25f, 2.3f), new Vector3(1.5f, 0.95f, 1.4f), t);
                _cam.OverrideLook = new Vector3(38.0f, a.Y + 0.8f, wall.Z - 0.2f);
            },
        });
        // 3. radiographie : le réseau de galeries et la colonie qui s'affaire
        _shots.Add(new Shot
        {
            Len = 3.4f, Caption = "Creuse des galeries avec ta colonie",
            Begin = () =>
            {
                _player.BiteHeld = false;
                while (_xray.Mode != 3) _xray.Cycle();
                _game.Player.Pos = new Vector3(start.X, Terrain.BaseGroundAt(start.X, start.Z) + 0.05f, start.Z);
            },
            Cam = t =>
            {
                float a = 0.55f + t * 0.5f;
                float r = Mathf.Lerp(66, 56, Ease(t));
                _cam.Override = new Vector3(MathF.Sin(a) * r, Mathf.Lerp(34, 28, Ease(t)), MathF.Cos(a) * r);
                _cam.OverrideLook = new Vector3(0, 2.5f, 0);
            },
        });
        // 4. la fabrique : grue, tapis, compacteur, four, décorateur, opératrices casquées
        _shots.Add(new Shot
        {
            Len = 3.4f, Caption = "Bâtis ta fabrique à gâteaux",
            Begin = () => { while (_xray.Mode != 0) _xray.Cycle(); },
            Cam = t =>
            {
                // du côté du gâteau (nord de la chaîne), le nid en fond
                float x = Mathf.Lerp(4.5f, 18f, Ease(t));
                _cam.Override = new Vector3(x, Mathf.Lerp(5.2f, 4.0f, Ease(t)), 41.8f);
                _cam.OverrideLook = new Vector3(x + 1.5f, 1.0f, 48.2f);
            },
        });
        // 5. le puits jusqu'à la fève : la caméra plonge vers elle
        _shots.Add(new Shot
        {
            Len = 2.8f, Caption = "Trouve la fève !",
            Cam = t =>
            {
                var f = _feveSpot;
                // on plonge dans le puits creusé au-dessus de la fève
                _cam.Override = f + Lerp3(new Vector3(0.9f, 17f, 4.5f), new Vector3(0.35f, 2.3f, 1.7f), t);
                _cam.OverrideLook = f + new Vector3(0, 0.1f, 0);
            },
        });
        // 6. grand plan final sur le pique-nique, titre
        _shots.Add(new Shot
        {
            Len = 3.6f, Caption = null,
            Cam = t =>
            {
                // grand recul au-dessus de la clairière : tout le pique-nique et ses gâteaux
                _cam.Override = Lerp3(new Vector3(26, 20, 44), new Vector3(34, 52, 66), t);
                _cam.OverrideLook = Lerp3(new Vector3(0, 4, 0), new Vector3(-8, 0, -70), t);
            },
        });

        // 7. (miniatures seulement, pas joué) : la fourmi en héroïne qui croque, la paroi qui la domine
        _played = _shots.Count;
        _shots.Add(new Shot
        {
            Len = 1, Caption = null,
            Begin = () =>
            {
                Input.ActionRelease("move_forward");
                _game.Player.Pos = new Vector3(wall.X, Terrain.BaseGroundAt(wall.X, wall.Z) + 0.05f, wall.Z);
                _player.BiteHeld = true;
                while (_xray.Mode != 0) _xray.Cycle();
            },
            Cam = t =>
            {
                var a = _game.Player.Pos;
                float d = _args.TryGetValue("coverdist", out var cd) ? float.Parse(cd, System.Globalization.CultureInfo.InvariantCulture) : 1;
                _cam.Override = a + new Vector3(-0.9f, 0.32f, 2.5f) * d;
                _cam.OverrideLook = a + new Vector3(-0.8f, 1.35f, -0.2f);
            },
        });

        // ── surcouche : textes et éclairs de coupe ──
        _tLayer = new CanvasLayer { Layer = 60 };
        AddChild(_tLayer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _tLayer.AddChild(root);
        float u = MathF.Min(vs.X, vs.Y);
        Label L(int size, Color col, int outline, Color outCol, float y0, float y1)
        {
            var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontOverride("font", UiSkin.Bold);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", col);
            l.AddThemeConstantOverride("outline_size", outline);
            l.AddThemeColorOverride("font_outline_color", outCol);
            l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.35f));
            l.AddThemeConstantOverride("shadow_offset_y", (int)(size * 0.06f));
            l.AnchorLeft = 0.06f; l.AnchorRight = 0.94f; l.AnchorTop = y0; l.AnchorBottom = y1;
            root.AddChild(l);
            return l;
        }
        var ink = new Color("#3b2414");
        _tCap = L((int)(u * 0.085f), new Color("#fff8ea"), (int)(u * 0.022f), ink, portrait ? 0.70f : 0.70f, portrait ? 0.86f : 0.90f);
        _tTitle = L((int)(u * 0.2f), new Color("#ffc93c"), (int)(u * 0.04f), ink, portrait ? 0.30f : 0.22f, portrait ? 0.48f : 0.52f);
        _tSub = L((int)(u * 0.07f), new Color("#fff8ea"), (int)(u * 0.02f), ink, portrait ? 0.48f : 0.52f, portrait ? 0.58f : 0.66f);
        _tTitle.Text = "La Fève";
        _tSub.Text = "6 gâteaux · 6 fèves · 1 colonie";
        _tTitle.Modulate = _tSub.Modulate = new Color(1, 1, 1, 0);
        _tFlash = new ColorRect { Color = new Color(1, 0.97f, 0.9f, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _tFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_tFlash);
    }

    // ───── image par image ─────
    // miniatures (--cover=plan,t) : la séquence est figée sur un instant d'un plan, titre affiché en haut
    int _holdShot = -1; float _holdT;
    void TrailerFrame(float dt)
    {
        if (_holdShot < 0 && _args.TryGetValue("cover", out var cv))
        {
            var a = cv.Split(',');
            _holdShot = int.Parse(a[0]); _holdT = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
            _shots[_holdShot].Begin?.Invoke();
            _shotI = _holdShot; _shotT = _holdT * _shots[_holdShot].Len;
            _tCap.Text = "";
            // titre en haut de l'image, sous-titre juste dessous
            _tTitle.AnchorTop = 0.03f; _tTitle.AnchorBottom = 0.3f;
            _tSub.AnchorTop = 0.27f; _tSub.AnchorBottom = 0.38f;
            _tSub.Text = _args.TryGetValue("coversub", out var cs) ? cs : "Trouve la fève !";
        }
        if (_holdShot >= 0)
        {
            var hs = _shots[_holdShot];
            hs.Cam(_holdT);
            _padAimT = 1;
            _tTitle.Modulate = _tSub.Modulate = Colors.White;
            _tTitle.PivotOffset = _tTitle.Size / 2; _tTitle.Scale = Vector2.One; _tTitle.Rotation = -0.03f;
            _tFlash.Color = new Color(0, 0, 0, 0);
            return;
        }
        if (_shotI < 0 || _shotT >= _shots[_shotI].Len)
        {
            if (_shotI >= 0) _shotT -= _shots[_shotI].Len;
            _shotI++;
            if (_shotI >= _played) { GetTree().Quit(); return; }
            _shots[_shotI].Begin?.Invoke();
            _tFlash.Color = new Color(1, 0.97f, 0.9f, _shotI == 0 ? 0 : 0.55f);
            _tCap.Text = _shots[_shotI].Caption ?? "";
        }
        var s = _shots[_shotI];
        float t = _shotT / s.Len;
        s.Cam(t);
        _padAimT = 1; // visée au centre de l'écran (croquer)
        // texte du plan : il surgit (léger rebond), tient, puis s'efface
        float cin = Mathf.Clamp((_shotT - 0.15f) / 0.3f, 0, 1), cout = Mathf.Clamp((s.Len - _shotT) / 0.3f, 0, 1);
        float k = MathF.Min(cin, cout);
        float pop = cin < 1 ? 1 + 0.18f * MathF.Sin(cin * MathF.PI) : 1;
        _tCap.Modulate = new Color(1, 1, 1, k);
        _tCap.PivotOffset = _tCap.Size / 2;
        _tCap.Scale = Vector2.One * (0.85f + 0.15f * cin) * pop;
        // titre final
        if (s.Caption == null)
        {
            float a = Mathf.Clamp((_shotT - 0.3f) / 0.45f, 0, 1), b = Mathf.Clamp((_shotT - 0.9f) / 0.4f, 0, 1);
            _tTitle.Modulate = new Color(1, 1, 1, a);
            _tTitle.PivotOffset = _tTitle.Size / 2;
            _tTitle.Scale = Vector2.One * (a < 1 ? 0.6f + 0.4f * a + 0.22f * MathF.Sin(a * MathF.PI) : 1 + 0.015f * MathF.Sin(_shotT * 3));
            _tTitle.Rotation = (1 - a) * -0.12f;
            _tSub.Modulate = new Color(1, 1, 1, b);
        }
        _tFlash.Color = new Color(1, 0.97f, 0.9f, MathF.Max(0, _tFlash.Color.A - dt * 4.5f));
        // capture image par image (--frames=<dossier>, avec --fixed-fps 30) : à la taille réelle de la
        // fenêtre (le Movie Maker de Godot reste à la taille de l'écran)
        if (_args.TryGetValue("frames", out var dir))
            GetViewport().GetTexture().GetImage().SavePng($"{dir}/f{_frameN++:D5}.png");
        _shotT += dt;
        _trailerT += dt;
    }
}
