// Recolors the Kenney tower-defense tile sheet's terrain into an Egyptian palette.
// Compiled and driven by Tools/Art/build-egypt-art.ps1 (PowerShell Add-Type, no Unity needed).
//
// Each pixel of a terrain cell is classified against the four Kenney terrain base colors
// (sand, dirt, grass, stone). Pixels on a material keep their lightness offset from that
// base (so rims, specks and icon lines survive); anti-aliased pixels between two materials
// are re-blended between the two new colors. Texture is added only to flat base pixels and
// depends on tile-local coordinates with periods dividing 64, so neighbouring tiles stay
// seamless. Cells that are not terrain (units, towers, UI, effects) are left untouched.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class EgyptTerrain
{
    const int Cell = 64;
    // Kenney bases: sand, dirt, grass, stone (road)
    static readonly int[] Src = { 0xECDCB8, 0xBB8044, 0x2ECC71, 0x89A4A6 };
    // Egyptian bases: golden sand, terracotta mud-brick, olive oasis, warm granite paving
    static readonly int[] Dst = { 0xE4C385, 0xB0653A, 0x7E9B40, 0x8B7563 };

    public static string RecolorSheet(string input, string output, int[] skipCells)
    {
        using (var src = new Bitmap(input))
        using (var bmp = src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb))
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            var buf = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);

            int cols = bmp.Width / Cell, rows = bmp.Height / Cell, changed = 0;
            var skip = new System.Collections.Generic.HashSet<int>(skipCells);
            for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                int index = cy * cols + cx;
                if (skip.Contains(index) || !IsTerrainCell(buf, stride, cx, cy)) continue;
                changed++;
                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    int o = (cy * Cell + y) * stride + (cx * Cell + x) * 4;
                    if (buf[o + 3] == 0) continue;
                    int r = buf[o + 2], g = buf[o + 1], b = buf[o];
                    Recolor(ref r, ref g, ref b, x, y);
                    buf[o + 2] = (byte)r; buf[o + 1] = (byte)g; buf[o] = (byte)b;
                }
            }
            Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            bmp.UnlockBits(data);
            bmp.Save(output, ImageFormat.Png);
            return changed.ToString();
        }
    }

    // A terrain cell is fully opaque and >= 85% of its pixels sit near a terrain base color.
    static bool IsTerrainCell(byte[] buf, int stride, int cx, int cy)
    {
        int near = 0, total = 0;
        for (int y = 0; y < Cell; y += 2)
        for (int x = 0; x < Cell; x += 2)
        {
            int o = (cy * Cell + y) * stride + (cx * Cell + x) * 4;
            total++;
            if (buf[o + 3] < 250) return false;
            int best = int.MaxValue;
            for (int i = 0; i < 4; i++) best = Math.Min(best, Dist2(buf[o + 2], buf[o + 1], buf[o], Src[i]));
            if (best < 45 * 45) near++;
        }
        return near >= total * 85 / 100;
    }

    static void Recolor(ref int r, ref int g, ref int b, int x, int y)
    {
        int a = -1, c = -1, da = int.MaxValue, dc = int.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            int d = Dist2(r, g, b, Src[i]);
            if (d < da) { dc = da; c = a; da = d; a = i; }
            else if (d < dc) { dc = d; c = i; }
        }

        // Anti-aliased pixel between two materials? Project onto the segment between their bases.
        double residual;
        double t = Project(r, g, b, Src[a], Src[c], out residual);
        if (da > 18 * 18 && t > 0.08 && t < 0.92 && residual < 14)
        {
            int ar, ag, ab, cr, cg, cb;
            Base(a, x, y, out ar, out ag, out ab);
            Base(c, x, y, out cr, out cg, out cb);
            r = Clamp(ar + (cr - ar) * t); g = Clamp(ag + (cg - ag) * t); b = Clamp(ab + (cb - ab) * t);
            return;
        }

        // On a material: keep the lightness offset from its Kenney base (rims, specks, icons).
        double delta = Lum(r, g, b) - Lum(Src[a] >> 16 & 255, Src[a] >> 8 & 255, Src[a] & 255);
        int br, bg, bb;
        if (Math.Abs(delta) < 4) Base(a, x, y, out br, out bg, out bb);      // flat: textured
        else { br = Dst[a] >> 16 & 255; bg = Dst[a] >> 8 & 255; bb = Dst[a] & 255; }
        r = Clamp(br + delta); g = Clamp(bg + delta); b = Clamp(bb + delta);
    }

    // Textured Egyptian base color for material m at tile-local (x, y). Periods divide 64.
    static void Base(int m, int x, int y, out int r, out int g, out int b)
    {
        r = Dst[m] >> 16 & 255; g = Dst[m] >> 8 & 255; b = Dst[m] & 255;
        double shade = 0;
        switch (m)
        {
            case 0: // sand: wind ripples + fine grain
                shade = 5 * Math.Sin(2 * Math.PI * (y + 5 * Math.Sin(2 * Math.PI * x / 64.0)) / 16.0) + Grain(x, y, 4);
                break;
            case 1: // mud-brick: 16x8 bricks, staggered, faint mortar
            {
                int row = y / 8, bx = (x + (row % 2) * 8) % 16;
                bool mortar = y % 8 == 0 || bx == 0;
                shade = (mortar ? -10 : 0) + ((Hash(((x + (row % 2) * 8) / 16) % 4, row) % 7) - 3) + Grain(x, y, 2);
                break;
            }
            case 2: // oasis: soft grass streaks
                shade = 4 * Math.Sin(2 * Math.PI * (x + 2 * y) / 32.0) + Grain(x, y, 6);
                break;
            case 3: // granite paving: 32x16 blocks, staggered rows, dark grout, per-block tone
            {
                int row = y / 16, bx = (x + (row % 2) * 16) % 32, by = y % 16;
                bool grout = by <= 1 || bx <= 1;
                bool edgeLight = by == 2 || bx == 2;
                int block = Hash(((x + (row % 2) * 16) / 32) % 2, row);
                shade = grout ? -26 : (edgeLight ? 7 : 0) + (block % 11) - 5 + Grain(x, y, 3);
                break;
            }
        }
        r = Clamp(r + shade); g = Clamp(g + shade); b = Clamp(b + shade * 0.9);
    }

    static double Grain(int x, int y, int amp) { return (Hash(x, y) % (2 * amp + 1)) - amp; }
    static int Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) & 0x7fffffff; } }
    static double Lum(int r, int g, int b) { return 0.299 * r + 0.587 * g + 0.114 * b; }
    static int Clamp(double v) { return v < 0 ? 0 : v > 255 ? 255 : (int)Math.Round(v); }

    static int Dist2(int r, int g, int b, int rgb)
    {
        int dr = r - (rgb >> 16 & 255), dg = g - (rgb >> 8 & 255), db = b - (rgb & 255);
        return dr * dr + dg * dg + db * db;
    }

    static double Project(int r, int g, int b, int p, int q, out double residual)
    {
        double pr = p >> 16 & 255, pg = p >> 8 & 255, pb = p & 255;
        double vr = (q >> 16 & 255) - pr, vg = (q >> 8 & 255) - pg, vb = (q & 255) - pb;
        double wr = r - pr, wg = g - pg, wb = b - pb;
        double t = (wr * vr + wg * vg + wb * vb) / (vr * vr + vg * vg + vb * vb);
        t = Math.Max(0, Math.Min(1, t));
        double er = wr - t * vr, eg = wg - t * vg, eb = wb - t * vb;
        residual = Math.Sqrt(er * er + eg * eg + eb * eb);
        return t;
    }
}
