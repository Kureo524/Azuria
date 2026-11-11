using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>Bord d'écran auquel un tiroir est attaché.</summary>
internal enum DrawerEdge
{
    /// <summary>Collé au bord haut : congés concaves en haut à gauche/droite, bas arrondi.</summary>
    Top,
    /// <summary>Collé au bord bas : congés concaves en bas à gauche/droite, haut arrondi.</summary>
    Bottom,
    /// <summary>Pleine hauteur, collé au bord gauche : congés concaves sur le côté droit, en haut et en bas.</summary>
    Left,
    /// <summary>Pleine hauteur, collé au bord droit : congés concaves sur le côté gauche, en haut et en bas.</summary>
    Right,
}

/// <summary>
/// Découpe la fenêtre à la forme d'un « tiroir » qui sort d'un bord d'écran : le corps du panneau,
/// plus des congés concaves là où il rejoint le bord (le panneau semble pousser hors du bord).
/// La région découpe aussi le matériau Acrylic de DWM. La page dessine la même forme (web/drawer.js).
/// </summary>
internal static class Shaping
{
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern nint CreateEllipticRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint a, nint b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint rgn, bool redraw);

    private const int RGN_OR = 2, RGN_DIFF = 4;

    /// <summary>
    /// Applique la forme de tiroir à une fenêtre de <paramref name="w"/>×<paramref name="h"/> px
    /// (taille de la fenêtre, congés compris), avec des rayons de <paramref name="r"/> px.
    /// </summary>
    public static void ApplyDrawer(nint hwnd, DrawerEdge edge, int w, int h, int r)
    {
        if (w <= 0 || h <= 0) return;
        r = Math.Max(1, Math.Min(r, Math.Min(w, h) / 2));

        nint body, f1, f2;
        switch (edge)
        {
            case DrawerEdge.Top:
                // Corps entre les deux congés ; ses coins hauts sortent de la fenêtre (donc carrés).
                body = CreateRoundRectRgn(r, -2 * r, w - r + 1, h + 1, 2 * r, 2 * r);
                f1 = Fillet(0, 0, r, r, -r, 0, r, 2 * r);
                f2 = Fillet(w - r, 0, w, r, w - r, 0, w + r, 2 * r);
                break;
            case DrawerEdge.Bottom:
                body = CreateRoundRectRgn(r, 0, w - r + 1, h + 2 * r, 2 * r, 2 * r);
                f1 = Fillet(0, h - r, r, h, -r, h - 2 * r, r, h);
                f2 = Fillet(w - r, h - r, w, h, w - r, h - 2 * r, w + r, h);
                break;
            case DrawerEdge.Left:
                body = CreateRectRgn(0, 0, w - r, h);
                f1 = Fillet(w - r, 0, w, r, w - r, 0, w + r, 2 * r);
                f2 = Fillet(w - r, h - r, w, h, w - r, h - 2 * r, w + r, h);
                break;
            default: // Right
                body = CreateRectRgn(r, 0, w, h);
                f1 = Fillet(0, 0, r, r, -r, 0, r, 2 * r);
                f2 = Fillet(0, h - r, r, h, -r, h - 2 * r, r, h);
                break;
        }

        nint shape = CreateRectRgn(0, 0, 0, 0);
        CombineRgn(shape, body, f1, RGN_OR);
        CombineRgn(shape, shape, f2, RGN_OR);
        DeleteObject(body);
        DeleteObject(f1);
        DeleteObject(f2);
        // Après SetWindowRgn, la région appartient au système : pas de DeleteObject.
        SetWindowRgn(hwnd, shape, true);
    }

    /// <summary>Congé concave : le carré (sl,st,sr,sb) privé du disque inscrit dans (el,et,er,eb).</summary>
    private static nint Fillet(int sl, int st, int sr, int sb, int el, int et, int er, int eb)
    {
        nint square = CreateRectRgn(sl, st, sr, sb);
        nint disk = CreateEllipticRgn(el, et, er, eb);
        CombineRgn(square, square, disk, RGN_DIFF);
        DeleteObject(disk);
        return square;
    }
}
