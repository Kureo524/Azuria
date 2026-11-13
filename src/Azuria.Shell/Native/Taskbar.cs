using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Retire la barre des tâches de Windows tant qu'Azuria tourne (les applis ouvertes sont dans la
/// barre gauche). Deux gestes, validés par le propriétaire : le masquage automatique (réglage
/// officiel, <c>ABM_SETSTATE</c>) pour que Windows rende la place, et la fenêtre de la barre cachée
/// (<c>ShowWindow</c>) pour qu'elle ne ressorte pas au bas de l'écran. Aucune injection : on ne fait
/// que masquer une fenêtre de l'explorateur, sans toucher à son code. Tout est rétabli à la sortie.
/// </summary>
internal sealed class Taskbar : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize; public nint hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public nint lParam;
    }

    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;
    private const int SW_HIDE = 0, SW_SHOWNA = 8;

    private readonly int _originalState;
    private readonly ExplorerWatcher _watcher;
    private readonly System.Windows.Forms.Timer _keepHidden = new() { Interval = 1000 };
    private bool _restored;

    public Taskbar()
    {
        // L'état d'origine est gardé sur disque jusqu'à ce qu'il soit rendu : si Azuria a été tué sans
        // pouvoir le rendre, la session suivante le connaît encore (et non le masquage posé par nous).
        if (!(File.Exists(StateFile) && int.TryParse(File.ReadAllText(StateFile), out _originalState)))
        {
            var d = New();
            _originalState = (int)SHAppBarMessage(ABM_GETSTATE, ref d);
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, _originalState.ToString());
        }
        Hide();
        // Explorateur relancé (la barre est recréée) : on la recache.
        _watcher = new ExplorerWatcher(RegisterWindowMessage("TaskbarCreated"), Hide);
        // Windows réaffiche parfois la barre (menu Démarrer, notifications) : on la recache.
        _keepHidden.Tick += (_, _) => { foreach (var h in Bars()) if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE); };
        _keepHidden.Start();
    }

    private void Hide()
    {
        SetState(_originalState | ABS_AUTOHIDE);
        foreach (var h in Bars()) ShowWindow(h, SW_HIDE);
    }

    /// <summary>Rend la barre des tâches telle qu'elle était (sûr depuis n'importe quel thread).</summary>
    public void Restore()
    {
        if (_restored) return;
        _restored = true;
        foreach (var h in Bars()) ShowWindow(h, SW_SHOWNA);
        SetState(_originalState);
        try { File.Delete(StateFile); } catch { /* rien de grave : relu à la prochaine session */ }
    }

    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Azuria", "barre-des-taches.txt");

    private static void SetState(int state)
    {
        var d = New();
        d.hWnd = FindWindow("Shell_TrayWnd", null);
        d.lParam = state;
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    // Barre principale et barres des écrans secondaires.
    private static IEnumerable<nint> Bars()
    {
        nint main = FindWindow("Shell_TrayWnd", null);
        if (main != 0) yield return main;
        nint h = 0;
        while ((h = FindWindowEx(0, h, "Shell_SecondaryTrayWnd", null)) != 0) yield return h;
    }

    private static APPBARDATA New() => new() { cbSize = Marshal.SizeOf<APPBARDATA>() };

    public void Dispose()
    {
        _keepHidden.Dispose();
        _watcher.DestroyHandle();
        Restore();
    }

    /// <summary>Fenêtre de premier niveau invisible, qui reçoit le message diffusé « TaskbarCreated ».</summary>
    private sealed class ExplorerWatcher : NativeWindow
    {
        private readonly uint _msg;
        private readonly Action _onCreated;

        public ExplorerWatcher(uint msg, Action onCreated)
        {
            _msg = msg;
            _onCreated = onCreated;
            CreateHandle(new CreateParams { ExStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)_msg) _onCreated();
            base.WndProc(ref m);
        }
    }
}
