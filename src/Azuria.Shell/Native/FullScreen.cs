using System.Runtime.InteropServices;
using System.Text;

namespace Azuria.Shell.Native;

/// <summary>
/// Repère une appli en plein écran (un jeu) : la fenêtre au premier plan, qui n'est ni à Azuria ni
/// le bureau, et qui couvre tout son écran. Simple lecture de la position d'une fenêtre (aucune
/// injection, rien n'est lu dans le jeu). Sert à masquer tout Azuria par-dessus un jeu (CLAUDE.md).
/// </summary>
internal static class FullScreen
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder s, int max);

    private static readonly HashSet<string> ShellClasses = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>Nom de l'écran (DeviceName) occupé par une appli plein écran, ou null.</summary>
    public static string? OccupiedScreen()
    {
        nint fg = GetForegroundWindow();
        if (fg == 0) return null;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId) return null;
        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (ShellClasses.Contains(cls.ToString())) return null;
        if (!GetWindowRect(fg, out var r)) return null;

        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        var screen = Screen.FromRectangle(rect);
        return rect.Contains(screen.Bounds) ? screen.DeviceName : null;
    }
}
