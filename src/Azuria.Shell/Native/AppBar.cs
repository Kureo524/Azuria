using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Barre d'application de Windows (<c>SHAppBarMessage</c>, API documentée du shell) : réserve une
/// bande le long d'un bord d'écran, que les fenêtres agrandies n'occupent plus — comme la barre des
/// tâches. Sert au cadre permanent d'Azuria. Windows prévient aussi quand une appli passe en plein
/// écran sur ce moniteur (<see cref="FullScreenApp"/>).
/// </summary>
internal static class AppBar
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;

    /// <summary>Notifications reçues dans <see cref="CallbackMessage"/> (wParam).</summary>
    public const int PosChanged = 1, FullScreenApp = 2;

    /// <summary>Message par lequel Windows prévient nos barres (taskbar déplacée, plein écran…).</summary>
    public static readonly uint CallbackMessage = RegisterWindowMessage("Azuria.AppBar");

    private static uint EdgeCode(DrawerEdge e) => e switch
    {
        DrawerEdge.Left => 0, DrawerEdge.Top => 1, DrawerEdge.Right => 2, _ => 3,
    };

    public static void Register(nint hwnd)
    {
        var d = New(hwnd);
        d.uCallbackMessage = CallbackMessage;
        SHAppBarMessage(ABM_NEW, ref d);
    }

    public static void Unregister(nint hwnd)
    {
        var d = New(hwnd);
        SHAppBarMessage(ABM_REMOVE, ref d);
    }

    /// <summary>
    /// Réserve une bande d'épaisseur <paramref name="thickness"/> le long de <paramref name="edge"/>
    /// sur l'écran <paramref name="monitor"/> ; Windows la décale au besoin (barre des tâches).
    /// Renvoie la bande réellement réservée.
    /// </summary>
    public static Rectangle Reserve(nint hwnd, DrawerEdge edge, Rectangle monitor, int thickness)
    {
        var d = New(hwnd);
        d.uEdge = EdgeCode(edge);
        d.rc = new RECT { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom };
        Clamp(ref d.rc, edge, thickness);
        SHAppBarMessage(ABM_QUERYPOS, ref d);
        Clamp(ref d.rc, edge, thickness); // Windows a pu déplacer le bord intérieur : on rétablit l'épaisseur
        SHAppBarMessage(ABM_SETPOS, ref d);
        return Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
    }

    private static void Clamp(ref RECT r, DrawerEdge edge, int t)
    {
        switch (edge)
        {
            case DrawerEdge.Top: r.Bottom = r.Top + t; break;
            case DrawerEdge.Bottom: r.Top = r.Bottom - t; break;
            case DrawerEdge.Left: r.Right = r.Left + t; break;
            default: r.Left = r.Right - t; break;
        }
    }

    private static APPBARDATA New(nint hwnd) => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
}
