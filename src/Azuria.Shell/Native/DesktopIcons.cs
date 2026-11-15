using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Retire les icônes du bureau tant qu'Azuria tourne (DA §7 : le bureau n'a pas d'icônes, seulement
/// le fond). Même geste que pour la barre des tâches : la fenêtre des icônes de l'Explorateur
/// (<c>SysListView32</c> « FolderView », dans <c>SHELLDLL_DefView</c>) est cachée par
/// <c>ShowWindow</c>, sans toucher à son code ni au réglage « Afficher les icônes du bureau » de
/// l'utilisateur. Aucune injection. Tout est rétabli à la sortie ; si Azuria est tué, un redémarrage
/// de l'Explorateur suffit à les faire revenir.
/// </summary>
internal sealed class DesktopIcons : IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);

    private const int SW_HIDE = 0, SW_SHOWNA = 8;

    private readonly System.Windows.Forms.Timer _keepHidden = new() { Interval = 1000 };
    private readonly HashSet<nint> _hidden = new();
    private bool _restored;

    public DesktopIcons()
    {
        Hide();
        // L'Explorateur recrée la fenêtre des icônes (redémarrage, changement de fond) : on la recache.
        _keepHidden.Tick += (_, _) => Hide();
        _keepHidden.Start();
    }

    private void Hide()
    {
        foreach (var h in Views())
        {
            if (!IsWindowVisible(h)) continue;
            ShowWindow(h, SW_HIDE);
            _hidden.Add(h);
        }
    }

    /// <summary>Rend les icônes cachées par Azuria (sûr depuis n'importe quel thread).</summary>
    public void Restore()
    {
        if (_restored) return;
        _restored = true;
        foreach (var h in Views()) if (_hidden.Contains(h)) ShowWindow(h, SW_SHOWNA);
    }

    // La vue des icônes vit sous Progman, ou sous une fenêtre WorkerW selon la version de Windows.
    private static IEnumerable<nint> Views()
    {
        var hosts = new List<nint>();
        nint progman = FindWindow("Progman", null);
        if (progman != 0) hosts.Add(progman);
        nint w = 0;
        while ((w = FindWindowEx(0, w, "WorkerW", null)) != 0) hosts.Add(w);
        foreach (var host in hosts)
        {
            nint def = FindWindowEx(host, 0, "SHELLDLL_DefView", null);
            if (def == 0) continue;
            nint list = FindWindowEx(def, 0, "SysListView32", null);
            if (list != 0) yield return list;
        }
    }

    public void Dispose()
    {
        _keepHidden.Dispose();
        Restore();
    }
}
