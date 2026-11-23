using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>Une icône de la zone de notification (tray), telle que le popout l'affiche.</summary>
internal sealed record TrayIcon(string Key, string Tip, string Png);

/// <summary>
/// La zone de notification, sans la barre des tâches de Windows (masquée par <see cref="Taskbar"/>).
/// Les applis posent leurs icônes avec <c>Shell_NotifyIcon</c>, qui envoie un <c>WM_COPYDATA</c> à la
/// première fenêtre de classe « Shell_TrayWnd ». On crée la nôtre, invisible et au-dessus de celle de
/// l'Explorateur : on lit chaque message, puis on le transmet tel quel à l'Explorateur, qui garde ses
/// icônes (rien ne se perd si Azuria s'arrête). C'est la méthode des barres des tâches alternatives :
/// aucune injection, aucun accès à la mémoire d'un autre processus. Un clic sur une icône est rendu à
/// l'appli par son message de rappel, comme l'Explorateur le fait.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private delegate nint WndProc(nint hwnd, uint msg, nint w, nint l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public nint hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] private struct COPYDATASTRUCT { public nuint dwData; public int cbData; public nint lpData; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string cls, nint inst);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint h, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    private const string TrayClass = "Shell_TrayWnd";
    private const uint WM_COPYDATA = 0x004A, WM_COMMAND = 0x0111, WM_USER = 0x0400;
    private const uint SMTO_ABORTIFHUNG = 0x2;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private static readonly nint HWND_TOPMOST = -1, HWND_NOTOPMOST = -2, HWND_BOTTOM = 1, HWND_BROADCAST = 0xFFFF;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private const uint WS_POPUP = 0x80000000;

    // Shell_NotifyIcon : messages, champs et états.
    private const int TraySignature = 0x34753423;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_GUID = 0x20;
    private const uint NIS_HIDDEN = 0x1;
    private const uint WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203;
    private const uint WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400;

    private sealed class Entry
    {
        public nint Owner; public uint Id; public Guid Guid; public uint Callback; public uint Version;
        public string Tip = ""; public string Png = ""; public bool Hidden; public uint Pid;
        public string Key => Guid != Guid.Empty ? Guid.ToString("N") : $"{(long)Owner:x}-{Id}";
    }

    private readonly List<Entry> _icons = new();      // dans l'ordre d'arrivée
    private readonly WndProc _proc;                    // gardé en vie : Windows l'appelle
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly System.Windows.Forms.Timer _keepFirst = new() { Interval = 1000 };
    private nint _hwnd;

    /// <summary>Les icônes ont changé (ajout, retrait, nouvelle image ou info-bulle).</summary>
    public event Action? Changed;

    public TrayHost()
    {
        _proc = Proc;
        var c = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = GetModuleHandle(null), lpszClassName = TrayClass,
        };
        if (RegisterClassEx(ref c) == 0) return;
        // Même place que la barre de l'Explorateur : certaines applis lisent son rectangle pour placer
        // leurs bulles. La fenêtre n'est jamais affichée.
        var r = GetWindowRect(ExplorerTray(), out var er) ? er : new RECT();
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, TrayClass, "", WS_POPUP,
                               r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0, 0, c.hInstance, 0);
        if (_hwnd == 0) return;
        _current = _hwnd;
        _self = this;
        BeFirst();
        // Les applis renvoient leurs icônes à la première fenêtre « Shell_TrayWnd » trouvée. Au
        // démarrage, les barres d'Azuria réservent leurs bords (StepAside : notre fenêtre passe un
        // instant derrière celle de l'Explorateur) : l'annonce attend qu'elles aient fini.
        AnnounceSoon();
        _announce.Tick += (_, _) =>
        {
            _announce.Stop();
            if (FirstTray() != _hwnd) BeFirst();
            Broadcast();
            _announcedAt = Environment.TickCount64;
        };
        // L'Explorateur repasse parfois sa fenêtre devant la nôtre (il est relancé, ou la recrée) :
        // on reprend la place, et les applis renvoient leurs icônes.
        _keepFirst.Tick += (_, _) =>
        {
            if (FirstTray() == _hwnd) return;
            BeFirst();
            Broadcast();
        };
        _keepFirst.Start();
        // Fenêtres des applis disparues (appli plantée) : leurs icônes partent aussi.
        _sweep.Tick += (_, _) => { if (_icons.RemoveAll(e => !IsWindow(e.Owner)) > 0) Changed?.Invoke(); };
        _sweep.Start();
    }

    private readonly System.Windows.Forms.Timer _sweep = new() { Interval = 5000 };

    /// <summary>Les icônes visibles, dans l'ordre d'arrivée.</summary>
    public List<TrayIcon> Icons() =>
        _icons.Where(e => !e.Hidden && e.Callback != 0).Select(e => new TrayIcon(e.Key, e.Tip, e.Png)).ToList();

    /// <summary>
    /// Rend un clic à l'appli, comme l'Explorateur : <paramref name="button"/> vaut « left »,
    /// « double » ou « right ». L'appli reçoit le droit de passer au premier plan (son menu, sa fenêtre).
    /// </summary>
    public void Click(string key, string button)
    {
        var e = _icons.FirstOrDefault(i => i.Key == key);
        if (e is null || !IsWindow(e.Owner) || e.Callback == 0) return;
        AllowSetForegroundWindow(e.Pid);
        var p = Cursor.Position;
        _clicked = (e, p, Environment.TickCount64); // l'appli peut demander où est son icône (IconRect)
        void Send(uint m)
        {
            // Version 4 : le message dans le mot bas de lParam, l'identifiant dans le haut, et la
            // position du clic dans wParam. Avant : l'identifiant dans wParam, le message dans lParam.
            if (e.Version >= 4) PostMessage(e.Owner, e.Callback, (nint)((p.X & 0xFFFF) | ((p.Y & 0xFFFF) << 16)), (nint)((m & 0xFFFF) | ((e.Id & 0xFFFF) << 16)));
            else PostMessage(e.Owner, e.Callback, (nint)e.Id, (nint)m);
        }
        switch (button)
        {
            case "left": Send(WM_LBUTTONDOWN); Send(WM_LBUTTONUP); if (e.Version >= 4) Send(NIN_SELECT); break;
            case "double": Send(WM_LBUTTONDBLCLK); Send(WM_LBUTTONUP); break;
            case "right": Send(WM_RBUTTONDOWN); Send(WM_RBUTTONUP); if (e.Version >= 4) Send(WM_CONTEXTMENU); break;
        }
    }

    /// <summary>La fenêtre « Shell_TrayWnd » de l'Explorateur (jamais la nôtre).</summary>
    public static nint ExplorerTray()
    {
        nint h = 0;
        while ((h = FindWindowEx(0, h, TrayClass, null)) != 0)
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid != Environment.ProcessId) return h;
        }
        return 0;
    }

    private static nint FirstTray() => FindWindow(TrayClass, null);

    private void BeFirst() => SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private static nint _current; // notre fenêtre « Shell_TrayWnd », s'il y en a une
    private static TrayHost? _self;

    // Annonce « TaskbarCreated » différée : 1,5 s après le dernier écart (StepAside), et pas plus
    // d'une fois toutes les 10 s, pour ne pas faire renvoyer leurs icônes aux applis en boucle.
    private readonly System.Windows.Forms.Timer _announce = new() { Interval = 1500 };
    private long _announcedAt = -10_000;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private void AnnounceSoon()
    {
        int wait = (int)Math.Max(1500, 10_000 - (Environment.TickCount64 - _announcedAt));
        _announce.Stop();
        _announce.Interval = wait;
        _announce.Start();
    }

    /// <summary>
    /// Les barres d'application d'Azuria passent par <c>SHAppBarMessage</c>, qui écrit à la première
    /// fenêtre « Shell_TrayWnd » : la nôtre. Relayés depuis notre propre processus, ces messages sont
    /// acceptés mais Windows ne déduit jamais les bandes de la zone de travail (constaté sur deux
    /// écrans : aucun bord réservé). Le temps de l'appel, notre fenêtre passe donc derrière celle de
    /// l'Explorateur, qui reçoit le message directement ; elle reprend sa place juste après.
    /// </summary>
    public static IDisposable StepAside()
    {
        nint h = _current;
        if (h == 0 || !IsWindow(h)) return Restorer.None;
        SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        return new Restorer(() =>
        {
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            // Une icône posée pendant l'écart est partie chez l'Explorateur : on redemande à tous.
            if (_self is { } t)
            {
                if (t._ui is { } ui) ui.Post(_ => { if (t._hwnd != 0) t.AnnounceSoon(); }, null);
                else t.AnnounceSoon();
            }
        });
    }

    private sealed class Restorer(Action? done) : IDisposable
    {
        public static readonly Restorer None = new(null);
        public void Dispose() => done?.Invoke();
    }

    // « La barre des tâches vient d'être créée » : chaque appli renvoie ses icônes (vers nous).
    private void Broadcast() => PostMessage(HWND_BROADCAST, _taskbarCreated, 0, 0);

    private nint Proc(nint hwnd, uint msg, nint w, nint l)
    {
        try
        {
            if (msg == WM_COPYDATA)
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(l);
                if (cds.dwData == 3 && IconRect(cds) is nint at) return at;
                bool ok = cds.dwData == 1 && Read(cds);
                nint res = Forward(msg, w, l);
                return cds.dwData == 1 ? (ok ? 1 : res) : res;
            }
            // Messages adressés à la barre par des applis (« réduire tout », requêtes du shell…) :
            // l'Explorateur y répond, comme si nous n'étions pas là.
            if (msg == WM_COMMAND || (msg >= WM_USER && msg < 0x8000)) return Forward(msg, w, l);
        }
        catch { /* message illisible : on laisse l'Explorateur répondre */ }
        return DefWindowProc(hwnd, msg, w, l);
    }

    // Shell_NotifyIconGetRect : l'appli demande où est son icône, pour y ouvrir son menu ou sa fenêtre.
    // L'Explorateur répondrait avec sa barre cachée (en bas à droite) : on répond avec l'endroit où
    // l'icône vient d'être cliquée dans le popout. Pas de clic récent sur cette icône : l'Explorateur répond.
    // Données : 0 signature · 4 demande (1 = coin haut-gauche, 2 = coin bas-droit) · 8 cbSize ·
    // 12 réservé · 16 hWnd (32 bits) · 20 uID · 24 guidItem. Réponse : MAKELPARAM(x, y).
    private nint? IconRect(COPYDATASTRUCT cds)
    {
        if (cds.cbData < 40 || Marshal.ReadInt32(cds.lpData) != TraySignature) return null;
        if (_clicked is not { } c || Environment.TickCount64 - c.At > 10_000) return null;
        int ask = Marshal.ReadInt32(cds.lpData, 4);
        nint owner = (nint)Marshal.ReadInt32(cds.lpData, 16);
        uint id = (uint)Marshal.ReadInt32(cds.lpData, 20);
        var b = new byte[16];
        Marshal.Copy(cds.lpData + 24, b, 0, 16);
        var guid = new Guid(b);
        bool same = guid != Guid.Empty ? c.Entry.Guid == guid : c.Entry.Owner == owner && c.Entry.Id == id;
        if (!same) return null;
        int half = Monitors.Scale(Screen.FromPoint(c.Point), 12); // une icône de 24 px logiques
        var r = new Rectangle(c.Point.X - half, c.Point.Y - half, 2 * half, 2 * half);
        return ask switch
        {
            1 => MakeLParam(r.Left, r.Top),
            2 => MakeLParam(r.Right, r.Bottom),
            _ => null,
        };
    }

    private static nint MakeLParam(int lo, int hi) => (nint)(int)((lo & 0xFFFF) | ((hi & 0xFFFF) << 16));

    private (Entry Entry, Point Point, long At)? _clicked; // dernier clic rendu à une appli

    private static nint Forward(uint msg, nint w, nint l)
    {
        nint tray = ExplorerTray();
        if (tray == 0) return 0;
        SendMessageTimeout(tray, msg, w, l, SMTO_ABORTIFHUNG, 2000, out nint result);
        return result;
    }

    // Lit un message de Shell_NotifyIcon. La structure transmise garde des poignées sur 32 bits,
    // quelle que soit l'appli (NOTIFYICONDATA « 32 ») :
    //   0 signature · 4 message · 8 NOTIFYICONDATA : +0 cbSize, +4 hWnd, +8 uID, +12 uFlags,
    //   +16 uCallbackMessage, +20 hIcon, +24 szTip[128], +280 dwState, +284 dwStateMask,
    //   +288 szInfo[256], +800 uVersion, +804 szInfoTitle[64], +932 dwInfoFlags, +936 guidItem.
    private bool Read(COPYDATASTRUCT cds)
    {
        if (cds.cbData < 8 + 24 || Marshal.ReadInt32(cds.lpData) != TraySignature) return false;
        int message = Marshal.ReadInt32(cds.lpData, 4);
        nint nid = cds.lpData + 8;
        int size = cds.cbData - 8;
        nint owner = (nint)Marshal.ReadInt32(nid, 4);
        uint id = (uint)Marshal.ReadInt32(nid, 8);
        uint flags = (uint)Marshal.ReadInt32(nid, 12);
        Guid guid = Guid.Empty;
        if ((flags & NIF_GUID) != 0 && size >= 952)
        {
            var b = new byte[16];
            Marshal.Copy(nid + 936, b, 0, 16);
            guid = new Guid(b);
        }
        Entry? Find() => _icons.FirstOrDefault(e => guid != Guid.Empty ? e.Guid == guid : e.Owner == owner && e.Id == id);

        switch (message)
        {
            case NIM_ADD:
            case NIM_MODIFY:
            {
                var e = Find();
                if (e is null)
                {
                    // Un NIM_MODIFY inconnu vient d'une icône posée avant Azuria : on la prend aussi.
                    e = new Entry { Owner = owner, Id = id, Guid = guid };
                    GetWindowThreadProcessId(owner, out e.Pid);
                    _icons.Add(e);
                }
                // Déjà connue (renvoyée après notre « TaskbarCreated ») : simple mise à jour, et succès,
                // pour que l'appli ne la renvoie pas en boucle.
                e.Owner = owner;
                e.Id = id;
                if ((flags & NIF_MESSAGE) != 0) e.Callback = (uint)Marshal.ReadInt32(nid, 16);
                if ((flags & NIF_ICON) != 0) e.Png = Png((nint)Marshal.ReadInt32(nid, 20));
                if ((flags & NIF_TIP) != 0 && size >= 280) e.Tip = (Marshal.PtrToStringUni(nid + 24, 128) ?? "").Split('\0')[0];
                if ((flags & NIF_STATE) != 0 && size >= 288)
                {
                    uint state = (uint)Marshal.ReadInt32(nid, 280), mask = (uint)Marshal.ReadInt32(nid, 284);
                    if ((mask & NIS_HIDDEN) != 0) e.Hidden = (state & NIS_HIDDEN) != 0;
                }
                Changed?.Invoke();
                return true;
            }
            case NIM_DELETE:
            {
                var e = Find();
                if (e is null) return false;
                _icons.Remove(e);
                Changed?.Invoke();
                return true;
            }
            case NIM_SETVERSION:
            {
                var e = Find();
                if (e is null || size < 804) return false;
                e.Version = (uint)Marshal.ReadInt32(nid, 800);
                return true;
            }
            default: return false; // NIM_SETFOCUS : l'Explorateur s'en charge
        }
    }

    // Les icônes sont des objets du système, lisibles depuis n'importe quel processus : on en fait
    // une image tout de suite (l'appli peut détruire l'icône dès le message suivant).
    private static string Png(nint icon)
    {
        if (icon == 0) return "";
        try
        {
            using var bmp = Icon.FromHandle(icon).ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch { return ""; }
    }

    public void Dispose()
    {
        _keepFirst.Dispose();
        _sweep.Dispose();
        _announce.Dispose();
        _self = null;
        // Sans notre fenêtre, les applis retrouvent celle de l'Explorateur, qui a déjà toutes leurs
        // icônes (tout lui a été transmis).
        if (_hwnd != 0) DestroyWindow(_hwnd);
        _hwnd = 0;
        _current = 0;
        UnregisterClass(TrayClass, GetModuleHandle(null));
    }
}
