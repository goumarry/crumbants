// ─────────────────────────────────────────────────────────────
// Bibliothèque de matériaux : remplace les couleurs unies exportées
// de Blender (par nom de matériau) par des surfaces procédurales
// (Shaders/prop.gdshader), du verre et des liquides.
// ─────────────────────────────────────────────────────────────
using System.Collections.Generic;
using Godot;

namespace Crumbants.Render;

public static class MatLib
{
    // nom Blender → (famille, échelle du motif, relief)
    static readonly Dictionary<string, (int kind, float scale, float bump)> Kinds = new()
    {
        ["Wood"] = (1, 1.2f, 1), ["WoodDark"] = (1, 1.2f, 1), ["WoodHandle"] = (1, 1.5f, 0.8f), ["SignWood"] = (1, 0.5f, 1), ["SignWoodDark"] = (1, 0.5f, 1),
        ["MatchWood"] = (1, 0.4f, 0.6f), ["Stem"] = (1, 0.6f, 0.6f), ["Twig"] = (1, 0.3f, 0.6f), ["PineNeedle"] = (1, 0.3f, 0.5f),
        ["Wicker"] = (2, 2.2f, 1.2f), ["WickerDark"] = (2, 2.2f, 1.2f),
        ["Faience"] = (3, 3f, 1), ["Marble"] = (3, 2f, 1), ["Shell"] = (3, 1f, 0.5f), ["ShellDark"] = (3, 1f, 0.5f), ["CiderCeramic"] = (3, 3f, 1),
        ["PaintBlue"] = (3, 3f, 1), ["PaintYellow"] = (3, 3f, 1),
        ["TowelRed"] = (4, 1.2f, 1), ["TowelWhite"] = (4, 1.2f, 1), ["Linen"] = (4, 1.0f, 1), ["Stitch"] = (4, 0.5f, 0.6f), ["Cushion"] = (4, 0.15f, 0.6f), ["CushionGingham"] = (4, 0.15f, 0.6f), ["Eraser"] = (11, 1, 1), ["Ink"] = (11, 1, 0.2f),
        ["Gingham"] = (4, 0.6f, 0.8f), ["String"] = (4, 0.3f, 0.6f), ["Rope"] = (4, 0.2f, 0.8f),
        ["Steel"] = (5, 1f, 1), ["Steel2"] = (5, 2f, 1), ["Iron"] = (5, 0.5f, 1), ["BeltLeg"] = (5, 0.3f, 1), ["BeltPlate"] = (5, 0.3f, 1),
        ["BeltRivet"] = (5, 0.1f, 0.5f), ["BeltRoller"] = (5, 0.2f, 1), ["Brass"] = (5, 0.5f, 1), ["Brass2"] = (5, 1f, 1), ["Copper"] = (5, 0.5f, 1), ["CardGold"] = (11, 1f, 0.3f),
        ["Cork"] = (6, 0.8f, 1),
        ["Bread"] = (7, 2.5f, 1.2f), ["BreadLight"] = (7, 2.5f, 1), ["GaletteCrust"] = (7, 2f, 1), ["GaletteFill"] = (7, 1.5f, 0.8f), ["Crumb"] = (7, 0.15f, 1), ["CrumbPile"] = (7, 0.2f, 1),
        ["Apple"] = (8, 1.5f, 0.6f), ["Clementine"] = (8, 0.6f, 1), ["CherryRed"] = (8, 0.8f, 0.4f), ["Cherry"] = (8, 0.2f, 0.4f),
        ["NestGrain"] = (9, 0.15f, 1), ["NestPebble"] = (9, 0.12f, 1), ["NestPebble2"] = (9, 0.12f, 1), ["Stone"] = (9, 0.3f, 1), ["Concrete"] = (9, 0.3f, 0.8f), ["Brick"] = (9, 0.25f, 1),
        ["Paper"] = (10, 0.6f, 1), ["Paper2"] = (10, 0.6f, 1), ["Label"] = (10, 2f, 1), ["MatchBox"] = (10, 0.4f, 1), ["Striker"] = (9, 0.05f, 1), ["MatchYellow"] = (10, 0.4f, 1),
        ["Red"] = (11, 1, 1), ["Blue"] = (11, 1, 1), ["Green"] = (11, 1, 1), ["Yellow"] = (11, 1, 1), ["Pink"] = (11, 1, 1), ["Black"] = (11, 1, 1), ["Cream"] = (11, 1, 1),
        ["MushCap"] = (8, 0.5f, 0.3f), ["MushDot"] = (11, 1, 1), ["MushStem"] = (10, 0.3f, 1), ["JewelBlue"] = (11, 1, 0.2f), ["JewelRed"] = (11, 1, 0.2f),
        ["BeltRubber"] = (9, 0.08f, 0.5f), ["BeltRail"] = (11, 1, 1),
        // machines (Blender/buildings2.py) et panneaux des gâteaux
        ["Orange"] = (11, 1, 1), ["Teal"] = (11, 1, 1), ["Rubber"] = (9, 0.08f, 0.4f), ["Chrome"] = (5, 2f, 0.4f),
        ["AppleGreen"] = (8, 1.5f, 0.6f), ["Grape"] = (8, 0.5f, 0.3f), ["Cheese"] = (10, 0.5f, 0.8f),
        ["Navy"] = (11, 1, 1), ["Volt"] = (11, 1, 1),
        ["Chalk"] = (9, 0.4f, 0.3f), ["ChalkLine"] = (11, 1, 0.2f), ["Ribbon"] = (4, 0.6f, 0.6f),
        ["Leaf"] = (12, 1f, 1), ["LeafDry"] = (12, 3f, 1), ["LeafVein"] = (12, 1f, 0.5f), ["Leather"] = (10, 0.5f, 0.6f), ["WineStain"] = (6, 0.8f, 0.5f), ["Sugar"] = (13, 0.6f, 1),
    };
    static readonly HashSet<string> Glass = new() { "Glass", "JarGlass", "BottleGlass" };
    static readonly HashSet<string> Liquid = new() { "Cider", "Jam" };
    static readonly Dictionary<Material, Material> _cache = new();
    static Shader _prop, _glass;

    public static Material Upgrade(Material src)
    {
        if (src == null) return null;
        if (_cache.TryGetValue(src, out var done)) return done;
        Material res = src;
        string name = src.ResourceName ?? "";
        int dot = name.LastIndexOf('.'); if (dot > 0 && int.TryParse(name[(dot + 1)..], out _)) name = name[..dot]; // « Wood.001 » → « Wood »
        var sm = src as StandardMaterial3D;
        if (sm != null)
        {
            var c = sm.AlbedoColor;
            if (Glass.Contains(name))
            {
                _glass ??= GD.Load<Shader>("res://Shaders/glass.gdshader");
                var g = new ShaderMaterial { Shader = _glass };
                g.SetShaderParameter("tint", new Color(c.R, c.G, c.B));
                g.SetShaderParameter("alpha_min", name == "BottleGlass" ? 0.8f : 0.08f);
                res = g;
            }
            else if (Liquid.Contains(name))
            {
                // opaque : le verre (qui redessine l'arrière-plan) le voit toujours, sous tous les angles
                res = new StandardMaterial3D
                {
                    AlbedoColor = new Color(c.R, c.G, c.B), Roughness = 0.04f, MetallicSpecular = 0.7f,
                    BacklightEnabled = true, Backlight = c * 0.8f, RimEnabled = true, Rim = 0.3f, RimTint = 0.8f,
                };
            }
            else if (Kinds.TryGetValue(name, out var k) && !sm.EmissionEnabled)
            {
                _prop ??= GD.Load<Shader>("res://Shaders/prop.gdshader");
                var m = new ShaderMaterial { Shader = _prop, ResourceName = name };
                m.SetShaderParameter("kind", k.kind);
                m.SetShaderParameter("albedo", new Color(c.R, c.G, c.B));
                m.SetShaderParameter("roughness", sm.Roughness);
                m.SetShaderParameter("metallic", sm.Metallic);
                m.SetShaderParameter("scale", k.scale);
                m.SetShaderParameter("bump", k.bump);
                res = m;
            }
        }
        _cache[src] = res;
        return res;
    }

    public static Mesh UpgradeMesh(Mesh m)
    {
        bool any = false;
        for (int i = 0; i < m.GetSurfaceCount(); i++)
        {
            var src = m.SurfaceGetMaterial(i);
            if (Upgrade(src) != src) any = true;
        }
        if (!any) return m;
        var copy = (Mesh)m.Duplicate();
        for (int i = 0; i < copy.GetSurfaceCount(); i++) copy.SurfaceSetMaterial(i, Upgrade(m.SurfaceGetMaterial(i)));
        return copy;
    }
}
