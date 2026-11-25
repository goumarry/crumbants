// ─────────────────────────────────────────────────────────────
// Menus : écran titre (logo, nom et couleur de la fourmi, continuer
// ou nouvelle partie), pause et options (volume, qualité, plein
// écran, sensibilité). Réglages enregistrés dans user://settings.cfg.
// ─────────────────────────────────────────────────────────────
using System;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;
using static Crumbants.UI.UiSkin;

namespace Crumbants.UI;

public static class Settings
{
    const string Path = "user://settings.cfg";
    public static float Master = 0.8f, Music = 0.55f, Effects = 0.9f, Sensitivity = 1f;
    public static int Quality = 1; // 0 = rapide, 1 = belle, 2 = sublime
    public static bool Fullscreen = false;
    public static bool ShowFps = false;
    public static string Lang = ""; // vide : langue du système

    public static void Load()
    {
        var c = new ConfigFile();
        if (c.Load(Path) != Error.Ok) { L.Set(L.Default()); return; }
        Lang = (string)c.GetValue("general", "lang", "");
        L.Set(string.IsNullOrEmpty(Lang) ? L.Default() : Lang);
        Master = (float)c.GetValue("audio", "master", Master);
        Music = (float)c.GetValue("audio", "music", Music);
        Effects = (float)c.GetValue("audio", "effects", Effects);
        Sensitivity = (float)c.GetValue("input", "sensitivity", Sensitivity);
        Quality = (int)c.GetValue("video", "quality", Quality);
        Fullscreen = (bool)c.GetValue("video", "fullscreen", Fullscreen);
        ShowFps = (bool)c.GetValue("video", "show_fps", ShowFps);
    }

    public static void Save()
    {
        var c = new ConfigFile();
        c.SetValue("audio", "master", Master); c.SetValue("audio", "music", Music); c.SetValue("audio", "effects", Effects);
        c.SetValue("input", "sensitivity", Sensitivity);
        c.SetValue("video", "quality", Quality); c.SetValue("video", "fullscreen", Fullscreen); c.SetValue("video", "show_fps", ShowFps);
        c.SetValue("general", "lang", Lang);
        c.Save(Path);
    }

    public static void Apply(Viewport vp, Godot.Environment env, DirectionalLight3D sun)
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(0.0001f, Master)));
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
        // Trois profils. Rapide : PC modestes (pas de MSAA, FXAA seul, résolution 75 %, ni occlusion,
        // ni lueur, ombres courtes et dures, peu d'herbe). Belle : compromis. Sublime : tout au maximum.
        if (vp != null)
        {
            vp.Scaling3DScale = Quality == 0 ? 0.75f : 1f;
            vp.ScreenSpaceAA = Quality == 0 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
            vp.Msaa3D = Quality >= 2 ? Viewport.Msaa.Msaa8X : Quality == 1 ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        }
        if (env != null)
        {
            env.SsilEnabled = Quality >= 2;
            env.SsaoEnabled = Quality >= 1;
            env.GlowEnabled = Quality >= 1;
            // occlusion : demi-résolution en « belle », pleine en « sublime »
            RenderingServer.EnvironmentSetSsaoQuality(Quality >= 2 ? RenderingServer.EnvironmentSsaoQuality.High : RenderingServer.EnvironmentSsaoQuality.Low,
                Quality < 2, 0.5f, 2, 50, 300);
        }
        if (sun != null)
        {
            sun.DirectionalShadowMode = Quality == 0 ? DirectionalLight3D.ShadowMode.Orthogonal : Quality == 1 ? DirectionalLight3D.ShadowMode.Parallel2Splits : DirectionalLight3D.ShadowMode.Parallel4Splits;
            sun.DirectionalShadowMaxDistance = Quality == 0 ? 70 : Quality == 1 ? 130 : 200;
        }
        RenderingServer.DirectionalShadowAtlasSetSize(Quality == 0 ? 2048 : 4096, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(Quality == 0 ? RenderingServer.ShadowQuality.Hard : Quality == 1 ? RenderingServer.ShadowQuality.SoftLow : RenderingServer.ShadowQuality.SoftMedium);
        RenderingServer.PositionalSoftShadowFilterSetQuality(Quality == 0 ? RenderingServer.ShadowQuality.Hard : RenderingServer.ShadowQuality.SoftLow);
        Player.CameraRig.LampShadows = Quality >= 1; // lampe des galeries : ses ombres = 6 rendus de plus par image
    }
}

public partial class Menus : CanvasLayer
{
    GameState _game;
    Control _root, _title, _pause, _options, _dim;
    CheckButton _fpsCheck;
    public void SyncFps() { if (_fpsCheck != null) _fpsCheck.SetPressedNoSignal(Settings.ShowFps); }
    LineEdit _name;
    int _color;
    HBoxContainer _colors;
    bool _fromTitle;
    TextureRect _crown;
    public bool Open => _title.Visible || _pause.Visible || _options.Visible;
    public bool TitleOpen => _title.Visible;
    public event Action<bool> StartRequested;
    public event Action SettingsChanged;

    public void Setup(Hud hud, GameState game, bool hasSave)
    {
        _game = game;
        Layer = 5;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = BuildTheme() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _dim = new ColorRect { Color = new Color(0.23f, 0.14f, 0.08f, 0.22f), Visible = false };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_dim);
        _color = game.Player.Color;
        BuildTitle(hasSave);
        BuildPause();
        BuildOptions();
    }

    static Label Txt(Control p, string t, int size, Color c, bool center = true)
    {
        var l = new Label { Text = t, HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", Bold);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        p.AddChild(l);
        return l;
    }

    static Button B(Control p, string t, string kind, int size, string icon = null)
    {
        var b = new Button { Text = t, CustomMinimumSize = new Vector2(0, 58) };
        ApplyButton(b, kind, size);
        if (icon != null) { b.Icon = Icon(icon); b.AddThemeConstantOverride("icon_max_width", 34); }
        b.MouseEntered += () => Sfx.Play("jump", -16, 1.6f);
        p.AddChild(b);
        return b;
    }

    Control Screen(out VBoxContainer v, float width)
    {
        var c = new CenterContainer { Visible = false };
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(c);
        v = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        v.AddThemeConstantOverride("separation", 12);
        c.AddChild(v);
        return c;
    }

    PanelContainer CardIn(VBoxContainer parent, out VBoxContainer v)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Box(Cream, null, Cream2, 24, 4, 8, 6, padH: 26, padV: 18));
        parent.AddChild(p);
        v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        p.AddChild(v);
        return p;
    }

    void Logo(VBoxContainer v, string text, int size)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(0, size * 1.5f), MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddChild(holder);
        var t = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        Sticker(t, size, new Color("#ffd84d"), 18, 10);
        t.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(t);
        _crown = IconRect("crown", 84);
        _crown.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _crown.PivotOffset = new Vector2(42, 60);
        holder.AddChild(_crown);
    }

    void BuildTitle(bool hasSave)
    {
        _title = Screen(out var v, 520);
        Logo(v, "Crumbants", 104);
        var pitch = Txt(v, "Un gâteau géant. Des millions de miettes. Une seule fève.", 21, Colors.White);
        Sticker(pitch, 21, Colors.White, 7, 3);
        pitch.AddThemeFontOverride("font", new FontVariation { BaseFont = Bold, SpacingSpace = 4 });
        CardIn(v, out var c);
        Txt(c, "Ta fourmi", 18, Ink, false);
        _name = new LineEdit { Text = _game.Player.Name == "Fourmi" ? L.T("Fourmi") : _game.Player.Name, CustomMinimumSize = new Vector2(0, 50), MaxLength = 18, PlaceholderText = L.T("Son nom") };
        _name.AddThemeFontSizeOverride("font_size", 22);
        c.AddChild(_name);
        _colors = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _colors.AddThemeConstantOverride("separation", 7);
        c.AddChild(_colors);
        for (int i = 0; i < AntColors.Length; i++)
        {
            int k = i;
            var sw = new Button { CustomMinimumSize = new Vector2(44, 44), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, IconAlignment = HorizontalAlignment.Center };
            sw.Pressed += () => { _color = k; Paint(); Sfx.Play("jump", -10, 1.4f); };
            _colors.AddChild(sw);
        }
        Paint();
        if (hasSave)
        {
            var info = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            info.AddThemeConstantOverride("separation", 16);
            c.AddChild(info);
            void Chip(string icon, string text) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 4); info.AddChild(h); h.AddChild(IconRect(icon, 26)); Txt(h, text, 17, InkSoft).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; }
            Chip("coin", Fmt(_game.Money));
            Chip("crown", $"{_game.Crowns} / 6");
            Chip("clock", L.F("{0} min", (int)(_game.PlayTime / 60)));
            B(c, "Continuer", "green", 28).Pressed += () => Start(false);
            var nb = B(c, "Nouvelle partie", "gold", 20);
            nb.Pressed += () => { if (nb.Text == "Nouvelle partie") { nb.Text = "Sûr ? La partie sera effacée"; ApplyButton(nb, "red", 20); } else Start(true); };
        }
        else B(c, "Jouer", "green", 30).Pressed += () => Start(true);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        c.AddChild(row);
        var o = B(row, "Options", "cream", 18, "gear"); o.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        o.Pressed += () => { _title.Visible = false; _options.Visible = true; _fromTitle = true; };
        var q = B(row, "Quitter", "cream", 18, "close"); q.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        q.Pressed += () => GetTree().Quit();
        var tiny = Txt(v, "", 16, Colors.White);
        void TinyText() => tiny.Text = L.F("{0} pour marcher · clic gauche pour croquer · {1} boutique · {2} aide", L.MoveKeys(), L.KeyName(Key.B), L.KeyName(Key.H));
        TinyText();
        L.Changed += TinyText;
        Sticker(tiny, 16, Colors.White, 6, 2);
        tiny.AddThemeFontOverride("font", new FontVariation { BaseFont = Bold, SpacingSpace = 3 });
    }

    void Paint()
    {
        int i = 0;
        foreach (var n in _colors.GetChildren())
        {
            var b = (Button)n;
            bool sel = i == _color;
            var sb = Box(sel ? Gold : Colors.White, null, null, 12, 3, sel ? 5 : 3, 0, padH: 4, padV: 4);
            foreach (var s in new[] { "normal", "hover", "pressed" }) b.AddThemeStyleboxOverride(s, sb);
            // la fourmi de l'icône, recolorée. Le SVG est intégré au code : l'export de Godot n'embarque
            // que la version importée des .svg, jamais le fichier source (les fourmis manquaient)
            var img = new Image();
            img.LoadSvgFromString(AntSvg.Replace("#c0392b", "#" + AntColors[i].ToHtml(false)), sel ? 0.42f : 0.36f);
            b.Icon = ImageTexture.CreateFromImage(img);
            i++;
        }
    }

    // copie de Icons/ant.svg (couleur de base #c0392b remplacée par celle choisie)
    const string AntSvg = @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""96"" height=""96"" viewBox=""0 0 48 48"" aria-hidden=""true""><g stroke=""#3b2414"" stroke-width=""3"" stroke-linejoin=""round"" stroke-linecap=""round""><path d=""M30 25l4 9M24 26l0 10M18 25l-4 9M33 15c1-5 5-8 9-8M30 15c-1-5 0-9 4-11"" fill=""none""/></g><ellipse cx=""13"" cy=""25"" rx=""9"" ry=""7"" fill=""#c0392b"" stroke=""#3b2414"" stroke-width=""3"" stroke-linejoin=""round"" stroke-linecap=""round""/><ellipse cx=""24"" cy=""23"" rx=""5"" ry=""4"" fill=""#c0392b"" stroke=""#3b2414"" stroke-width=""3"" stroke-linejoin=""round"" stroke-linecap=""round""/><circle cx=""33"" cy=""19"" r=""6"" fill=""#c0392b"" stroke=""#3b2414"" stroke-width=""3"" stroke-linejoin=""round"" stroke-linecap=""round""/><circle cx=""35"" cy=""18"" r=""1.6"" fill=""#fff""/><path d=""M9 21c2-2 5-2 7-1"" stroke=""#fff"" stroke-opacity="".7"" stroke-width=""3"" stroke-linecap=""round"" fill=""none""/></svg>";

    void Start(bool fresh)
    {
        _game.Player.Name = string.IsNullOrWhiteSpace(_name.Text) ? L.T("Fourmi") : _name.Text.Trim();
        _game.Player.Color = _color;
        _title.Visible = false;
        Sfx.Play("buy");
        StartRequested?.Invoke(fresh);
    }

    public void ShowTitle() => _title.Visible = true;
    public void ShowOptions() { _title.Visible = false; _options.Visible = true; _fromTitle = true; } // test : --options

    public override void _Process(double delta)
    {
        if (_crown != null && _title.Visible)
        {
            float t = Time.GetTicksMsec() / 1000f;
            _crown.Rotation = MathF.Sin(t * 2.6f) * 0.14f;
            var hw = _crown.GetParent<Control>().Size.X;
            _crown.Position = new Vector2(hw / 2 - 42 + 6, -30 - MathF.Abs(MathF.Sin(t * 2.6f)) * 6);
        }
        _dim.Visible = _pause.Visible || _options.Visible;
    }

    void BuildPause()
    {
        _pause = Screen(out var v, 420);
        var t = Txt(v, "Pause", 70, new Color("#ffd84d"));
        Sticker(t, 70, new Color("#ffd84d"), 14, 8);
        CardIn(v, out var c);
        B(c, "Reprendre", "green", 26).Pressed += () => { _pause.Visible = false; GetTree().Paused = false; };
        B(c, "Options", "cream", 20, "gear").Pressed += () => { _pause.Visible = false; _options.Visible = true; _fromTitle = false; };
        B(c, "Sauvegarder et quitter", "gold", 20).Pressed += () => { _game.Save(); GetTree().Quit(); };
    }

    public void TogglePause()
    {
        if (_title.Visible) return;
        if (_options.Visible) { Settings.Save(); _options.Visible = false; _pause.Visible = true; return; }
        _pause.Visible = !_pause.Visible;
        GetTree().Paused = _pause.Visible;
    }

    void BuildOptions()
    {
        _options = Screen(out var v, 640);
        var t = Txt(v, "Options", 64, new Color("#ffd84d"));
        Sticker(t, 64, new Color("#ffd84d"), 14, 8);
        CardIn(v, out var c);
        void Slider(string label, string icon, float val, Action<float> set)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
            c.AddChild(row);
            row.AddChild(IconRect(icon, 32));
            var l = Txt(row, label, 19, Ink, false); l.CustomMinimumSize = new Vector2(210, 0); l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            var s = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = val, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            s.ValueChanged += x => { set((float)x); SettingsChanged?.Invoke(); };
            row.AddChild(s);
        }
        Slider("Volume général", "sound", Settings.Master, x => Settings.Master = x);
        Slider("Musique", "sparkle", Settings.Music, x => Settings.Music = x);
        Slider("Effets sonores", "crumb", Settings.Effects, x => Settings.Effects = x);
        Slider("Sensibilité souris", "eye", Settings.Sensitivity / 2, x => Settings.Sensitivity = Mathf.Max(0.1f, x * 2));
        var q = new HBoxContainer(); q.AddThemeConstantOverride("separation", 8);
        c.AddChild(q);
        q.AddChild(IconRect("star", 32));
        var ql = Txt(q, "Qualité", 19, Ink, false); ql.CustomMinimumSize = new Vector2(214, 0); ql.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var names = new[] { "Rapide", "Belle", "Sublime" };
        var btns = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            btns[i] = B(q, names[i], Settings.Quality == i ? "gold" : "cream", 16);
            btns[i].CustomMinimumSize = new Vector2(100, 46);
            btns[i].Pressed += () =>
            {
                Settings.Quality = k;
                for (int j = 0; j < 3; j++) ApplyButton(btns[j], j == k ? "gold" : "cream", 16);
                SettingsChanged?.Invoke();
            };
        }
        var fs = new CheckButton { Text = "Plein écran", ButtonPressed = Settings.Fullscreen, FocusMode = Control.FocusModeEnum.None };
        fs.AddThemeFontSizeOverride("font_size", 19);
        fs.Toggled += on => { Settings.Fullscreen = on; SettingsChanged?.Invoke(); };
        c.AddChild(fs);
        var fps = new CheckButton { Text = "Afficher les FPS (F3)", ButtonPressed = Settings.ShowFps, FocusMode = Control.FocusModeEnum.None };
        fps.AddThemeFontSizeOverride("font_size", 19);
        fps.Toggled += on => { Settings.ShowFps = on; SettingsChanged?.Invoke(); };
        c.AddChild(fps);
        _fpsCheck = fps;
        // langue : prise en compte tout de suite (les textes se retraduisent)
        var lr = new HBoxContainer(); lr.AddThemeConstantOverride("separation", 8);
        c.AddChild(lr);
        lr.AddChild(IconRect("help", 32));
        var ll = Txt(lr, "Langue", 19, Ink, false); ll.CustomMinimumSize = new Vector2(120, 0); ll.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var lbtns = new Button[L.Codes.Length];
        for (int i = 0; i < L.Codes.Length; i++)
        {
            int k = i;
            lbtns[i] = B(lr, L.Names[i], L.Codes[i] == L.Code ? "gold" : "cream", 15);
            lbtns[i].AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled; // chaque langue écrite dans sa langue
            lbtns[i].CustomMinimumSize = new Vector2(96, 46);
            lbtns[i].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            lbtns[i].Pressed += () =>
            {
                Settings.Lang = L.Codes[k];
                L.Set(L.Codes[k]);
                for (int j = 0; j < lbtns.Length; j++) ApplyButton(lbtns[j], j == k ? "gold" : "cream", 15);
                Settings.Save();
                SettingsChanged?.Invoke();
            };
        }
        B(c, "Retour", "green", 22).Pressed += () =>
        {
            Settings.Save();
            _options.Visible = false;
            if (_fromTitle) _title.Visible = true; else _pause.Visible = true;
        };
    }
}
