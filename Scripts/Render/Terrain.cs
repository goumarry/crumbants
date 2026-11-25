// ─────────────────────────────────────────────────────────────
// Formes du terrain partagées par le rendu et la physique :
// zone de marche, sol (fourmilière, semelles des gâteaux), herbe.
// ─────────────────────────────────────────────────────────────
using System;
using Godot;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public static class Terrain
{
    // Fourmilière : dôme de terre en (Nest.X, Nest.Y) qu'on peut escalader
    public const float NestDomeR = 4.4f, NestDomeH = 2.4f;

    public static float NestH(float dx, float dz)
    {
        float r = MathF.Sqrt(dx * dx + dz * dz);
        float h = 0;
        if (r < NestDomeR) h = NestDomeH * MathF.Pow(1 - (r / NestDomeR) * (r / NestDomeR), 0.75f);
        // anneau de terre rejetée
        float ring = 0.35f * MathF.Exp(-MathF.Pow((r - NestDomeR - 0.4f) / 0.7f, 2));
        // l'entrée : un entonnoir au sommet
        float hole = r < 1.1f ? -0.9f * (1 - r / 1.1f) * (1 - r / 1.1f) : 0;
        return MathF.Max(0, h + ring + hole);
    }

    // Assiette de porcelaine sous chaque gâteau : le gâteau est posé dans le
    // creux (sol des galeries = BoardH), un marli remonte jusqu'au rebord, puis
    // la lèvre arrondie redescend sur la couverture. Profil (rayon, hauteur)
    // partagé par le rendu (WorldBuilder) et la marche (GroundAt).
    public const float BoardH = 0.16f;
    const float PW = 0.6f; // largeur du marli (le profil ci-dessous est en « pas » de 1/PW)
    public static readonly (float r, float y)[] PlateTop =
    {
        (CakeR - 1.0f, BoardH - 0.01f), (CakeR + 0.6f * PW, BoardH - 0.01f), (CakeR + 1.2f * PW, 0.22f), (CakeR + 1.8f * PW, 0.36f),
        (CakeR + 2.4f * PW, 0.56f), (CakeR + 2.9f * PW, 0.76f), (CakeR + 3.25f * PW, 0.88f), (CakeR + 3.55f * PW, 0.93f), (CakeR + 3.85f * PW, 0.9f),
        (CakeR + 4.1f * PW, 0.78f), (CakeR + 4.3f * PW, 0.6f), (CakeR + 4.42f * PW, 0.38f), (CakeR + 4.46f * PW, 0.17f), (CakeR + 4.42f * PW, 0.0f),
    };
    public static float RingOf(float k) => CakeR + k * PW; // rayon d'un repère du profil
    public static float PlateOuterR => PlateTop[^2].r;

    public static float PlateH(float r)
    {
        if (r <= PlateTop[0].r) return BoardH;
        for (int i = 1; i < PlateTop.Length - 1; i++)
        {
            var (r0, y0) = PlateTop[i - 1]; var (r1, y1) = PlateTop[i];
            if (r <= r1) return y0 + (y1 - y0) * (r - r0) / MathF.Max(1e-4f, r1 - r0);
        }
        return 0;
    }

    // sol avec le dessus des objets du décor (ouvrières, bâtiments, objets posés)
    public static float GroundAt(float x, float z) => MathF.Max(BaseGroundAt(x, z), PropField.Height(x, z));

    // sol sans les objets (la fourmi et la caméra testent les objets en vraie 3D : PropPhysics)
    public static float BaseGroundAt(float x, float z)
    {
        float g = NestH(x - Nest.X, z - Nest.Y);
        foreach (var p in CakePos)
        {
            float dx = x - p.X, dz = z - p.Y;
            if (dx * dx + dz * dz < (CakeR + 4.6f) * (CakeR + 4.6f)) g = MathF.Max(g, PlateH(MathF.Sqrt(dx * dx + dz * dz)));
        }
        return g;
    }

    public static float EdgeDist(float x, float z) =>
        MathF.Min(MathF.Min(x - BlanketX0, BlanketX1 - x), MathF.Min(z - BlanketZ0, BlanketZ1 - z));

    public static float ClearDist(float x, float z) => MathF.Sqrt(x * x + (z - ClearCZ) * (z - ClearCZ));
    public static bool InClearing(float x, float z) => ClearDist(x, z) <= ClearR;
    public static bool InMoveArea(float x, float z) => EdgeDist(x, z) >= 0.6f || InClearing(x, z);

    // Hauteur de l'herbe : rase dans la clairière du nid, puis un mur qui cache l'horizon.
    public static float GrassHeightAt(float x, float z)
    {
        float e = -EdgeDist(x, z); // > 0 hors de la couverture
        if (e <= 0) return 0;
        float rc = ClearDist(x, z);
        // clairière : herbe rase qui monte doucement vers le bord, puis le mur d'herbe — sans marche
        if (rc <= ClearR) return 0.6f + 1.6f * Mathf.SmoothStep(ClearR - 12, ClearR, rc);
        float dist = MathF.Min(e, rc - ClearR); // distance à la couverture ou à la clairière
        return MathF.Min(75, 2.2f + MathF.Pow(MathF.Max(0, dist), 1.3f) * 0.62f);
    }
}
