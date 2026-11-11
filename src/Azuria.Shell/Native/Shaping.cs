using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Découpe la fenêtre (cadre + matériau DWM compris) en rectangle arrondi, via une région.
/// C'est ce qui empêche les coins carrés du verre de dépasser au-delà de l'arrondi.
/// </summary>
internal static class Shaping
{
    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int wEllipse, int hEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint hrgn, bool redraw);

    /// <summary>Applique un rectangle arrondi de <paramref name="radius"/> px (physiques) à la fenêtre.</summary>
    public static void RoundWindow(nint hwnd, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0) return;
        int d = Math.Min(radius * 2, Math.Min(width, height)); // diamètre des coins, borné
        // +1 car CreateRoundRectRgn exclut le bord bas/droit.
        nint rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, d, d);
        // Après SetWindowRgn, la région appartient au système : pas de DeleteObject.
        SetWindowRgn(hwnd, rgn, true);
    }
}
