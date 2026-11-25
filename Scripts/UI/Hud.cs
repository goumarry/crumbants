// ─────────────────────────────────────────────────────────────
// Interface de jeu (style « autocollant », cf. UiSkin) :
// pastilles du haut (pièces, sac, gâteau), collier de fèves,
// encadrés Miettes récoltées et Colonie, boutons latéraux, barre d'outils,
// fil d'actualité, bannières, infobulles, invites dans le monde,
// et les fenêtres : boutique, prix, aide, gestionnaire.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;
using static Crumbants.UI.UiSkin;

namespace Crumbants.UI;

// Pastille avec une grande icône qui déborde à gauche
public partial class StatPill : Container
{
    public PanelContainer Panel;
    public TextureRect Ico;
    public int Overlap = 22;
    public override Vector2 _GetMinimumSize()
    {
        var m = Panel?.GetCombinedMinimumSize() ?? Vector2.Zero;
        return new Vector2(m.X + Overlap, MathF.Max(m.Y, Ico?.CustomMinimumSize.Y ?? 0));
    }
    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren || Panel == null) return;
        FitChildInRect(Panel, new Rect2(Overlap, 0, Size.X - Overlap, Size.Y));
        var s = Ico.CustomMinimumSize;
        FitChildInRect(Ico, new Rect2(0, (Size.Y - s.Y) / 2 - 2, s.X, s.Y));
    }
}

public partial class Hud : CanvasLayer
{
    GameState _game;
    public Font Font => UiSkin.Font;
    public Font FontBold => UiSkin.Bold;
    public Control Root;
    public event Action ResetRequested;
    public event Action<string> ToolPicked;
    public event Action<string> SideAction;
    public Func<bool> _extraOpen;
    public Action CloseExtra;

    Label _money, _income, _bag, _cakeName, _cakeSub;
    ProgressBar _bagBar, _cakeBar;
    StatPill _moneyPill, _bagPill;
    HBoxContainer _necklace;
    readonly List<TextureRect> _feves = new();
    VBoxContainer _revRows, _colRows;
    VBoxContainer _feed;
    HBoxContainer _tools;
    readonly List<(string mode, PanelContainer tile, Label cost, TextureRect ico)> _toolSlots = new();
    Label _hint, _banner, _bannerSub;
    Control _bannerBox;
    TextureRect _bannerIco;
    PanelContainer _tip, _worldHint, _feveInd;
    Label _tipTitle, _tipBody, _tipState, _worldHintL, _feveIndL;
    TextureRect _tipIco, _feveIndArrow;
    Crosshair _crosshair;
    readonly Dictionary<string, PanelContainer> _side = new();
    Control _topBar, _neck, _revBox, _colBox, _sideBox, _tray;
    Control _overlay;
    PanelContainer _window;
    HBoxContainer _tabs;
    VBoxContainer _body;
    string _tab;
    int _pricesCake;
    float _slow, _moneyPulse;
    double _lastMoney;

    public bool PanelOpen => _overlay.Visible || (_extraOpen?.Invoke() ?? false);

    public void Setup(GameState game)
    {
        _game = game;
        Init();
        Root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = BuildTheme() };
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Root);
        BuildTop();
        BuildBoxes();
        BuildSide();
        BuildFeed();
        BuildMisc();
        BuildFps();
        BuildWindow();
        _lastMoney = game.Money;
    }

    // ───── Briques ─────
    static Label Txt(Control parent, string text, int size = 18, Color? col = null, bool bold = true)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col ?? Ink);
        if (bold) l.AddThemeFontOverride("font", Bold);
        parent?.AddChild(l);
        return l;
    }

    static Button Btn(Control parent, string text, string kind = "gold", int size = 18, string icon = null, int iconSize = 26)
    {
        var b = new Button { Text = text };
        ApplyButton(b, kind, size);
        if (icon != null) { b.Icon = Icon(icon); b.ExpandIcon = false; b.AddThemeConstantOverride("icon_max_width", iconSize); }
        b.MouseEntered += () => Sfx.Play("jump", -18, 1.8f);
        parent?.AddChild(b);
        return b;
    }

    static PanelContainer Panel(Control parent, StyleBox sb)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", sb);
        parent?.AddChild(p);
        return p;
    }

    static StatPill Pill(Control parent, string icon, int iconSize, out VBoxContainer content, int overlap = 22)
    {
        var w = new StatPill { MouseFilter = Control.MouseFilterEnum.Ignore, Overlap = overlap };
        var sb = Box(Cream, null, Cream2, 18, 3, 4, 4, padH: 14, padV: 4);
        sb.ContentMarginLeft = iconSize - overlap + 8;
        w.Panel = Panel(w, sb);
        w.Ico = IconRect(icon, iconSize);
        w.AddChild(w.Ico);
        content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", -3);
        w.Panel.AddChild(content);
        parent?.AddChild(w);
        return w;
    }

    // bandeau doré d'en-tête : arrondi en haut seulement, posé à l'intérieur du contour du cadre
    static StyleBoxFlat HeaderBand(int radius, int padH, int padV)
    {
        var sb = new StyleBoxFlat { BgColor = new Color("#ffd65e"), AntiAliasing = true };
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = radius;
        sb.ContentMarginLeft = sb.ContentMarginRight = padH;
        sb.ContentMarginTop = sb.ContentMarginBottom = padV;
        return sb;
    }

    static ProgressBar Meter(Control parent, float width, Color a, Color b)
    {
        var m = new ProgressBar { ShowPercentage = false, MaxValue = 1, CustomMinimumSize = new Vector2(width, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        m.AddThemeStyleboxOverride("background", Gauge(new Color("#eadcc0")));
        m.AddThemeStyleboxOverride("fill", Gauge(a.Lerp(b, 0.45f)));
        parent?.AddChild(m);
        return m;
    }

    // ───── Haut : pièces, sac, gâteau, collier de fèves ─────
    void BuildTop()
    {
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 16);
        top.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        top.GrowHorizontal = Control.GrowDirection.Both;
        top.Position = new Vector2(0, 12);
        Root.AddChild(top);
        _topBar = top;

        _moneyPill = Pill(top, "coin", 60, out var mv, 26);
        _moneyPill.Panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _moneyPill.Panel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _moneyPill.Panel.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OpenTab("prices"); };
        _money = Txt(mv, "0", 30, MoneyText);
        _money.CustomMinimumSize = new Vector2(118, 0);
        _income = Txt(mv, "+0 /s", 15, Green2);

        _bagPill = Pill(top, "bag", 50, out var bv);
        _bag = Txt(bv, "0 / 3", 22);
        _bagBar = Meter(bv, 120, new Color("#ffb35c"), new Color("#e8743b"));

        Pill(top, "cake", 50, out var cv);
        cv.AddThemeConstantOverride("separation", 0);
        _cakeName = Txt(cv, "Gâteau 1", 18);
        _cakeSub = Txt(cv, "", 14, InkSoft);
        _cakeBar = Meter(cv, 190, new Color("#ffe07a"), Gold2);

        var neck = Panel(Root, Box(new Color(1, 0.97f, 0.92f), null, null, 17, 3, 3, 0, 0.92f, padH: 10, padV: 4));
        neck.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        neck.GrowHorizontal = Control.GrowDirection.Both;
        _neck = neck;
        _necklace = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _necklace.AddThemeConstantOverride("separation", 2);
        neck.AddChild(_necklace);
        for (int i = 0; i < NbCakes; i++)
        {
            var t = IconRect("feve_locked", 34);
            t.PivotOffset = new Vector2(17, 17);
            _necklace.AddChild(t);
            _feves.Add(t);
        }
    }

    // ───── Encadrés « Revenus » et « Colonie » ─────
    PanelContainer InfoBox(string title, string icon, out VBoxContainer rows)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Box(Cream, null, Cream2, 18, 3, 4, 4, padH: 3, padV: 3));
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 0);
        p.AddChild(v);
        var head = Panel(v, HeaderBand(15, 10, 4));
        var hh = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hh.AddThemeConstantOverride("separation", 6);
        head.AddChild(hh);
        hh.AddChild(IconRect(icon, 24));
        Txt(hh, title, 17);
        v.AddChild(new ColorRect { Color = Ink, CustomMinimumSize = new Vector2(0, 3), MouseFilter = Control.MouseFilterEnum.Ignore });
        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var k in new[] { "margin_left", "margin_right" }) pad.AddThemeConstantOverride(k, 10);
        pad.AddThemeConstantOverride("margin_top", 5); pad.AddThemeConstantOverride("margin_bottom", 2);
        v.AddChild(pad);
        rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(200, 0) };
        rows.AddThemeConstantOverride("separation", 2);
        pad.AddChild(rows);
        return p;
    }

    static void Row(VBoxContainer rows, string icon, string label, string value, Color? valCol = null)
    {
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 6);
        rows.AddChild(h);
        if (icon != null) h.AddChild(IconRect(icon, 22));
        var l = Txt(h, label, 15, InkSoft);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Txt(h, value, 16, valCol ?? Ink);
    }

    void BuildBoxes()
    {
        var rev = InfoBox("Miettes récoltées", "crumb", out _revRows);
        Root.AddChild(rev);
        _revBox = rev;
        var col = InfoBox("Colonie", "ant", out _colRows);
        Root.AddChild(col);
        _colBox = col;
    }

    // ───── Boutons latéraux ─────
    void BuildSide()
    {
        var side = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        side.AddThemeConstantOverride("separation", 14);
        side.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        side.GrowHorizontal = Control.GrowDirection.Begin;
        side.GrowVertical = Control.GrowDirection.Both;
        Root.AddChild(side);
        _sideBox = side;
        foreach (var (id, icon, key, tip) in new[] { ("shop", "cart", L.KeyName(Key.B), "Boutique"), ("prices", "tag", L.KeyName(Key.P), "Prix"), ("xray", "eye", L.KeyName(Key.G), "Radiographie"), ("sound", "sound", L.KeyName(Key.M), "Son"), ("help", "help", L.KeyName(Key.H), "Aide") })
        {
            var p = new PanelContainer { CustomMinimumSize = new Vector2(62, 62), TooltipText = tip, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            p.AddThemeStyleboxOverride("panel", Box(Cream, null, Cream2, 16, 3, 4, 4, padH: 6, padV: 4));
            p.AddChild(IconRect(icon, 40));
            var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddChild(holder);
            var k = Kbd(key);
            k.Position = new Vector2(34, 36);
            holder.AddChild(k);
            string i2 = id;
            p.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { Sfx.Play("jump", -12, 1.5f); SideAction?.Invoke(i2); } };
            p.PivotOffset = new Vector2(31, 31);
            p.MouseEntered += () => { var tw = p.CreateTween(); tw.TweenProperty(p, "rotation", -0.06f, 0.08f); };
            p.MouseExited += () => { var tw = p.CreateTween(); tw.TweenProperty(p, "rotation", 0f, 0.1f); };
            side.AddChild(p);
            _side[id] = p;
        }
    }

    public void SetSideOn(string id, bool on, string icon = null)
    {
        if (!_side.TryGetValue(id, out var p)) return;
        p.AddThemeStyleboxOverride("panel", on ? Box(new Color("#fff3b0"), Gold, Gold2, 16, 3, 4, 4, padH: 6, padV: 4) : Box(Cream, null, Cream2, 16, 3, 4, 4, padH: 6, padV: 4));
        if (icon != null && p.GetChild(0) is TextureRect tr) tr.Texture = Icon(icon);
    }

    // ───── Fil d'actualité ─────
    void BuildFeed()
    {
        _feed = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        _feed.AddThemeConstantOverride("separation", 5);
        _feed.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _feed.GrowVertical = Control.GrowDirection.Begin;
        _feed.Position = new Vector2(12, -128);
        Root.AddChild(_feed);
    }

    public void Feed(string text, Color? c = null, string icon = null)
    {
        var p = Panel(null, FeedItem());
        p.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 7);
        p.AddChild(h);
        if (icon != null)
        {
            // pastille crème : les icônes (traits encre) restent lisibles sur le fond sombre
            var badge = Panel(h, Gauge(Cream, Ink, 15, 2));
            badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            badge.CustomMinimumSize = new Vector2(30, 30);
            var ic = IconRect(icon, 22);
            ic.SizeFlagsHorizontal = ic.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            badge.AddChild(ic);
        }
        text = L.T(text);
        var l = Txt(h, text, 16, c ?? Colors.White);
        l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (text.Length > 46) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(380, 0); }
        _feed.AddChild(p);
        while (_feed.GetChildCount() > 6) { var o = _feed.GetChild(0); _feed.RemoveChild(o); o.QueueFree(); }
        p.Modulate = new Color(1, 1, 1, 0);
        p.Position += new Vector2(-30, 0);
        var tw = p.CreateTween();
        tw.TweenProperty(p, "modulate:a", 1f, 0.25f);
        tw.TweenInterval(6);
        tw.TweenProperty(p, "modulate:a", 0f, 1.0f);
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(p)) p.QueueFree(); }));
    }

    // ───── Bannière, indications, viseur, infobulle, invites ─────
    void BuildMisc()
    {
        _bannerBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0), Alignment = BoxContainer.AlignmentMode.Center };
        _bannerBox.AddThemeConstantOverride("separation", 4);
        _bannerBox.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _bannerBox.GrowHorizontal = Control.GrowDirection.Both;
        _bannerBox.Position = new Vector2(0, 150);
        Root.AddChild(_bannerBox);
        _bannerIco = IconRect("crown", 72);
        _bannerIco.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _bannerBox.AddChild(_bannerIco);
        _banner = Txt(_bannerBox, "", 58);
        Sticker(_banner, 58, new Color("#ffd84d"), 14, 7);
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        var subP = Panel(_bannerBox, Card(14, 16));
        subP.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _bannerSub = Txt(subP, "", 20);

        _hint = Txt(Root, "", 21, Colors.White);
        Sticker(_hint, 21, Colors.White, 8, 3);
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _hint.GrowHorizontal = Control.GrowDirection.Both;
        _hint.GrowVertical = Control.GrowDirection.Begin;
        _hint.Position = new Vector2(0, -150);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;

        _crosshair = new Crosshair { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.SetAnchorsPreset(Control.LayoutPreset.Center);
        Root.AddChild(_crosshair);

        _tip = Panel(Root, Card(14, 12));
        _tip.Visible = false;
        var tv = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tv.AddThemeConstantOverride("separation", 2);
        _tip.AddChild(tv);
        var th = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        th.AddThemeConstantOverride("separation", 6);
        tv.AddChild(th);
        _tipIco = IconRect("belt", 28); th.AddChild(_tipIco);
        _tipTitle = Txt(th, "", 18);
        _tipBody = Txt(tv, "", 15, InkSoft);
        _tipState = Txt(tv, "", 14);
        _tipState.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;

        _worldHint = Panel(Root, Card(14, 12));
        _worldHint.Visible = false;
        var wh = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        wh.AddThemeConstantOverride("separation", 8);
        _worldHint.AddChild(wh);
        wh.AddChild(Kbd("E", 16));
        _worldHintL = Txt(wh, "", 17);

        _feveInd = Panel(Root, Box(Cream, null, null, 16, 3, 4, 0, padH: 8, padV: 2));
        _feveInd.Visible = false;
        var fh = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feveInd.AddChild(fh);
        fh.AddChild(IconRect("feve_found", 28));
        _feveIndL = Txt(fh, "", 16);
        _feveIndArrow = new TextureRect
        {
            Texture = ArrowTex(), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Size = new Vector2(26, 26), PivotOffset = new Vector2(13, 13),
        };
        Root.AddChild(_feveIndArrow);
    }

    static Texture2D ArrowTex()
    {
        var img = new Image();
        img.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='26' height='26'><path d='M13 2 L24 22 L13 17 L2 22 Z' fill='#f2a71b' stroke='#3b2414' stroke-width='2.5' stroke-linejoin='round'/></svg>", 1);
        return ImageTexture.CreateFromImage(img);
    }

    Tween _bannerTw;
    public void Banner(string title, string sub, float time = 2.8f, string icon = "crown")
    {
        _banner.Text = L.T(title); _bannerSub.Text = L.T(sub);
        _bannerSub.GetParent<Control>().Visible = !string.IsNullOrEmpty(sub);
        _bannerIco.Visible = icon != null;
        if (icon != null) _bannerIco.Texture = Icon(icon);
        _bannerTw?.Kill();
        _bannerBox.Modulate = new Color(1, 1, 1, 0);
        _bannerBox.Scale = new Vector2(0.3f, 0.3f);
        _bannerBox.Rotation = -0.1f;
        _bannerBox.PivotOffset = new Vector2(_bannerBox.Size.X / 2, 60);
        _bannerTw = _bannerBox.CreateTween();
        _bannerTw.SetParallel();
        _bannerTw.TweenProperty(_bannerBox, "modulate:a", 1f, 0.2f);
        _bannerTw.TweenProperty(_bannerBox, "scale", Vector2.One, 0.55f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bannerTw.TweenProperty(_bannerBox, "rotation", 0f, 0.55f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bannerTw.Chain().TweenInterval(time);
        _bannerTw.Chain().TweenProperty(_bannerBox, "modulate:a", 0f, 0.5f);
    }

    public void FlashBag() { }
    public void SetHint(string s) => _hint.Text = L.T(s);
    public void SetCrosshair(bool on, bool hot) { _crosshair.Visible = on; _crosshair.Hot = hot; _crosshair.QueueRedraw(); }

    public void SetWorldHint(string text, Vector2? at)
    {
        _worldHint.Visible = at != null && !string.IsNullOrEmpty(text);
        if (!_worldHint.Visible) return;
        _worldHintL.Text = L.T(text);
        _worldHint.ResetSize();
        float bob = MathF.Sin(Time.GetTicksMsec() / 220f) * 4;
        _worldHint.Position = at.Value - new Vector2(_worldHint.Size.X / 2, _worldHint.Size.Y + bob);
    }

    // au-dessus de la fève si elle est à l'écran, sinon au bord avec une flèche
    public void SetFeveIndicator(string text, Vector2? screen, bool onScreen)
    {
        _feveInd.Visible = screen != null;
        _feveIndArrow.Visible = false;
        if (screen == null) return;
        _feveIndL.Text = text;
        _feveInd.ResetSize();
        var vp = Root.Size;
        var p = screen.Value;
        if (!onScreen)
        {
            var c = vp / 2;
            var d = p - c;
            if (d.LengthSquared() < 1) d = Vector2.Down;
            float k = MathF.Min((vp.X / 2 - 110) / MathF.Max(1, MathF.Abs(d.X)), (vp.Y / 2 - 110) / MathF.Max(1, MathF.Abs(d.Y)));
            p = c + d * k;
            _feveIndArrow.Visible = true;
            _feveIndArrow.Position = p + d.Normalized() * 52 - new Vector2(13, 13);
            _feveIndArrow.Rotation = MathF.Atan2(d.Y, d.X) + MathF.PI / 2;
        }
        // jamais sous les bandeaux de l'interface (haut : pièces, sac, fèves ; bas : outils ; côtés : encadrés)
        p.Y = Mathf.Clamp(p.Y, 200, vp.Y - 170);
        p.X = Mathf.Clamp(p.X, 330, vp.X - 330);
        _feveInd.PivotOffset = _feveInd.Size / 2;
        _feveInd.Scale = Vector2.One * (1 + 0.06f * MathF.Sin(Time.GetTicksMsec() / 180f));
        _feveInd.Position = p - _feveInd.Size / 2;
    }

    public void SetTip(string title, string body, string state, string stateKind, string icon, Vector2 at)
    {
        _tip.Visible = !string.IsNullOrEmpty(title);
        if (!_tip.Visible) return;
        _tipTitle.Text = title;
        _tipBody.Text = body ?? "";
        _tipBody.Visible = !string.IsNullOrEmpty(body);
        _tipIco.Texture = Icon(icon ?? "belt");
        _tipState.Visible = !string.IsNullOrEmpty(state);
        if (_tipState.Visible)
        {
            _tipState.Text = state;
            var (bg, fg) = stateKind switch { "ok" => ("#d6f5df", "#1d7a3c"), "idle" => ("#e1e6ff", "#3846a8"), "blocked" => ("#ffe1c2", "#a3510a"), _ => ("#ffd6d6", "#b02a2a") };
            _tipState.AddThemeColorOverride("font_color", new Color(fg));
            _tipState.AddThemeStyleboxOverride("normal", Box(new Color(bg), null, null, 8, 0, 0, 0, padH: 8, padV: 1));
        }
        _tip.ResetSize();
        var vp = Root.Size;
        var pos = at + new Vector2(20, 20);
        if (pos.X + _tip.Size.X > vp.X - 8) pos.X = at.X - _tip.Size.X - 16;
        if (pos.Y + _tip.Size.Y > vp.Y - 8) pos.Y = at.Y - _tip.Size.Y - 16;
        _tip.Position = pos;
    }

    // ───── Barre d'outils ─────
    public void BuildToolbar()
    {
        var tray = Panel(Root, Tray());
        tray.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        tray.GrowHorizontal = Control.GrowDirection.Both;
        tray.GrowVertical = Control.GrowDirection.Begin;
        tray.MouseFilter = Control.MouseFilterEnum.Stop;
        _tray = tray;
        Root.MoveChild(tray, _overlay.GetIndex()); // sous les fenêtres
        _tools = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tools.AddThemeConstantOverride("separation", 8);
        tray.AddChild(_tools);
        var modes = new List<string> { "harvest" };
        modes.AddRange(GameState.BuildOrder);
        modes.Add("demolish");
        int key = 1;
        foreach (var mode in modes)
        {
            var slot = new Control { CustomMinimumSize = new Vector2(76, 86), MouseFilter = Control.MouseFilterEnum.Ignore };
            _tools.AddChild(slot);
            var tile = new PanelContainer { Size = new Vector2(76, 86), MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, PivotOffset = new Vector2(38, 43) };
            slot.AddChild(tile);
            var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            v.AddThemeConstantOverride("separation", 0);
            tile.AddChild(v);
            var ico = IconRect(mode, 42);
            ico.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            v.AddChild(ico);
            var costRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            costRow.AddThemeConstantOverride("separation", 2);
            v.AddChild(costRow);
            var coin = IconRect("coin", 14);
            costRow.AddChild(coin);
            var cost = Txt(costRow, "", 13);
            var k = Kbd(key.ToString());
            k.Position = new Vector2(-7, -9);
            slot.AddChild(k);
            string m = mode;
                        tile.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ToolPicked?.Invoke(m); };
            tile.MouseEntered += () => { if (!_selTile.Equals(m)) { var tw = tile.CreateTween(); tw.TweenProperty(tile, "position:y", -3f, 0.08f); } };
            tile.MouseExited += () => { var tw = tile.CreateTween(); tw.TweenProperty(tile, "position:y", _selTile == m ? -6f : 0f, 0.1f); };
            coin.Visible = mode != "harvest" && mode != "demolish";
            _toolSlots.Add((mode, tile, cost, ico));
            key++;
        }
    }

    string _selTile = "harvest";
    string _lastTools = "";
    public void UpdateToolbar(string current)
    {
        var sig = L.Code + current + Fmt(_game.Money) + _game.Buildings.Count + string.Join("", _game.UpgLvl.Values);
        if (sig == _lastTools) return;
        _lastTools = sig;
        _selTile = current;
        foreach (var (mode, tile, cost, ico) in _toolSlots)
        {
            bool sel = mode == current;
            bool locked = mode != "harvest" && mode != "demolish" && !_game.BuildingUnlocked(mode);
            bool poor = !locked && mode != "harvest" && mode != "demolish" && _game.Money < _game.CostOf(mode);
            cost.Text = mode == "harvest" ? "Croquer" : mode == "demolish" ? "50 %" : locked ? "Verrouillé" : Fmt(_game.CostOf(mode));
            tile.TooltipText = mode == "harvest" ? L.T("Récolter") : mode == "demolish" ? L.T("Démolir (50 % remboursés)") : GameState.BuildingTypes[mode].Name + " · " + GameState.BuildingTypes[mode].Desc;
            cost.AddThemeColorOverride("font_color", poor ? new Color("#d23a3a") : locked ? new Color("#8a7b64") : Ink);
            if (cost.GetParent().GetChild(0) is Control coin) coin.Visible = !locked && mode != "harvest" && mode != "demolish";
            tile.AddThemeStyleboxOverride("panel", sel
                ? Box(new Color("#fff3b0"), Gold, Gold2, 16, 3, 4, 4, padH: 4, padV: 4)
                : locked ? Box(new Color("#ddd2c0"), null, new Color("#cbbca3"), 16, 3, 4, 4, padH: 4, padV: 4)
                : Box(Cream, null, Cream2, 16, 3, 4, 4, padH: 4, padV: 4));
            ico.Modulate = locked ? new Color(0.35f, 0.35f, 0.35f, 0.5f) : Colors.White;
            tile.Position = new Vector2(0, sel ? -6 : 0);
        }
    }

    // ───── Fenêtres à onglets ─────
    void BuildWindow()
    {
        _overlay = new ColorRect { Color = new Color(0.23f, 0.14f, 0.08f, 0.45f), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(_overlay);
        _overlay.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) CloseAll(); };
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(center);
        _window = new PanelContainer { CustomMinimumSize = new Vector2(880, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        var card = BigCard();
        // le contenu reste DANS le contour encre (l'en-tête ne le recouvre plus)
        card.ContentMarginLeft = card.ContentMarginRight = card.ContentMarginTop = 4;
        _window.AddThemeStyleboxOverride("panel", card);
        center.AddChild(_window);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 0);
        _window.AddChild(v);
        var head = Panel(v, HeaderBand(22, 14, 12));
        head.MouseFilter = Control.MouseFilterEnum.Stop;
        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 8);
        head.AddChild(_tabs);
        v.AddChild(new ColorRect { Color = Ink, CustomMinimumSize = new Vector2(0, 4) });
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 580) };
        v.AddChild(scroll);
        var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var k in new[] { "margin_left", "margin_right" }) pad.AddThemeConstantOverride(k, 18);
        pad.AddThemeConstantOverride("margin_top", 14); pad.AddThemeConstantOverride("margin_bottom", 18);
        scroll.AddChild(pad);
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        pad.AddChild(_body);
    }

    void BuildTabs()
    {
        foreach (var c in _tabs.GetChildren()) { _tabs.RemoveChild(c); c.QueueFree(); }
        if (_tab == "manager") Btn(_tabs, "Gestion des ouvrières", "cream", 19, "manager", 30);
        else
        {
            foreach (var (id, label, icon) in new[] { ("shop", "Boutique", "cart"), ("prices", "Prix", "tag"), ("help", "Aide", "help") })
            {
                var b = Btn(_tabs, label, "cream", 19, icon, 28);
                if (id == _tab) { var sb = Box(Colors.White, null, null, 14, 4, 2, 0, padH: 14, padV: 6); b.AddThemeStyleboxOverride("normal", sb); b.AddThemeStyleboxOverride("hover", sb); b.AddThemeStyleboxOverride("pressed", sb); }
                else b.Modulate = new Color(1, 1, 1, 0.82f);
                string i2 = id;
                b.Pressed += () => OpenTab(i2);
            }
        }
        _tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = Btn(_tabs, "", "red", 18, "close", 24);
        close.CustomMinimumSize = new Vector2(50, 46);
        close.Pressed += CloseAll;
    }

    public void OpenTab(string tab)
    {
        bool wasOpen = _overlay.Visible;
        _tab = tab;
        _overlay.Visible = true;
        BuildTabs();
        Refresh(true);
        if (wasOpen) return;
        _window.PivotOffset = _window.Size / 2;
        _window.Scale = new Vector2(0.9f, 0.9f);
        _window.Modulate = new Color(1, 1, 1, 0);
        var tw = _window.CreateTween(); tw.SetParallel();
        tw.TweenProperty(_window, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_window, "modulate:a", 1f, 0.15f);
    }

    public void ToggleShop() { if (_overlay.Visible && _tab == "shop") CloseAll(); else OpenTab("shop"); }
    public void ToggleHelp() { if (_overlay.Visible && _tab == "help") CloseAll(); else OpenTab("help"); }
    public void TogglePrices() { if (_overlay.Visible && _tab == "prices") CloseAll(); else OpenTab("prices"); }
    public void ToggleManager() { if (_overlay.Visible && _tab == "manager") CloseAll(); else OpenTab("manager"); }
    public void CloseAll() { _overlay.Visible = false; CloseExtra?.Invoke(); }

    string _bodySig = "";
    void Refresh(bool force)
    {
        string sig = L.Code + _tab + Fmt(_game.Money) + string.Join(",", _game.UpgLvl.Values) + string.Join(",", _game.Alloc) + _game.RecruitTo + _pricesCake + _game.TrophyCount + _game.Cakes.Count(k => k.Enraged) + _game.Cakes.Count(k => k.Unlocked) + _game.Workers.Count(w => w.At != w.Cake);
        if (!force && sig == _bodySig) return;
        _bodySig = sig;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        switch (_tab)
        {
            case "shop": BuildShop(); break;
            case "prices": BuildPrices(); break;
            case "help": BuildHelp(); break;
            case "manager": BuildManager(); break;
        }
    }

    static readonly Dictionary<string, string> UpgIcon = new()
    {
        ["mand"] = "harvest", ["sac"] = "bag", ["mach"] = "lightning", ["pattes"] = "boot", ["recruit"] = "recruit", ["train"] = "train", ["eng"] = "gear",
        ["turbo"] = "turbo", ["oil"] = "oil", ["planComp"] = "planComp", ["planOven"] = "planOven", ["planDeco"] = "planDeco", ["chefs"] = "chef", ["pub"] = "tv",
    };

    static string G(string s) => $"[color=#1d7a3c][b]{s}[/b][/color]";
    static string Effect(string id, int l) => id switch
    {
        "mand" => L.F("Morsure : {0} → {1} miettes", Fmt(Fx.Bite(l)), G(Fmt(Fx.Bite(l + 1)))),
        "sac" => L.F("Sac : {0} → {1} miettes", Fmt(Fx.Cap(l)), G(Fmt(Fx.Cap(l + 1)))),
        "mach" => L.F("Entre deux morsures : {0} s → {1} s", L.N(Fx.Cooldown(l), "0.00"), G(L.N(Fx.Cooldown(l + 1), "0.00"))),
        "pattes" => L.F("Vitesse : {0} → {1}", L.N(Fx.Speed(l), "0.0"), G(L.N(Fx.Speed(l + 1), "0.0"))),
        "recruit" => L.F("Ouvrières : {0} → {1}", l, G((l + 1).ToString())),
        "train" => L.F("Charge {0} → {1} · cadence {2}/s → {3}/s", Fmt(Fx.WLoad(l)), G(Fmt(Fx.WLoad(l + 1))), L.N(Fx.WRate(l), "0.#"), G(L.N(Fx.WRate(l + 1), "0.#"))),
        "turbo" => L.F("Grues : {0}/s → {1}/s · portée {2} → {3}", L.N(Fx.HRate(l), "0.#"), G(L.N(Fx.HRate(l + 1), "0.#")), Fx.HRange(l), G(Fx.HRange(l + 1).ToString())),
        "oil" => L.F("Tapis : {0} → {1} cases/s", L.N(Fx.Belt(l), "0.0"), G(L.N(Fx.Belt(l + 1), "0.0"))),
        "chefs" => L.F("Usines : ×{0} → ×{1}", L.N(Fx.Factory(l), "0.0"), G(L.N(Fx.Factory(l + 1), "0.0"))),
        "pub" => L.F("Tous les prix : +{0} % → +{1} %", 25 * l, G((25 * (l + 1)).ToString())),
        _ => Upg[id].Desc,
    };

    void Category(string title, string icon)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        _body.AddChild(h);
        h.AddChild(IconRect(icon, 32));
        Txt(h, title, 21).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
    }

    void BuildShop()
    {
        var groups = new (string title, string icon, string[] ids)[]
        {
            ("Ta fourmi", "ant", new[] { "mand", "sac", "mach", "pattes" }),
            ("Les ouvrières", "recruit", new[] { "recruit", "train" }),
            ("L'usine", "gear", new[] { "eng", "turbo", "oil", "planComp", "planOven", "planDeco", "chefs" }),
            ("Commerce", "tv", new[] { "pub" }),
        };
        foreach (var (title, icon, ids) in groups)
        {
            Category(title, icon);
            foreach (var id in ids) ShopRow(id);
        }
        int nt = _game.TrophyCount;
        Category(L.F("Trophées · {0} / 6 · +{1} % sur tous les gains", nt, 10 * nt), "trophy");
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 8); grid.AddThemeConstantOverride("v_separation", 8);
        _body.AddChild(grid);
        for (int i = 0; i < NbCakes; i++)
        {
            bool has = _game.Trophies[i];
            var K = _game.Cakes[i];
            var p = Panel(grid, Box(has ? new Color("#fff3b0") : new Color("#ece2d0"), null, null, 14, 3, 3, 0, padH: 4, padV: 6));
            p.CustomMinimumSize = new Vector2(128, 0);
            var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            v.AddThemeConstantOverride("separation", 2);
            p.AddChild(v);
            var ic = IconRect("trophy", 40); ic.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            if (!has) ic.Modulate = new Color(0, 0, 0, 0.25f);
            v.AddChild(ic);
            var l = Txt(v, has ? K.Theme.Name : L.F("Gâteau {0}", i + 1), 13, has ? Ink : new Color("#9a8b74"));
            l.HorizontalAlignment = HorizontalAlignment.Center;
            var l2 = Txt(v, has ? "+10 %" : L.F("{0} % mangé", (100 * (1 - K.Remaining / Math.Max(1, K.Total))).ToString("0")), 12, has ? Green2 : new Color("#9a8b74"), false);
            l2.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    void ShopRow(string id)
    {
        var u = Upg[id];
        int lvl = _game.UpgLvl[id];
        var c = _game.CanBuyUpg(id);
        bool locked = c.why == "req";
        var row = Panel(_body, Box(Colors.White, null, null, 18, 3, 3, 0, padH: 8, padV: 8));
        if (locked) row.Modulate = new Color(1, 1, 1, 0.6f);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        row.AddChild(h);
        var tile = Panel(h, Box(new Color("#fff3d1"), null, null, 14, 3, 0, 0, padH: 6, padV: 6));
        var ti = IconRect(UpgIcon.GetValueOrDefault(id, "star"), 44);
        if (locked) ti.Modulate = new Color(0.5f, 0.5f, 0.5f);
        tile.AddChild(ti);
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        mid.AddThemeConstantOverride("separation", 1);
        h.AddChild(mid);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        mid.AddChild(nameRow);
        Txt(nameRow, u.Name, 19);
        if (u.Max > 1)
        {
            var lv = Txt(nameRow, L.F("niv {0}", lvl), 13, Colors.White);
            lv.AddThemeStyleboxOverride("normal", Box(Ink, null, null, 8, 0, 0, 0, padH: 7, padV: 0));
            lv.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        }
        var eff = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(480, 0) };
        eff.AddThemeFontOverride("normal_font", UiSkin.Font);
        eff.AddThemeFontOverride("bold_font", Bold);
        eff.AddThemeFontSizeOverride("normal_font_size", 15);
        eff.AddThemeFontSizeOverride("bold_font_size", 15);
        eff.AddThemeColorOverride("default_color", InkSoft);
        eff.Text = locked ? L.F("Requiert : {0}", $"[b]{u.ReqText}[/b]") : lvl >= u.Max ? (u.Max == 1 ? $"{u.Desc} · {G(L.T("acquis"))}" : G(L.T("Niveau maximum atteint"))) : Effect(id, lvl);
        mid.AddChild(eff);
        if (u.Max > 1)
        {
            var mrow = new HBoxContainer();
            mrow.AddThemeConstantOverride("separation", 8);
            mid.AddChild(mrow);
            var pips = Meter(mrow, 340, new Color("#7dea9b"), Green);
            pips.CustomMinimumSize = new Vector2(340, 12);
            pips.Value = lvl / (double)u.Max;
            pips.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            Txt(mrow, $"{lvl} / {u.Max}", 13, InkSoft);
        }
        if (lvl >= u.Max)
        {
            var done = IconRect("star", 42);
            done.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            h.AddChild(done);
            return;
        }
        var b = Btn(h, Fmt(UpgCost(id, lvl)), c.ok ? "green" : "gold", 20, "coin", 26);
        b.CustomMinimumSize = new Vector2(150, 54);
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        b.Disabled = !c.ok;
        b.Pressed += () =>
        {
            if (_game.BuyUpg(id)) { Sfx.Play("buy"); Refresh(true); }
            else Sfx.Play("error");
        };
    }

    void BuildPrices()
    {
        var pick = new HBoxContainer();
        pick.AddThemeConstantOverride("separation", 6);
        _body.AddChild(pick);
        Txt(pick, "Miettes du gâteau :", 17).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        for (int k = 0; k < NbCakes; k++)
        {
            var K = _game.Cakes[k];
            var b = Btn(pick, $"{k + 1}", k == _pricesCake ? "gold" : "cream", 17);
            b.Disabled = !K.Unlocked;
            b.CustomMinimumSize = new Vector2(48, 42);
            int kk = k;
            b.Pressed += () => { _pricesCake = kk; Refresh(true); };
        }
        var KC = _game.Cakes[_pricesCake];
        double m = _game.GMult() * KC.Vm;
        var multRow = new HBoxContainer();
        multRow.AddThemeConstantOverride("separation", 10);
        _body.AddChild(multRow);
        multRow.AddChild(IconRect("coin", 36));
        Txt(multRow, L.F("Multiplicateur : ×{0}", L.N(m, "0.##")), 24).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Txt(_body, L.F("Gâteau ×{0} · Pub +{1} % · trophées +{2} %", Fmt(KC.Vm), 25 * _game.UpgLvl["pub"], 10 * _game.TrophyCount), 15, InkSoft, false);
        Table(new[] { "Objet", "Valeur", "Miettes", "Par miette" }, Items.Select(kv => new[]
        {
            kv.Key, kv.Value.Name, Fmt(kv.Value.Value * m), Fmt(kv.Value.Crumbs), "×" + L.N(kv.Value.Value / kv.Value.Crumbs, "0.0#"),
        }).ToList(), true);
        Category("Les six gâteaux", "cake");
        Table(new[] { "Gâteau", "Miettes", "Valeur", "Fève" }, _game.Cakes.Select(K => new[]
        {
            K.Found ? "feve_found" : K.Unlocked ? "feve_hunt" : "lock", $"{K.N} · {K.Theme.Name}", Fmt(K.Total), $"×{Fmt(K.Vm)}", L.T(K.Found ? "trouvée" : K.Unlocked ? (K.FeveHint ? "signalée" : "à chercher") : "sous cloche"),
        }).ToList(), true);
    }

    void Table(string[] head, List<string[]> rows, bool withIcon)
    {
        var p = Panel(_body, Box(Colors.White, null, null, 16, 3, 0, 0, padH: 2, padV: 2));
        var g = new GridContainer { Columns = head.Length };
        g.AddThemeConstantOverride("h_separation", 0); g.AddThemeConstantOverride("v_separation", 0);
        p.AddChild(g);
        void Cell(string text, bool header, string icon = null)
        {
            var c = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            c.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = header ? new Color("#fff3d1") : Colors.White, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6, BorderColor = new Color("#f0e2c8"), BorderWidthBottom = 2 });
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 6);
            c.AddChild(h);
            if (icon != null) h.AddChild(IconRect(icon, 24));
            Txt(h, text, 16, header ? Ink : InkSoft, header).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            g.AddChild(c);
        }
        foreach (var hd in head) Cell(hd, true);
        foreach (var r in rows)
        {
            Cell(r[1], false, withIcon ? r[0] : null);
            for (int i = 2; i < r.Length; i++) Cell(r[i], false);
        }
    }

    void Para(string t)
    {
        var l = Txt(_body, t, 16, InkSoft, false);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(820, 0);
    }

    void BuildHelp()
    {
        Category("Le but", "feve_found");
        Para("Six gâteaux, six fèves. Croque, vends tes miettes à la fourmilière, agrandis ta colonie, et rapporte chaque fève au nid pour débloquer le gâteau suivant.");
        Category("Commandes", "gear");
        Table(new[] { "Touche", "Action" }, new List<string[]>
        {
            new[] { null, L.MoveKeys() + " · " + L.T("flèches"), "se déplacer (pousse contre un mur pour l'escalader)" },
            new[] { null, "Espace", "sauter" },
            new[] { null, "Clic gauche maintenu", "croquer · construire (glisser pour les tapis) · démolir (glisser : en chaîne)" },
            new[] { null, "Clic droit + glisser", "orienter la caméra" },
            new[] { null, L.KeyName(Key.V), "changer de vue : proche → large (construction) → fourmi" },
            new[] { null, L.KeyName(Key.E), "ramasser la fève · parler à la gestionnaire" },
            new[] { null, "1 · 2 à 8 · 9", "récolter · bâtiments · démolir" },
            new[] { null, L.KeyName(Key.R), "tourner un bâtiment" },
            new[] { null, string.Join(" · ", new[] { Key.B, Key.P, Key.H, Key.G, Key.M }.Select(L.KeyName)), "boutique · prix · aide · radiographie · son" },
            new[] { null, "Échap", "fermer · pause" },
        }, false);
        Category("Astuces", "sparkle");
        foreach (var t in new[]
        {
            "La gestionnaire, assise à côté du nid, répartit tes ouvrières entre les gâteaux.",
            "Une grue posée au bord du gâteau grignote le dessus : relie-la au nid par des tapis, et alimente le tout avec une roue à fourmi.",
            "Compacteur, puis four, puis décorateur : la même miette vaut jusqu'à 5 fois plus.",
            "Seule ta fourmi trouve des pépites et des amandes en croquant.",
            "Chaque gâteau mangé à 100 % rapporte un trophée : +10 % sur tous les gains.",
        }) Para("•  " + L.T(t));
        var reset = Btn(_body, "Recommencer de zéro", "red", 17);
        reset.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        reset.Pressed += () => { if (reset.Text == "Recommencer de zéro") reset.Text = "Sûr ? Tout sera effacé"; else ResetRequested?.Invoke(); };
    }

    void BuildManager()
    {
        var head = Panel(_body, Box(new Color("#f6ecd6"), null, null, 16, 3, 0, 0, padH: 14, padV: 10));
        var hh = new HBoxContainer(); hh.AddThemeConstantOverride("separation", 14);
        head.AddChild(hh);
        hh.AddChild(IconRect("manager", 60));
        var hv = new VBoxContainer(); hh.AddChild(hv);
        Txt(hv, L.P(_game.WantedWorkers, "{0} ouvrière", "{0} ouvrières") + " · " + L.F("{0} au repos", _game.Resting), 20);
        Txt(hv, _game.Cakes.Count(k => k.Unlocked) > 1 ? "« Dites-moi où envoyer tout ce petit monde. »" : "« Zzz… Un seul gâteau, rien à organiser. »", 15, InkSoft, false);
        var route = new int[NbCakes];
        foreach (var w in _game.Workers) if (w.Cake >= 0 && w.At != w.Cake) route[w.Cake]++;
        for (int k = 0; k < NbCakes; k++)
        {
            var K = _game.Cakes[k];
            int kk = k;
            var row = Panel(_body, Box(K.Unlocked ? new Color("#fffaf0") : new Color("#f0e9dc"), null, null, 12, 2, 0, 0, stroke: new Color("#e2d3b4"), padH: 10, padV: 6));
            if (!K.Unlocked) row.Modulate = new Color(1, 1, 1, 0.6f);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8);
            row.AddChild(h);
            var dot = new Panel { CustomMinimumSize = new Vector2(22, 22), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            dot.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = K.Theme.Glaze, BorderColor = Ink, BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, CornerRadiusTopLeft = 11, CornerRadiusTopRight = 11, CornerRadiusBottomLeft = 11, CornerRadiusBottomRight = 11 });
            h.AddChild(dot);
            var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tv.AddThemeConstantOverride("separation", -2);
            h.AddChild(tv);
            Txt(tv, L.F("Gâteau {0} · {1}", K.N, K.Theme.Name), 18);
            Txt(tv, !K.Unlocked ? L.T("Sous cloche") : $"×{Fmt(K.Vm)} · " + L.F("{0} restantes", Fmt(K.Remaining)) + " · " + L.T(K.Found ? "fève trouvée" : K.FeveHint ? "fève signalée !" : "fève cachée") + (route[k] > 0 ? " · " + L.F("{0} en route", route[k]) : "") + (K.Enraged && K.Remaining > 0 ? " · " + L.T("ENRAGÉES") : ""), 14, K.Enraged && K.Remaining > 0 ? new Color("#c0392b") : InkSoft, false);
            if (!K.Unlocked) { h.AddChild(IconRect("lock", 30)); continue; }
            var minus = Btn(h, "-", "cream", 22); minus.CustomMinimumSize = new Vector2(44, 44);
            var n = Txt(h, _game.Alloc[k].ToString(), 24); n.CustomMinimumSize = new Vector2(44, 0); n.HorizontalAlignment = HorizontalAlignment.Center;
            var plus = Btn(h, "+", "gold", 22); plus.CustomMinimumSize = new Vector2(44, 44);
            var all = Btn(h, "Toutes", "cream", 14);
            var rec = Btn(h, "recrues", _game.RecruitTo == k ? "green" : "cream", 14, "recruit", 22);
            foreach (var b in new Control[] { n, minus, plus, all, rec }) b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            minus.Pressed += () => { _game.MoveWorkers(kk, Input.IsKeyPressed(Key.Shift) ? -5 : -1); Refresh(true); };
            plus.Pressed += () => { _game.MoveWorkers(kk, Input.IsKeyPressed(Key.Shift) ? 5 : 1); Refresh(true); };
            all.Pressed += () => { _game.AllWorkersTo(kk); Refresh(true); };
            rec.Pressed += () => { _game.RecruitTo = kk; Refresh(true); };
        }
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 12);
        _body.AddChild(foot);
        var spread = Btn(foot, "Répartir équitablement", "green", 17);
        spread.Pressed += () => { _game.SpreadWorkers(); Sfx.Play("buy"); Refresh(true); };
        Txt(foot, "Maj + clic : par 5", 14, InkSoft, false).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
    }

    // placement explicite (indépendant des ancres), recalculé à chaque image
    void Layout()
    {
        var W = Root.Size;
        void At(Control c, float x, float y) { if (c != null) { c.Size = c.GetCombinedMinimumSize(); c.Position = new Vector2(MathF.Round(x), MathF.Round(y)); } }
        At(_topBar, (W.X - _topBar.GetCombinedMinimumSize().X) / 2, 12);
        At(_neck, (W.X - _neck.GetCombinedMinimumSize().X) / 2, 98);
        At(_revBox, 12, 12);
        At(_colBox, W.X - _colBox.GetCombinedMinimumSize().X - 12, 12);
        At(_sideBox, W.X - _sideBox.GetCombinedMinimumSize().X - 14, (W.Y - _sideBox.GetCombinedMinimumSize().Y) / 2 + 30);
        if (_tray != null) At(_tray, (W.X - _tray.GetCombinedMinimumSize().X) / 2, W.Y - _tray.GetCombinedMinimumSize().Y - 14);
        _feed.Size = _feed.GetCombinedMinimumSize();
        _feed.Position = new Vector2(12, W.Y - _feed.Size.Y - 130);
        _hint.Size = new Vector2(W.X, 40);
        _hint.Position = new Vector2(0, W.Y - 170);
        _bannerBox.Size = new Vector2(W.X, _bannerBox.GetCombinedMinimumSize().Y);
        _bannerBox.Position = new Vector2(0, W.Y * 0.17f);
        _bannerBox.PivotOffset = new Vector2(W.X / 2, 60);
        _crosshair.Position = W / 2;
        bool small = W.X < 1300;
        _revBox.Visible = _colBox.Visible = !small;
    }

    // ───── Rafraîchissement ─────
    // ───── Compteur d'images par seconde (option ou F3) ─────
    PanelContainer _fpsBox; Label _fpsL; float _fpsT;
    void BuildFps()
    {
        _fpsBox = Panel(Root, Box(new Color(0.12f, 0.08f, 0.05f), null, null, 10, 2, 0, 0, 0.78f, new Color(1, 1, 1, 0.15f), padH: 10, padV: 3));
        _fpsBox.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _fpsBox.GrowHorizontal = Control.GrowDirection.Begin; _fpsBox.GrowVertical = Control.GrowDirection.Begin;
        _fpsBox.Position = new Vector2(-14, -14);
        _fpsL = Txt(_fpsBox, "", 15, Colors.White);
        _fpsBox.Visible = UI.Settings.ShowFps;
    }
    public void SetFpsVisible(bool on) { if (_fpsBox != null) _fpsBox.Visible = on; }

    public void UpdateHud(float dt)
    {
        if (_fpsBox.Visible && (_fpsT -= dt) <= 0)
        {
            _fpsT = 0.25f;
            double fps = Engine.GetFramesPerSecond();
            _fpsL.Text = L.F("{0} FPS · {1} ms/image", fps.ToString("0"), L.N(1000 / Math.Max(1, fps), "0.0"));
            _fpsL.AddThemeColorOverride("font_color", fps >= 55 ? new Color("#8ef0a6") : fps >= 30 ? new Color("#ffd36a") : new Color("#ff8f8f"));
            // en bas à droite, au-dessus des boutons latéraux
            _fpsBox.Position = Root.Size - _fpsBox.Size - new Vector2(14, 14);
        }
        Layout();
        var st = _game.Stats();
        var p = _game.Player;
        var inc = _game.Income();
        _money.Text = Fmt(_game.Money);
        if (_game.Money > _lastMoney + 0.5) _moneyPulse = 1;
        _lastMoney = _game.Money;
        _moneyPulse = MathF.Max(0, _moneyPulse - dt * 4);
        _moneyPill.PivotOffset = _moneyPill.Size / 2;
        _moneyPill.Scale = Vector2.One * (1 + 0.07f * MathF.Sin(_moneyPulse * MathF.PI));
        _income.Text = $"+{Fmt(inc.total)} /s";
        _bag.Text = $"{Fmt(p.Bag)} / {Fmt(st.cap)}";
        _bagBar.Value = st.cap > 0 ? p.Bag / (double)st.cap : 0;
        bool full = p.Bag >= st.cap;
        _bagPill.PivotOffset = _bagPill.Size / 2;
        _bagPill.Rotation = full ? MathF.Sin(Time.GetTicksMsec() / 70f) * 0.035f : 0;
        _bagPill.Panel.SelfModulate = full ? new Color("#ffe1e1") : Colors.White;
        if (_bagFull != full)
        {
            _bagFull = full;
            _bagBar.AddThemeStyleboxOverride("fill", Gauge(full ? new Color("#ff6b6b") : new Color("#ffb35c").Lerp(new Color("#e8743b"), 0.45f)));
        }
        var K = _game.CurrentCake(p.Pos.X, p.Pos.Z);
        if (K != null)
        {
            _cakeName.Text = L.F("Gâteau {0} · {1}", K.N, K.Theme.Name);
            _cakeSub.Text = L.F("{0} miettes · ×{1} par miette", Fmt(K.Remaining), Fmt(K.Vm));
            _cakeBar.Value = K.Total > 0 ? K.Remaining / K.Total : 0;
        }
        float t = Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < _feves.Count; i++)
        {
            var C = _game.Cakes[i];
            string s = C.Found ? "feve_found" : C.Unlocked ? "feve_hunt" : "feve_locked";
            _feves[i].Texture = Icon(s);
            bool hunt = s == "feve_hunt";
            float k = hunt ? MathF.Max(0, MathF.Sin(t * 3.9f)) : 0;
            _feves[i].Scale = Vector2.One * (1 + 0.15f * k);
            _feves[i].Rotation = -0.1f * k;
            _feves[i].Modulate = s == "feve_locked" ? new Color(1, 1, 1, 0.55f) : Colors.White;
        }
        _slow -= dt;
        if (_slow <= 0)
        {
            _slow = 0.3f;
            foreach (var c in _revRows.GetChildren()) { _revRows.RemoveChild(c); c.QueueFree(); }
            var cr = _game.CrumbRate();
            Row(_revRows, "you", "Toi", $"{Fmt(cr.me)} /s");
            Row(_revRows, "ant_worker", "Ouvrières", $"{Fmt(cr.workers)} /s");
            Row(_revRows, "harv", "Grues", $"{Fmt(cr.cranes)} /s");
            foreach (var c in _colRows.GetChildren()) { _colRows.RemoveChild(c); c.QueueFree(); }
            Row(_colRows, "feve_found", "Fèves", $"{_game.Crowns} / 6");
            Row(_colRows, "recruit", "Ouvrières", _game.WantedWorkers.ToString());
            Row(_colRows, "gear", "Bâtiments", _game.Buildings.Count.ToString());
            Row(_colRows, "trophy", "Trophées", $"{_game.TrophyCount} / 6");
            Row(_colRows, "clock", "Temps de jeu", $"{(int)(_game.PlayTime / 3600)} h {(int)(_game.PlayTime / 60) % 60:00}");
            if (_overlay.Visible) Refresh(false);
        }
    }
    bool _bagFull;
}

public partial class Crosshair : Control
{
    public bool Hot;
    public override void _Draw()
    {
        float r = Hot ? 11 : 7;
        DrawArc(Vector2.Zero, r, 0, MathF.Tau, 32, new Color(0.23f, 0.14f, 0.08f, 0.6f), 6, true);
        DrawArc(Vector2.Zero, r, 0, MathF.Tau, 32, Hot ? UiSkin.Gold : Colors.White, 3, true);
    }
}
