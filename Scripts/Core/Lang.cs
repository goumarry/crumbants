// ─────────────────────────────────────────────────────────────
// Langues : français (langue source), anglais, espagnol, allemand.
// Les textes sont écrits en français dans le code ; la clé de
// traduction EST le texte français (Scripts/Core/Strings.cs).
//  - Labels / Buttons / Label3D : Godot les traduit tout seul
//    (auto-translate) et les met à jour au changement de langue.
//  - Textes composés : L.F("Gâteau {0} · {1}", n, nom).
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;

namespace Crumbants;

public static class L
{
    public static readonly string[] Codes = { "fr", "en", "es", "de" };
    public static readonly string[] Names = { "Français", "English", "Español", "Deutsch" };

    public static string Code { get; private set; } = "fr";
    public static event Action Changed;
    static bool _loaded;

    // langue de départ : celle des réglages, sinon celle du système (anglais si non gérée)
    public static string Default()
    {
        string os = OS.GetLocaleLanguage();
        return Array.IndexOf(Codes, os) >= 0 ? os : "en";
    }

    public static void Set(string code)
    {
        if (Array.IndexOf(Codes, code) < 0) code = "en";
        if (!_loaded) Load();
        bool changed = code != Code;
        Code = code;
        TranslationServer.SetLocale(code);
        if (changed) Changed?.Invoke();
    }

    static void Load()
    {
        _loaded = true;
        var tables = new Translation[Codes.Length];
        for (int i = 1; i < Codes.Length; i++) tables[i] = new Translation { Locale = Codes[i] };
        foreach (var row in Strings.Rows())
        {
            for (int i = 1; i < Codes.Length && i < row.Length; i++)
                if (!string.IsNullOrEmpty(row[i])) tables[i].AddMessage(row[0], row[i]);
        }
        for (int i = 1; i < Codes.Length; i++) TranslationServer.AddTranslation(tables[i]);
    }

    public static string T(string fr) => string.IsNullOrEmpty(fr) || Code == "fr" ? fr : TranslationServer.Translate(fr).ToString();
    public static string F(string fr, params object[] a) => string.Format(T(fr), a);

    // « 1 ouvrière » / « 3 ouvrières » : deux clés, choisies selon n
    public static string P(long n, string one, string many) => string.Format(T(n == 1 ? one : many), n);

    // minuscules en milieu de phrase… sauf en allemand (les noms gardent leur majuscule)
    public static string Lower(string s) => Code == "de" || string.IsNullOrEmpty(s) ? s : s.ToLower();

    // Nombres : séparateur décimal et suffixes (k, M, Md/B/mil M/Mrd…)
    public static char Dec => Code == "en" ? '.' : ',';
    public static string N(double v, string fmt) => v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture).Replace('.', Dec);
    public static string Billion => Code switch { "en" => "B", "es" => "mil M", "de" => "Mrd", _ => "Md" };

    // nom affiché d'une touche physique selon la disposition du clavier (ex. M → « , » en AZERTY)
    public static string KeyName(Key physical)
    {
        var lab = DisplayServer.KeyboardGetLabelFromPhysical(physical);
        if (lab == Key.None) lab = physical;
        long c = (long)lab;
        // touches imprimables : le caractère lui-même (« , » plutôt que « Comma »)
        return (c > 0x20 && c < 0x250 ? ((char)c).ToString() : OS.GetKeycodeString(lab)).ToUpper();
    }

    // Les touches de déplacement suivent la disposition du clavier (ZQSD en AZERTY, WASD en QWERTY…)
    public static string MoveKeys()
    {
        string s = "";
        foreach (var k in new[] { Key.W, Key.A, Key.S, Key.D }) s += KeyName(k);
        return s;
    }
}
