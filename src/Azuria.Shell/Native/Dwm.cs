using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Le strict nécessaire de DWM pour poser le verre d'Azuria sur une fenêtre à nous.
/// API documentées uniquement : aucun pilote, aucune injection (voir CLAUDE.md).
/// Recette validée par le prototype <c>prototypes/verre-floute</c>.
/// </summary>
internal static class Dwm
{
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>Acrylic : floute ce qui est derrière la fenêtre (DWMSBT_TRANSIENTWINDOW).</summary>
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    /// <summary>Aucun matériau : repli opaque quand la transparence est coupée.</summary>
    private const int DWMSBT_NONE = 1;

    public enum Corner { Default = 0, DoNotRound = 1, Round = 2, RoundSmall = 3 }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    /// <summary>Force le thème sombre du cadre et du matériau (sinon l'Acrylic devient gris délavé).</summary>
    public static void UseDarkMode(nint hwnd, bool dark = true)
    {
        int v = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }

    /// <summary>Étend le matériau DWM à toute la fenêtre, pas seulement au cadre.</summary>
    public static void ExtendFrameFull(nint hwnd)
    {
        var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
    }

    public static void SetCorner(nint hwnd, Corner corner)
    {
        int v = (int)corner;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int));
    }

    /// <summary>Active (ou retire) le flou Acrylic. <paramref name="blur"/> = false → matériau NONE (repli).</summary>
    public static void SetAcrylic(nint hwnd, bool blur)
    {
        int v = blur ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int));
    }
}
