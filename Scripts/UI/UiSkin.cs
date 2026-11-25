// ─────────────────────────────────────────────────────────────
// Habillage de l'interface, style « autocollant » : contour encre
// épais, ombre portée nette, liseré clair en bas, dégradés chauds.
// Les cadres sont dessinés en SVG à la volée (9-patch nets à toute
// taille) et rassemblés dans un Theme Godot appliqué partout.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Crumbants.UI;

public static class UiSkin
{
    public static readonly Color Ink = new("#3b2414"), InkSoft = new("#6b4a2f"), Cream = new("#fff8ea"), Cream2 = new("#fbe9c8"),
        Gold = new("#ffc93c"), Gold2 = new("#f2a71b"), Gold3 = new("#b9760a"), Green = new("#43c96d"), Green2 = new("#23964a"),
        Red = new("#ff5a5f"), Blue = new("#4f8dff"), Edge = new("#e4c38f"), MoneyText = new("#b06d00");

    public static FontFile Font, Bold;
    // écriture manuscrite (panneaux des gâteaux) : Pacifico, avec Fredoka en secours pour les
    // caractères absents (autres langues plus tard : on ajoutera leurs polices en secours ici)
    public static FontFile Script;
    static readonly Dictionary<string, Texture2D> _icons = new();
    static readonly Dictionary<string, StyleBoxTexture> _boxes = new();

    public static void Init()
    {
        Font ??= GD.Load<FontFile>("res://Fonts/fredoka-latin-600-normal.woff2");
        Bold ??= GD.Load<FontFile>("res://Fonts/fredoka-latin-700-normal.woff2");
        if (Script == null)
        {
            Script = GD.Load<FontFile>("res://Fonts/pacifico-latin-400-normal.woff2");
            var ext = GD.Load<FontFile>("res://Fonts/pacifico-latin-ext-400-normal.woff2");
            Script.Fallbacks = new Godot.Collections.Array<Font> { ext, Bold };
        }
    }

    static string H(Color c) => "#" + c.ToHtml(false);
    static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static Texture2D Icon(string name)
    {
        if (_icons.TryGetValue(name, out var t)) return t;
        t = ResourceLoader.Exists($"res://Icons/{name}.svg") ? GD.Load<Texture2D>($"res://Icons/{name}.svg") : null;
        _icons[name] = t;
        return t;
    }

    static Texture2D Svg(string svg)
    {
        var img = new Image();
        img.LoadSvgFromString(svg, 1f);
        return ImageTexture.CreateFromImage(img);
    }

    // Cadre arrondi : ombre portée (décalée vers le bas), fond (uni ou dégradé), liseré du bas, contour.
    public static StyleBoxTexture Box(Color top, Color? bottom = null, Color? inset = null, int radius = 18, int border = 3,
        int shadow = 4, int insetH = 4, float alpha = 1, Color? stroke = null, int padH = 14, int padV = 8, Color? shadowCol = null)
    {
        string key = $"{H(top)}{H(bottom ?? top)}{H(inset ?? top)}{radius}{border}{shadow}{insetH}{alpha}{H(stroke ?? Ink)}{padH}{padV}{H(shadowCol ?? Ink)}";
        if (_boxes.TryGetValue(key, out var cached)) return (StyleBoxTexture)cached.Duplicate();
        int W = radius * 2 + border * 2 + 8, Hh = radius * 2 + border * 2 + 8 + insetH;
        float b = border, r = radius;
        var sc = stroke ?? Ink;
        var sh = shadowCol ?? Ink;
        string grad = bottom.HasValue ? $"<defs><linearGradient id='g' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{H(top)}'/><stop offset='1' stop-color='{H(bottom.Value)}'/></linearGradient></defs>" : "";
        string fill = bottom.HasValue ? "url(#g)" : H(top);
        float w = W - b, h = Hh - b;
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{W}' height='{Hh + shadow}'>{grad}" +
            (shadow > 0 ? $"<rect x='{F(b / 2)}' y='{F(b / 2 + shadow)}' width='{F(w)}' height='{F(h)}' rx='{F(r)}' fill='{H(sh)}' fill-opacity='{F(0.9f * alpha)}'/>" : "") +
            (inset.HasValue && insetH > 0 ? $"<rect x='{F(b / 2)}' y='{F(b / 2)}' width='{F(w)}' height='{F(h)}' rx='{F(r)}' fill='{H(inset.Value)}' fill-opacity='{F(alpha)}'/>" +
                $"<rect x='{F(b / 2)}' y='{F(b / 2)}' width='{F(w)}' height='{F(h - insetH)}' rx='{F(r)}' fill='{fill}' fill-opacity='{F(alpha)}'/>"
              : $"<rect x='{F(b / 2)}' y='{F(b / 2)}' width='{F(w)}' height='{F(h)}' rx='{F(r)}' fill='{fill}' fill-opacity='{F(alpha)}'/>") +
            (b > 0 ? $"<rect x='{F(b / 2)}' y='{F(b / 2)}' width='{F(w)}' height='{F(h)}' rx='{F(r)}' fill='none' stroke='{H(sc)}' stroke-width='{F(b)}'/>" : "") +
            "</svg>";
        var sb = new StyleBoxTexture { Texture = Svg(svg) };
        // marges 9-patch au plus juste (l'arrondi + la moitié du contour) : un cadre plus petit que
        // ses marges s'écrase (trait au milieu, bouts pointus)
        int m = (int)MathF.Ceiling(radius + border / 2f) + 1;
        sb.TextureMarginLeft = sb.TextureMarginRight = m;
        sb.TextureMarginTop = m;
        sb.TextureMarginBottom = m + insetH + shadow;
        sb.ContentMarginLeft = sb.ContentMarginRight = padH;
        sb.ContentMarginTop = padV;
        sb.ContentMarginBottom = padV + insetH + shadow;
        _boxes[key] = sb;
        return (StyleBoxTexture)sb.Duplicate();
    }

    // Jauge : capsule à bouts parfaitement ronds à toute largeur (même presque vide)
    public static StyleBoxFlat Gauge(Color fill, Color? border = null, int radius = 7, int bw = 2)
    {
        var sb = new StyleBoxFlat { BgColor = fill, BorderColor = border ?? Ink, AntiAliasing = true, AntiAliasingSize = 0.8f };
        sb.SetCornerRadiusAll(radius);
        sb.SetBorderWidthAll(bw);
        sb.SetContentMarginAll(0);
        return sb;
    }

    // préréglages
    public static StyleBoxTexture Card(int radius = 18, int pad = 14) => Box(Cream, null, Cream2, radius, 3, 4, 4, padH: pad, padV: pad / 2 + 2);
    public static StyleBoxTexture BigCard() => Box(Cream, null, Cream2, 26, 4, 8, 6, padH: 0, padV: 0);
    public static StyleBoxTexture GoldBtn(bool pressed = false, bool hover = false) =>
        Box(hover ? new Color("#ffe891") : new Color("#ffe07a"), hover ? new Color("#ffd24d") : Gold, Gold2, 14, 3, pressed ? 1 : 4, pressed ? 2 : 4, padH: 14, padV: 6);
    public static StyleBoxTexture GreenBtn(bool pressed = false, bool hover = false) =>
        Box(hover ? new Color("#94f0ae") : new Color("#7dea9b"), Green, Green2, 14, 3, pressed ? 1 : 4, pressed ? 2 : 4, padH: 14, padV: 6);
    public static StyleBoxTexture RedBtn(bool pressed = false, bool hover = false) =>
        Box(hover ? new Color("#ffa3a3") : new Color("#ff8f8f"), Red, new Color("#c93a3f"), 14, 3, pressed ? 1 : 4, pressed ? 2 : 4, padH: 14, padV: 6);
    public static StyleBoxTexture CreamBtn(bool pressed = false, bool hover = false) =>
        Box(hover ? Colors.White : Cream, null, Cream2, 14, 3, pressed ? 1 : 4, pressed ? 2 : 4, padH: 14, padV: 6);
    public static StyleBoxTexture DisabledBtn() =>
        Box(new Color("#e6dccb"), null, new Color("#cbbca3"), 14, 3, 4, 4, stroke: new Color("#a99a83"), shadowCol: new Color("#a99a83"), padH: 14, padV: 6);
    public static StyleBoxTexture Tray() => Box(new Color(0.23f, 0.14f, 0.08f), null, null, 22, 3, 4, 0, 0.55f, padH: 10, padV: 8);
    public static StyleBoxTexture FeedItem() => Box(new Color(0.23f, 0.14f, 0.08f), null, null, 12, 2, 0, 0, 0.8f, new Color(1, 1, 1, 0.12f), padH: 10, padV: 5);
    public static StyleBoxTexture Header() => Box(new Color("#ffd96a"), Gold, null, 0, 0, 0, 0, padH: 14, padV: 10);

    public static void ApplyButton(Button b, string kind = "gold", int size = 18)
    {
        Func<bool, bool, StyleBoxTexture> f = kind switch { "green" => GreenBtn, "red" => RedBtn, "cream" => CreamBtn, _ => GoldBtn };
        b.AddThemeStyleboxOverride("normal", f(false, false));
        b.AddThemeStyleboxOverride("hover", f(false, true));
        b.AddThemeStyleboxOverride("pressed", f(true, false));
        b.AddThemeStyleboxOverride("disabled", DisabledBtn());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontOverride("font", Bold);
        b.AddThemeFontSizeOverride("font_size", size);
        var fc = kind is "green" or "red" ? Colors.White : Ink;
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) b.AddThemeColorOverride(k, fc);
        b.AddThemeColorOverride("font_disabled_color", new Color("#9a8b74"));
        if (kind is "green" or "red") { b.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.25f)); b.AddThemeConstantOverride("outline_size", 4); }
        b.AddThemeColorOverride("icon_normal_color", Colors.White);
        b.AddThemeConstantOverride("h_separation", 6);
        b.FocusMode = Control.FocusModeEnum.None;
        b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
    }

    // texte « autocollant » : contour encre + ombre portée
    public static void Sticker(Label l, int size, Color col, int outline = 10, int shadow = 4)
    {
        l.AddThemeFontOverride("font", Bold);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        l.AddThemeColorOverride("font_outline_color", Ink);
        l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeColorOverride("font_shadow_color", Ink);
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", shadow);
        l.AddThemeConstantOverride("shadow_outline_size", outline);
    }

    public static Theme BuildTheme()
    {
        Init();
        var t = new Theme { DefaultFont = Font, DefaultFontSize = 18 };
        t.SetColor("font_color", "Label", Ink);
        // boutons par défaut : dorés
        t.SetStylebox("normal", "Button", GoldBtn());
        t.SetStylebox("hover", "Button", GoldBtn(false, true));
        t.SetStylebox("pressed", "Button", GoldBtn(true));
        t.SetStylebox("disabled", "Button", DisabledBtn());
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetFont("font", "Button", Bold);
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) t.SetColor(k, "Button", Ink);
        t.SetColor("font_disabled_color", "Button", new Color("#9a8b74"));
        t.SetStylebox("panel", "PanelContainer", Card());
        // champ de texte
        var le = Box(Colors.White, null, null, 14, 3, 0, 0, padH: 14, padV: 8);
        t.SetStylebox("normal", "LineEdit", le);
        t.SetStylebox("focus", "LineEdit", Box(Colors.White, null, null, 14, 3, 0, 0, stroke: Gold3, padH: 14, padV: 8));
        t.SetColor("font_color", "LineEdit", Ink);
        t.SetColor("caret_color", "LineEdit", Ink);
        t.SetFont("font", "LineEdit", Bold);
        // jauges
        t.SetStylebox("background", "ProgressBar", Gauge(new Color("#e8d5b2")));
        t.SetStylebox("fill", "ProgressBar", Gauge(new Color("#f59a4a")));
        // barres de défilement
        var grab = Box(Edge, null, null, 7, 0, 0, 0, padH: 4, padV: 4);
        t.SetStylebox("grabber", "VScrollBar", grab);
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(Gold2, null, null, 7, 0, 0, 0, padH: 4, padV: 4));
        t.SetStylebox("grabber_pressed", "VScrollBar", Box(Gold3, null, null, 7, 0, 0, 0, padH: 4, padV: 4));
        t.SetStylebox("scroll", "VScrollBar", Box(new Color("#f3e6cc"), null, null, 7, 0, 0, 0, padH: 4, padV: 4));
        // curseurs
        t.SetStylebox("slider", "HSlider", Box(new Color("#e8d5b2"), null, null, 6, 2, 0, 0, padH: 0, padV: 3));
        t.SetStylebox("grabber_area", "HSlider", Box(new Color("#ffe07a"), Gold, null, 6, 2, 0, 0, padH: 0, padV: 3));
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(new Color("#ffe891"), Gold, null, 6, 2, 0, 0, padH: 0, padV: 3));
        var knob = Svg($"<svg xmlns='http://www.w3.org/2000/svg' width='30' height='32'><circle cx='15' cy='17' r='12' fill='{H(Ink)}'/><circle cx='15' cy='14' r='12' fill='#fff8ea' stroke='{H(Ink)}' stroke-width='3'/><circle cx='15' cy='14' r='5' fill='{H(Gold)}'/></svg>");
        t.SetIcon("grabber", "HSlider", knob);
        t.SetIcon("grabber_highlight", "HSlider", knob);
        // case à cocher (interrupteur)
        var on = Svg($"<svg xmlns='http://www.w3.org/2000/svg' width='58' height='34'><rect x='1.5' y='4.5' width='55' height='28' rx='14' fill='{H(Ink)}'/><rect x='1.5' y='1.5' width='55' height='28' rx='14' fill='{H(Green)}' stroke='{H(Ink)}' stroke-width='3'/><circle cx='41' cy='15.5' r='10' fill='#fff' stroke='{H(Ink)}' stroke-width='3'/></svg>");
        var off = Svg($"<svg xmlns='http://www.w3.org/2000/svg' width='58' height='34'><rect x='1.5' y='4.5' width='55' height='28' rx='14' fill='{H(Ink)}'/><rect x='1.5' y='1.5' width='55' height='28' rx='14' fill='#e6dccb' stroke='{H(Ink)}' stroke-width='3'/><circle cx='17' cy='15.5' r='10' fill='#fff' stroke='{H(Ink)}' stroke-width='3'/></svg>");
        t.SetIcon("checked", "CheckButton", on);
        t.SetIcon("unchecked", "CheckButton", off);
        t.SetIcon("checked", "CheckBox", on);
        t.SetIcon("unchecked", "CheckBox", off);
        foreach (var s in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" }) t.SetStylebox(s, "CheckButton", new StyleBoxEmpty());
        t.SetColor("font_color", "CheckButton", Ink);
        t.SetColor("font_hover_color", "CheckButton", Ink);
        t.SetColor("font_pressed_color", "CheckButton", Ink);
        t.SetFont("font", "CheckButton", Bold);
        // infobulles
        t.SetStylebox("panel", "TooltipPanel", Card(14, 12));
        t.SetColor("font_color", "TooltipLabel", Ink);
        return t;
    }

    // touche de clavier (petite capsule blanche)
    public static Label Kbd(string k, int size = 13)
    {
        var l = new Label { Text = k, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", Bold);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Ink);
        l.AddThemeStyleboxOverride("normal", Box(Colors.White, null, null, 6, 2, 2, 0, padH: 5, padV: 0));
        return l;
    }

    public static TextureRect IconRect(string name, int size)
    {
        return new TextureRect
        {
            Texture = Icon(name), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}
