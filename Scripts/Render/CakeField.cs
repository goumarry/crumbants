// ─────────────────────────────────────────────────────────────
// Champ de densité d'un gâteau et extraction de sa surface LISSE
// (« surface nets »). La même densité sert au maillage ET aux
// collisions : la surface visible est l'iso 0,5 de ce champ.
// Thread-safe pour BuildChunk (tampons locaux), afin de construire
// toutes les tranches en parallèle au chargement.
// ─────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using Godot;
using Crumbants.Sim;
using static Crumbants.Cfg;

namespace Crumbants.Render;

public sealed class ChunkArrays
{
    public Vector3[] Pos;
    public Vector3[] Nor;
    public Color[] Col;   // r = occlusion, g = « sous terre », b = croûte du dessus
    public int[] Idx;
}

public sealed class CakeField
{
    public const int Chunk = 16;
    public static readonly int Nch = (CW + Chunk - 1) / Chunk;
    const float Iso = 0.5f;
    const int Levels = 6; // paliers de grignotage visibles d'un bloc
    static readonly float[] Dens = new float[Levels + 2];
    static readonly (float a, float b)[] Sample4 = { (-0.45f, -0.45f), (0.45f, -0.45f), (-0.45f, 0.45f), (0.45f, 0.45f) };

    static CakeField()
    {
        for (int l = 1; l <= Levels + 1; l++) Dens[l] = 0.56f + 0.44f * ((l - 1) / (float)Levels);
        // bloc vide : un peu de « matière » résiduelle, la paroi se rapproche du vide (~0,2 par côté) :
        // les galeries de 2 blocs paraissent (et sont, pour les collisions) un peu plus serrées
        Dens[0] = 0.28f;
    }

    public readonly CakeSim Sim;
    public readonly byte[] Level;

    public CakeField(CakeSim sim)
    {
        Sim = sim;
        Level = new byte[CW * CW * Layers];
    }

    public byte LevelOf(uint v) => v == 0 ? (byte)0 : (byte)(1 + Math.Min(Levels, (int)Math.Round(Levels * (double)v / Sim.CpvL)));

    public void RefreshAllLevels()
    {
        var c = Sim.Cake;
        for (int b = 0; b < c.Length; b++) Level[b] = LevelOf(c[b]);
    }

    // Densité au centre du bloc (i, yy, k) en coordonnées de grille
    public float SampleD(int i, int yy, int k)
    {
        var h0 = Sim.H0; var hx = Sim.Hx;
        float d = 0;
        bool inGrid = i >= 0 && k >= 0 && i < CW && k < CW;
        if (inGrid && yy < Layers)
        {
            if (yy < 0) yy = 0; // le gâteau continue jusqu'à l'assiette
            int c = i + k * CW, b = c + yy * Area, l = Level[b];
            d = Dens[l];
            // Près de la surface d'origine (dessus, bord arrondi), la densité devient une distance
            // continue à la vraie forme : pente lisse au lieu de marches. Seulement là où rien n'a été mangé.
            int H = h0[c];
            float ox = i - Off + 0.5f, oz = k - Off + 0.5f;
            float rc = MathF.Sqrt(ox * ox + oz * oz);
            bool nearTop = H > 0 && yy >= H - 2, nearRim = rc > CakeR - 2.5f;
            if (nearTop || nearRim)
            {
                float field = 0.5f + 0.5f * MathF.Min((H > 0 ? hx[c] : Layers) - (yy + 0.5f), CakeR - rc);
                if (l == Levels + 1) d = Math.Clamp(field, 0.52f, 1.5f);
                else if (l == 0 && (H == 0 || yy >= H)) d = Math.Clamp(field, 0, 0.48f);
            }
        }
        else if (inGrid && yy >= Layers)
        {
            int c = i + k * CW;
            d = h0[c] > 0 ? Math.Clamp(0.5f + 0.5f * (hx[c] - (yy + 0.5f)), 0, 0.48f) : 0;
        }
        else if (yy < Layers)
        {
            float ox = i - Off + 0.5f, oz = k - Off + 0.5f;
            float rc = MathF.Sqrt(ox * ox + oz * oz);
            d = Math.Clamp(0.5f + 0.5f * (CakeR - rc), 0, 0.48f);
        }
        return d;
    }

    // Densité interpolée (trilinéaire) en un point du monde
    public float DensityAt(float x, float y, float z)
    {
        float gi = x - Sim.Cx + Off - 0.5f, gy = y - 0.5f, gk = z - Sim.Cz + Off - 0.5f;
        int i0 = Mathf.FloorToInt(gi), y0 = Mathf.FloorToInt(gy), k0 = Mathf.FloorToInt(gk);
        float fx = gi - i0, fy = gy - y0, fz = gk - k0;
        float d = 0;
        for (int c = 0; c < 8; c++)
        {
            int a = c & 1, b = (c >> 1) & 1, e = (c >> 2) & 1;
            float w = (a == 1 ? fx : 1 - fx) * (b == 1 ? fy : 1 - fy) * (e == 1 ? fz : 1 - fz);
            if (w > 0) d += w * SampleD(i0 + a, y0 + b, k0 + e);
        }
        return d;
    }

    // ───── Construction d'une tranche (16 × 16 colonnes) ─────
    public ChunkArrays BuildChunk(int n)
    {
        var colTop = Sim.ColTop; var crust = Sim.Crust;
        int ci0 = (n % Nch) * Chunk, ck0 = (n / Nch) * Chunk;
        int ci1 = Math.Min(CW, ci0 + Chunk), ck1 = Math.Min(CW, ck0 + Chunk);
        int LW = Chunk + 6, LH = Layers + 4;
        int ox = ci0 - 3, oy = -2, oz = ck0 - 3;
        var loc = new float[LW * LW * LH];
        var grad = new float[LW * LW * LH * 3];
        var gradOk = new bool[LW * LW * LH];
        for (int y = 0; y < LH; y++)
        for (int z = 0; z < LW; z++)
        for (int x = 0; x < LW; x++) loc[x + z * LW + y * LW * LW] = SampleD(x + ox, y + oy, z + oz);

        float D(int i, int y, int k)
        {
            int x = i - ox, yy = y - oy, z = k - oz;
            if (x < 0 || z < 0 || yy < 0 || x >= LW || z >= LW || yy >= LH) return 0;
            return loc[x + z * LW + yy * LW * LW];
        }

        // gradient adouci (Sobel 3×3×3)
        int G(int i, int y, int k)
        {
            int x = i - ox, yy = y - oy, z = k - oz;
            x = Math.Clamp(x, 0, LW - 1); yy = Math.Clamp(yy, 0, LH - 1); z = Math.Clamp(z, 0, LW - 1);
            int id = x + z * LW + yy * LW * LW;
            if (!gradOk[id])
            {
                float gx = 0, gy = 0, gz = 0;
                for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                {
                    float w = (2 - Math.Abs(a)) * (2 - Math.Abs(b));
                    gx += w * (D(i + 1, y + a, k + b) - D(i - 1, y + a, k + b));
                    gy += w * (D(i + a, y + 1, k + b) - D(i + a, y - 1, k + b));
                    gz += w * (D(i + a, y + b, k + 1) - D(i + a, y + b, k - 1));
                }
                grad[id * 3] = gx; grad[id * 3 + 1] = gy; grad[id * 3 + 2] = gz;
                gradOk[id] = true;
            }
            return id * 3;
        }

        var pos = new List<Vector3>(4096);
        var nor = new List<Vector3>(4096);
        var col = new List<Color>(4096);
        var index = new List<int>(8192);
        var vmap = new Dictionary<int, int>(4096);
        Span<float> cd = stackalloc float[8];

        int Vertex(int ci, int cy, int ck, Span<float> cd)
        {
            int key = (ci + 2) + (cy + 2) * 100 + (ck + 2) * 10000;
            if (vmap.TryGetValue(key, out int got)) return got;
            for (int c = 0; c < 8; c++) cd[c] = D(ci + (c & 1), cy + ((c >> 1) & 1), ck + ((c >> 2) & 1));
            float px = 0, py = 0, pz = 0, fy = 0; int cnt = 0, fn = 0;
            bool lowStep = false, highStep = false;
            for (int c = 0; c < 8; c++)
            {
                for (int bit = 1; bit <= 4; bit <<= 1)
                {
                    if ((c & bit) != 0) continue;
                    int c2 = c | bit;
                    float a = cd[c], b = cd[c2];
                    if ((a >= Iso) == (b >= Iso)) continue;
                    float t = (Iso - a) / (b - a);
                    int ax = c & 1, ay = (c >> 1) & 1, az = (c >> 2) & 1;
                    px += ax + (bit == 1 ? t : 0);
                    py += ay + (bit == 2 ? t : 0);
                    pz += az + (bit == 4 ? t : 0);
                    cnt++;
                    if (bit == 2 && a >= Iso) { fy += ay + t; fn++; } // un sol (plein dessous, vide dessus)
                    if (bit != 2)
                    {
                        int sc = a >= Iso ? c : c2, ec = a >= Iso ? c2 : c;
                        if (((sc >> 1) & 1) != 0)
                        {
                            if (cd[sc & ~2] >= Iso && cd[ec & ~2] >= Iso && D(ci + (sc & 1), cy + 2, ck + ((sc >> 2) & 1)) < Iso) lowStep = true;
                        }
                        else if (cd[sc | 2] < Iso) highStep = true;
                    }
                }
            }
            px /= cnt; py /= cnt; pz /= cnt;
            // Sous un plafond (galeries), le sol est posé à sa vraie hauteur ; les marches de rampe sont lissées.
            if (fn > 0)
            {
                bool covered = false;
                for (int dk = 0; dk <= 1 && !covered; dk++)
                for (int di = 0; di <= 1; di++)
                {
                    int ii = ci + di, kk = ck + dk;
                    if (ii >= 0 && kk >= 0 && ii < CW && kk < CW && colTop[ii + kk * CW] > cy + 1) { covered = true; break; }
                }
                if (covered)
                {
                    py = fy / fn;
                    if (lowStep && !highStep) py += 0.5f;
                    else if (highStep && !lowStep) py -= 0.5f;
                }
            }
            float wx = ci + px - Off + 0.5f, wz = ck + pz - Off + 0.5f;
            float wy = MathF.Max(0, cy + py + 0.5f);
            // normale : gradient interpolé
            float nx = 0, ny = 0, nz = 0;
            for (int c = 0; c < 8; c++)
            {
                float fx2 = (c & 1) != 0 ? px : 1 - px, fy2 = (c & 2) != 0 ? py : 1 - py, fz2 = (c & 4) != 0 ? pz : 1 - pz;
                float w = fx2 * fy2 * fz2;
                int gi = G(ci + (c & 1), cy + ((c >> 1) & 1), ck + ((c >> 2) & 1));
                nx -= grad[gi] * w; ny -= grad[gi + 1] * w; nz -= grad[gi + 2] * w;
            }
            float nl = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-6f) { nx = 0; ny = 1; nz = 0; nl = 1; }
            nx /= nl; ny /= nl; nz /= nl;
            // occlusion ambiante : densité moyenne autour (4×4×4)
            int s = 0;
            for (int a = -1; a <= 2; a++) for (int b = -1; b <= 2; b++) for (int c = -1; c <= 2; c++) if (D(ci + a, cy + b, ck + c) > Iso) s++;
            float fr = s / 64f;
            float ao = 1 - Math.Clamp((fr - 0.42f) / 0.4f, 0, 1) * 0.62f;
            // « sous terre » : y a-t-il du gâteau au-dessus ? (depuis un point décalé le long de la normale)
            int cov = 0;
            float sx = wx + nx * 0.7f + Off, sy = wy + ny * 0.7f, sz = wz + nz * 0.7f + Off;
            foreach (var (a, b) in Sample4)
            {
                int i2 = Mathf.FloorToInt(sx + a), k2 = Mathf.FloorToInt(sz + b);
                if (i2 < 0 || k2 < 0 || i2 >= CW || k2 >= CW) continue;
                if (colTop[i2 + k2 * CW] + 1 > sy + 0.3f) cov++;
            }
            float cave = cov / 4f;
            // surface d'origine (jamais croquée) : dorure et feuilletage ; sinon, l'intérieur du gâteau
            float top = 1;
            for (int c = 0; c < 8 && top > 0; c++)
            {
                int si = ci + (c & 1), syy = cy + ((c >> 1) & 1), sk = ck + ((c >> 2) & 1);
                if (si < 0 || sk < 0 || si >= CW || sk >= CW || syy < 0 || syy >= Layers) continue;
                int col2 = si + sk * CW;
                if (Sim.H0[col2] > syy && Level[col2 + syy * Area] < Levels + 1) top = 0;
            }
            int id = pos.Count;
            pos.Add(new Vector3(wx, wy, wz));
            nor.Add(new Vector3(nx, ny, nz));
            col.Add(new Color(ao, cave, top, 1));
            vmap[key] = id;
            return id;
        }

        // Godot : faces avant dans le sens horaire (inverse de three.js)
        void Quad(int q0, int q1, int q2, int q3, bool solidFirst)
        {
            if (solidFirst) { index.Add(q0); index.Add(q2); index.Add(q1); index.Add(q0); index.Add(q3); index.Add(q2); }
            else { index.Add(q0); index.Add(q1); index.Add(q2); index.Add(q0); index.Add(q2); index.Add(q3); }
        }

        int iLo = ci0 == 0 ? -1 : ci0, iHi = ci1;
        int kLo = ck0 == 0 ? -1 : ck0, kHi = ck1;
        for (int y = -1; y < Layers; y++)
        for (int k = kLo; k < kHi; k++)
        for (int i = iLo; i < iHi; i++)
        {
            bool in0 = D(i, y, k) >= Iso;
            if (y >= 0 && in0 != (D(i + 1, y, k) >= Iso))
                Quad(Vertex(i, y - 1, k - 1, cd), Vertex(i, y, k - 1, cd), Vertex(i, y, k, cd), Vertex(i, y - 1, k, cd), in0);
            if (in0 != (D(i, y + 1, k) >= Iso))
                Quad(Vertex(i - 1, y, k - 1, cd), Vertex(i - 1, y, k, cd), Vertex(i, y, k, cd), Vertex(i, y, k - 1, cd), in0);
            if (y >= 0 && in0 != (D(i, y, k + 1) >= Iso))
                Quad(Vertex(i - 1, y - 1, k, cd), Vertex(i, y - 1, k, cd), Vertex(i, y, k, cd), Vertex(i - 1, y, k, cd), in0);
        }

        return new ChunkArrays { Pos = pos.ToArray(), Nor = nor.ToArray(), Col = col.ToArray(), Idx = index.ToArray() };
    }
}
