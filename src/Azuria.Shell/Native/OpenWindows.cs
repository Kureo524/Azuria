using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Azuria.Shell.Native;

/// <summary>Une appli ouverte, telle que la barre gauche l'affiche.</summary>
internal sealed record OpenWindow(nint Handle, string Title, string Icon, bool Active);

/// <summary>
/// Les fenêtres d'applis ouvertes, avec les mêmes règles que la barre des tâches de Windows, et les
/// gestes de la barre : activer, réduire. API documentées du gestionnaire de fenêtres uniquement :
/// on lit les fenêtres de premier niveau et on les active, sans rien injecter dans aucun processus
/// (CLAUDE.md). Les changements arrivent par le « shell hook » de Windows (fenêtre créée, détruite,
/// activée, renommée), le même mécanisme que les barres des tâches.
/// </summary>
internal sealed class OpenWindows : IDisposable
{
    private delegate bool EnumProc(nint hwnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(nint hwnd, bool altTab);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool RegisterShellHookWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool DeregisterShellHookWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint p, int flags, StringBuilder s, ref int size);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint GW_OWNER = 4, WM_GETICON = 0x7F, SMTO_ABORTIFHUNG = 0x2;
    private const int SW_MINIMIZE = 6, SW_RESTORE = 9;
    private const uint DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private readonly HookWindow _hook;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 120 };
    private readonly Dictionary<(nint, nint), string> _icons = new(); // (fenêtre, icône) → PNG en base64
    private readonly int _ownPid = Environment.ProcessId;

    /// <summary>La liste a (peut-être) changé.</summary>
    public event Action? Changed;

    public OpenWindows()
    {
        _hook = new HookWindow(RegisterWindowMessage("SHELLHOOK"), () => { _debounce.Stop(); _debounce.Start(); });
        RegisterShellHookWindow(_hook.Handle);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Changed?.Invoke(); };
    }

    /// <summary>Les applis ouvertes, dans l'ordre de Windows (la plus récente en premier).</summary>
    public List<OpenWindow> List()
    {
        var result = new List<OpenWindow>();
        var seen = new HashSet<(nint, nint)>();
        nint fg = GetForegroundWindow();
        EnumWindows((h, _) =>
        {
            if (IsAppWindow(h))
            {
                var icon = IconOf(h, out var key);
                seen.Add(key);
                result.Add(new OpenWindow(h, Title(h), icon, h == fg));
            }
            return true;
        }, 0);
        // Oublie les icônes des fenêtres disparues.
        foreach (var k in _icons.Keys.Where(k => !seen.Contains(k)).ToList()) _icons.Remove(k);
        // Ordre stable (par poignée) : la barre ne se réordonne pas à chaque changement de focus.
        result.Sort((a, b) => a.Handle.CompareTo(b.Handle));
        return result;
    }

    /// <summary>Clic sur une appli : l'active, ou la réduit si elle est déjà au premier plan.</summary>
    public void Activate(nint h)
    {
        if (!IsAppWindow(h)) return;
        if (h == GetForegroundWindow() && !IsIconic(h))
        {
            ShowWindow(h, SW_MINIMIZE);
            return;
        }
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        if (!SetForegroundWindow(h) || GetForegroundWindow() != h)
            SwitchToThisWindow(h, true); // Windows refuse parfois le premier plan à une barre qui n'a pas le focus
    }

    // Mêmes règles que la barre des tâches : visible, pas masquée (« cloaked »), sans propriétaire
    // ou marquée « appli », pas une palette d'outils ; jamais les fenêtres d'Azuria.
    private bool IsAppWindow(nint h)
    {
        if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return false;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow && ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0)) return false;
        if (!appWindow && GetWindow(h, GW_OWNER) != 0) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        GetWindowThreadProcessId(h, out uint pid);
        return pid != _ownPid;
    }

    private static string Title(nint h)
    {
        var sb = new StringBuilder(GetWindowTextLength(h) + 1);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // Icône de la fenêtre (grande d'abord), sinon celle de sa classe, sinon celle de l'exécutable.
    private string IconOf(nint h, out (nint, nint) key)
    {
        nint icon = 0;
        foreach (nint kind in new nint[] { 1 /* ICON_BIG */, 2 /* ICON_SMALL2 */, 0 /* ICON_SMALL */ })
        {
            if (SendMessageTimeout(h, WM_GETICON, kind, 0, SMTO_ABORTIFHUNG, 50, out icon) != 0 && icon != 0) break;
            icon = 0;
        }
        if (icon == 0) icon = GetClassLongPtr(h, -14 /* GCLP_HICON */);
        if (icon == 0) icon = GetClassLongPtr(h, -34 /* GCLP_HICONSM */);
        key = (h, icon);
        if (_icons.TryGetValue(key, out var cached)) return cached;

        string png = "";
        try
        {
            using var bmp = icon != 0 ? Icon.FromHandle(icon).ToBitmap() : ExeIcon(h);
            if (bmp is not null)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                png = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch { /* icône illisible : la page affiche l'initiale */ }
        _icons[key] = png;
        return png;
    }

    private static Bitmap? ExeIcon(nint h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        nint p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (p == 0) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (!QueryFullProcessImageName(p, 0, sb, ref size)) return null;
            return Icon.ExtractAssociatedIcon(sb.ToString())?.ToBitmap();
        }
        finally { CloseHandle(p); }
    }

    public void Dispose()
    {
        _debounce.Dispose();
        DeregisterShellHookWindow(_hook.Handle);
        _hook.DestroyHandle();
    }

    /// <summary>Fenêtre invisible qui reçoit les messages du shell hook.</summary>
    private sealed class HookWindow : NativeWindow
    {
        private readonly uint _msg;
        private readonly Action _onEvent;

        public HookWindow(uint msg, Action onEvent)
        {
            _msg = msg;
            _onEvent = onEvent;
            CreateHandle(new CreateParams { ExStyle = 0x80 /* WS_EX_TOOLWINDOW */ });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)_msg) _onEvent();
            base.WndProc(ref m);
        }
    }
}
