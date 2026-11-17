using System.Runtime.InteropServices;
using System.Text;

namespace Azuria.Shell.Native;

/// <summary>
/// Pavage des fenêtres (idée du « dwindle » de Hyprland, réécrite ici). Sur chaque écran, les
/// fenêtres d'applis remplissent la zone de travail (intérieur du cadre), avec une marge contre le
/// cadre et un espace entre elles :
/// - une seule fenêtre : toute la zone ;
/// - une deuxième : la zone se partage en deux, côte à côte ;
/// - chaque nouvelle coupe en deux la place de la précédente, dans le sens le plus long (spirale).
/// Ordre d'arrivée conservé. Le glissé ne sert qu'à réordonner un écran : une fenêtre lâchée sur une
/// autre du même écran échange sa place avec elle ; lâchée ailleurs (vide, autre écran), elle revient
/// à sa place. Seule sur son écran, elle ne se glisse pas du tout. Un redimensionnement à la main est
/// annulé. L'aimantation de Windows (Snap, et sa grille en haut de l'écran) est coupée tant que le
/// shell tourne : elle contredirait le pavage.
///
/// Seules les fenêtres « normales » sont pavées : redimensionnables, avec un bouton agrandir, sans
/// propriétaire (pas de boîte de dialogue), pas toujours au-dessus, pas réduites. Une fenêtre agrandie
/// à la main est laissée telle quelle ; une fenêtre qui s'ouvre agrandie rejoint le pavage.
///
/// Garde-fous Vanguard (CLAUDE.md) : on ne fait que lire la position des fenêtres et les déplacer
/// avec les API documentées du gestionnaire de fenêtres. Les notifications viennent de
/// <c>SetWinEventHook</c> en mode « hors contexte » : rien n'est chargé dans les autres processus.
/// **Aucun jeu n'est touché** : ni les fenêtres plein écran, ni celles des dossiers de Riot Games,
/// Steam, Epic Games ou Vanguard ; et tout s'arrête tant qu'un jeu est en plein écran.
/// </summary>
internal sealed class Tiler : IDisposable
{
    // Espacements (px logiques) : contre le cadre, et entre deux fenêtres.
    private const int OuterGap = 10;
    private const int InnerGap = 10;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const uint EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const int WS_THICKFRAME = 0x00040000, WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_EX_TOPMOST = 0x00000008, WS_EX_DLGMODALFRAME = 0x00000001;
    private const uint GW_OWNER = 4;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200, SWP_ASYNCWINDOWPOS = 0x4000;
    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint h);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, uint attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint h, uint attr, ref uint value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(nint h, bool altTab);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint h, StringBuilder s, int max);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint p, int flags, StringBuilder s, ref int size);
    private delegate bool EnumProc(nint h, nint lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfo")] private static extern bool SystemParametersSet(uint action, uint param, nint value, uint winIni);

    private const uint WM_CANCELMODE = 0x001F;
    private const uint SPI_GETWINARRANGING = 0x0082, SPI_SETWINARRANGING = 0x0083, SPIF_SENDCHANGE = 0x02;

    // Dossiers dont les fenêtres ne sont jamais touchées (jeux, launchers de jeux, anti-triche).
    private static readonly string[] GameFolders =
        { @"\riot games\", @"\riot vanguard\", @"\steamapps\", @"\epic games\" };

    private readonly WinEventProc _proc; // gardé vivant tant que les crochets sont posés
    private readonly List<nint> _hooks = new();
    private readonly List<nint> _order = new();                  // fenêtres pavées, ordre d'arrivée
    private readonly Dictionary<nint, string> _screenOf = new(); // écran de chaque fenêtre
    private readonly Dictionary<nint, Rectangle> _tiles = new(); // dernière place donnée
    private readonly HashSet<nint> _candidates = new();          // apparues, à examiner
    private readonly HashSet<nint> _fresh = new();               // nouvelles : on les sort de l'agrandi
    private readonly Dictionary<uint, bool> _gamePid = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 80 };
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 600 };
    private readonly System.Windows.Forms.Timer _hover = new() { Interval = 50 };
    private nint _hovered;   // fenêtre sous le curseur au tour précédent
    private POINT _lastCursor;
    private bool _armed;     // la souris a bougé : le focus peut suivre
    private nint _dragged;
    private nint _focused;   // fenêtre qui porte le contour
    private int _generation; // pavage en cours d'animation
    private bool _disposed;
    private bool _suspended;
    private bool _snapWasOn;  // Snap de Windows actif avant nous : à remettre en partant

    public Tiler()
    {
        _proc = OnEvent;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reconcile(); };
        // Beaucoup d'applis se replacent elles-mêmes juste après s'être affichées : seconde passe.
        _settle.Tick += (_, _) => { _settle.Stop(); Apply(); _fresh.Clear(); };
        _hover.Tick += (_, _) => FollowMouse();
    }

    public void Start()
    {
        // Snap de Windows coupé pour la session seulement (sans SPIF_UPDATEINIFILE : rien n'est écrit
        // dans le profil, une fermeture de session le remet même si le shell s'arrête mal).
        // Pavage coupé dans les réglages : Snap reste à Windows.
        _snapWasOn = SystemParametersInfo(SPI_GETWINARRANGING, 0, out int arranging, 0) && arranging != 0;
        UpdateSnap();

        uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
        foreach (var (min, max) in new[]
        {
            (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
            (EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND),
            (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
            (EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE),
            (EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE),
            (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
        })
        {
            nint h = SetWinEventHook(min, max, 0, _proc, 0, 0, flags);
            if (h != 0) _hooks.Add(h);
        }
        // Les fenêtres déjà ouvertes rejoignent le pavage, comme si elles venaient d'arriver.
        EnumWindows((h, _) => { _candidates.Add(h); return true; }, 0);
        Reconcile();
        Focus(GetForegroundWindow()); // contour sur la fenêtre déjà active
        _hover.Start();               // le focus suit le curseur
        Settings.Changed += OnSettingsChanged;
    }

    /// <summary>Vrai pendant un jeu en plein écran : on ne touche à aucune fenêtre.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            if (!value) Schedule();
            else Focus(0); // pendant un jeu, plus de contour non plus
        }
    }

    /// <summary>La zone de travail ou les écrans ont changé : tout se recale.</summary>
    public void Relayout() => Schedule();

    private void Schedule() { _debounce.Stop(); _debounce.Start(); }

    private void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 /* OBJID_WINDOW */ || idChild != 0 || hwnd == 0) return;
        switch (ev)
        {
            case EVENT_OBJECT_SHOW:
            case EVENT_OBJECT_UNCLOAKED:
            case EVENT_SYSTEM_MINIMIZEEND:
                if (!_order.Contains(hwnd)) _candidates.Add(hwnd);
                break;
            case EVENT_OBJECT_NAMECHANGE:
                // Une fenêtre sans titre n'est pas encore une appli : elle peut le devenir.
                if (_order.Contains(hwnd)) return;
                _candidates.Add(hwnd);
                break;
            case EVENT_SYSTEM_FOREGROUND:
                Focus(hwnd); // contour tout de suite, sans attendre
                return;
            case EVENT_SYSTEM_MOVESIZESTART:
                // Seule sur son écran, une fenêtre pavée n'a rien à réordonner : le glissé est annulé
                // d'emblée (message standard, la boucle de déplacement de Windows s'arrête).
                if (!_suspended && Settings.Current.Tiling && _order.Contains(hwnd) && !Shared(hwnd))
                    PostMessage(hwnd, WM_CANCELMODE, 0, 0);
                return;
            case EVENT_SYSTEM_MOVESIZEEND:
                _dragged = hwnd;
                break;
        }
        Schedule();
    }

    // ----- état -----

    private void Reconcile()
    {
        if (_suspended || !Settings.Current.Tiling) return;

        // Fenêtres disparues.
        foreach (var h in _order.Where(h => !IsWindow(h)).ToList()) Forget(h);

        // Nouvelles fenêtres pavables.
        bool added = false;
        foreach (var h in _candidates)
        {
            if (_order.Contains(h) || !Tileable(h, allowZoomed: true)) continue;
            _order.Add(h);
            _screenOf[h] = Screen.FromHandle(h).DeviceName;
            _fresh.Add(h);
            added = true;
        }
        _candidates.Clear();

        if (_dragged != 0) { Dropped(_dragged); _dragged = 0; }

        Apply();
        if (added) { _settle.Stop(); _settle.Start(); }
    }

    private void Forget(nint h)
    {
        _order.Remove(h);
        _screenOf.Remove(h);
        _tiles.Remove(h);
        _fresh.Remove(h);
    }

    // Fenêtres pavées sur un écran (même règle que le placement).
    private List<nint> TiledOn(string screen) =>
        _order.Where(h => _screenOf.TryGetValue(h, out var s) && s == screen
                          && Tileable(h, allowZoomed: _fresh.Contains(h))).ToList();

    // Vrai si la fenêtre partage son écran avec au moins une autre fenêtre pavée.
    private bool Shared(nint h) => _screenOf.TryGetValue(h, out var s) && TiledOn(s).Any(o => o != h);

    // Fin d'un glissé : lâchée sur une autre fenêtre du même écran, elle échange sa place avec elle.
    // Partout ailleurs (vide, autre écran, aimant de Windows), le pavage la remet à sa place.
    private void Dropped(nint h)
    {
        int i = _order.IndexOf(h);
        if (i < 0 || !_screenOf.TryGetValue(h, out var screen)) return;
        GetCursorPos(out POINT p);
        var cursor = new Point(p.X, p.Y);
        var over = TiledOn(screen).FirstOrDefault(o => o != h && _tiles.TryGetValue(o, out var t) && t.Contains(cursor));
        if (over == 0) return;
        int j = _order.IndexOf(over);
        (_order[i], _order[j]) = (_order[j], _order[i]);
    }

    // ----- placement -----

    private void Apply()
    {
        if (_suspended || !Settings.Current.Tiling) return;
        var moves = new List<(nint H, Rectangle From, Rectangle To)>();
        foreach (var screen in Screen.AllScreens)
        {
            var windows = TiledOn(screen.DeviceName);
            if (windows.Count == 0) continue;
            int outer = Monitors.Scale(screen, OuterGap), inner = Monitors.Scale(screen, InnerGap);
            var area = Rectangle.Inflate(Monitors.WorkArea(screen), -outer, -outer);
            var rects = Dwindle(area, windows.Count, inner);
            for (int k = 0; k < windows.Count; k++)
            {
                var h = windows[k];
                if (IsZoomed(h)) ShowWindow(h, SW_RESTORE);
                _tiles[h] = rects[k];
                var from = Visible(h);
                if (from is Rectangle f && f != rects[k]) moves.Add((h, f, rects[k]));
            }
        }
        if (moves.Count > 0) Animate(moves);
    }

    /// <summary>
    /// Les fenêtres glissent vers leur nouvelle place (DA §11 : 0,22 s, décélération), toutes en même
    /// temps, cadencées par DWM. Un nouveau pavage en cours de route reprend depuis la position atteinte.
    /// </summary>
    private async void Animate(List<(nint H, Rectangle From, Rectangle To)> moves)
    {
        int gen = ++_generation;
        const double dur = 220;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < dur)
        {
            double e = Easing.In(sw.Elapsed.TotalMilliseconds / dur);
            foreach (var (h, f, t) in moves)
                Place(h, Rectangle.FromLTRB(Easing.Lerp(f.Left, t.Left, e), Easing.Lerp(f.Top, t.Top, e),
                                            Easing.Lerp(f.Right, t.Right, e), Easing.Lerp(f.Bottom, t.Bottom, e)));
            await Task.Run(Dwm.Flush);
            if (gen != _generation || _disposed) return; // remplacé par un pavage plus récent
        }
        foreach (var (h, _, t) in moves) Place(h, t);
    }

    // Rectangle visible de la fenêtre (sans la bordure invisible de Windows 11).
    private static Rectangle? Visible(nint h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var fr, Marshal.SizeOf<RECT>()) == 0)
            return Rectangle.FromLTRB(fr.Left, fr.Top, fr.Right, fr.Bottom);
        return GetWindowRect(h, out var wr) ? Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom) : null;
    }

    /// <summary>Chaque fenêtre prend la moitié de la place restante, coupée dans le sens le plus long.</summary>
    private static List<Rectangle> Dwindle(Rectangle area, int n, int gap)
    {
        var result = new List<Rectangle>(n);
        var r = area;
        for (int i = 0; i < n; i++)
        {
            if (i == n - 1) { result.Add(r); break; }
            if (r.Width >= r.Height)
            {
                int w = (r.Width - gap) / 2;
                result.Add(new Rectangle(r.X, r.Y, w, r.Height));
                r = new Rectangle(r.X + w + gap, r.Y, r.Width - w - gap, r.Height);
            }
            else
            {
                int hh = (r.Height - gap) / 2;
                result.Add(new Rectangle(r.X, r.Y, r.Width, hh));
                r = new Rectangle(r.X, r.Y + hh + gap, r.Width, r.Height - hh - gap);
            }
        }
        return result;
    }

    // Place la partie visible de la fenêtre sur le rectangle : Windows 11 entoure les fenêtres d'une
    // bordure de redimensionnement invisible, qu'on compense (DWMWA_EXTENDED_FRAME_BOUNDS).
    private static void Place(nint h, Rectangle r)
    {
        if (!GetWindowRect(h, out var wr)) return;
        int l = 0, t = 0, ri = 0, b = 0;
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var fr, Marshal.SizeOf<RECT>()) == 0)
        {
            l = fr.Left - wr.Left; t = fr.Top - wr.Top;
            ri = wr.Right - fr.Right; b = wr.Bottom - fr.Bottom;
        }
        int x = r.X - l, y = r.Y - t, w = r.Width + l + ri, hh = r.Height + t + b;
        if (wr.Left == x && wr.Top == y && wr.Right - wr.Left == w && wr.Bottom - wr.Top == hh) return;
        SetWindowPos(h, 0, x, y, w, hh, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS);
    }

    // ----- le focus suit le curseur -----

    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;
    private const uint GA_ROOT = 2;

    // Ce qui peut céder le focus au survol : une appli, ou le bureau. Pas un menu, l'Alt-Tab, le
    // menu Démarrer de Windows… (ce ne sont pas des fenêtres d'applis).
    private static readonly HashSet<string> DesktopClasses = new() { "Progman", "WorkerW" };

    /// <summary>
    /// La fenêtre d'appli sous le curseur prend le focus, après un court arrêt dessus (deux tours de
    /// 50 ms : la traverser ne l'active pas). Garde-fous : jamais pendant un jeu en plein écran, jamais
    /// en prenant le focus à un jeu (même fenêtré), jamais bouton de souris enfoncé, jamais quand le
    /// premier plan est un menu ou une fenêtre d'Azuria (le lanceur où l'on tape).
    /// </summary>
    private void FollowMouse()
    {
        if (_suspended || !Settings.Current.FocusFollowsMouse) { _hovered = 0; return; }
        if (GetAsyncKeyState(VK_LBUTTON) < 0 || GetAsyncKeyState(VK_RBUTTON) < 0 || GetAsyncKeyState(VK_MBUTTON) < 0)
        {
            _hovered = 0;
            return;
        }
        GetCursorPos(out POINT p);
        // Seul un mouvement de la souris déplace le focus : une appli qui s'ouvre, l'Alt-Tab ou un
        // raccourci clavier gardent le leur tant que la souris ne bouge pas (comme Hyprland).
        if (p.X != _lastCursor.X || p.Y != _lastCursor.Y) _armed = true;
        _lastCursor = p;
        if (!_armed) return;

        nint under = WindowFromPoint(p);
        if (under != 0) under = GetAncestor(under, GA_ROOT);
        bool again = under == _hovered;
        _hovered = under;
        if (!again || under == 0) return; // attendre que le curseur se pose
        _armed = false;

        nint fg = GetForegroundWindow();
        if (under == fg) return;
        if (!OpenWindows.IsAppWindow(under) || IsGame(under)) return;
        if (fg != 0 && !OpenWindows.IsAppWindow(fg) && !DesktopClasses.Contains(ClassOf(fg))) return;
        if (fg != 0 && IsGame(fg)) return;

        if (!SetForegroundWindow(under) || GetForegroundWindow() != under)
            SwitchToThisWindow(under, true); // Windows refuse parfois le premier plan à un processus en arrière-plan
    }

    private static string ClassOf(nint h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // ----- contour de la fenêtre active -----

    private const uint DWMWA_BORDER_COLOR = 34, DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    // Réglages changés : le contour prend la nouvelle teinte ; le pavage remis en marche reprend
    // toutes les fenêtres ouvertes (coupé, il laisse chacune là où elle est).
    private bool _tilingWas = Settings.Current.Tiling;

    // Snap de Windows (s'il était actif) : coupé pendant le pavage, rendu quand le pavage est coupé.
    private void UpdateSnap()
    {
        if (_snapWasOn) SystemParametersSet(SPI_SETWINARRANGING, Settings.Current.Tiling ? 0u : 1u, 0, SPIF_SENDCHANGE);
    }
    private void OnSettingsChanged()
    {
        if (_focused != 0 && IsWindow(_focused))
        {
            uint c = Settings.Current.AccentColorRef;
            DwmSetWindowAttribute(_focused, DWMWA_BORDER_COLOR, ref c, sizeof(uint));
        }
        bool on = Settings.Current.Tiling;
        if (on != _tilingWas) UpdateSnap();
        if (on && !_tilingWas)
        {
            EnumWindows((h, _) => { _candidates.Add(h); return true; }, 0);
            Schedule();
        }
        _tilingWas = on;
    }

    /// <summary>
    /// La fenêtre d'appli au premier plan prend un contour couleur d'accent ; la précédente retrouve
    /// celui de Windows. Couleur de bordure de DWM (Windows 11) : anti-aliasé, suit les coins de la
    /// fenêtre, rien n'est dessiné par-dessus. Jamais sur un jeu.
    /// </summary>
    private void Focus(nint h)
    {
        if (h != 0 && (_suspended || _disposed || !OpenWindows.IsAppWindow(h) || IsGame(h))) h = 0;
        if (h == _focused) return;
        if (_focused != 0 && IsWindow(_focused)) Border(_focused, DWMWA_COLOR_DEFAULT);
        _focused = h;
        if (h != 0) Border(h, Settings.Current.AccentColorRef);

        static void Border(nint w, uint color) => DwmSetWindowAttribute(w, DWMWA_BORDER_COLOR, ref color, sizeof(uint));
    }

    // ----- règles -----

    private bool Tileable(nint h, bool allowZoomed)
    {
        if (!IsWindow(h) || !OpenWindows.IsAppWindow(h) || IsIconic(h)) return false;
        if (IsZoomed(h) && !allowZoomed) return false;
        int style = GetWindowLong(h, GWL_STYLE), ex = GetWindowLong(h, GWL_EXSTYLE);
        if ((style & WS_THICKFRAME) == 0 || (style & WS_MAXIMIZEBOX) == 0) return false;
        if ((ex & (WS_EX_TOPMOST | WS_EX_DLGMODALFRAME)) != 0) return false;
        if (GetWindow(h, GW_OWNER) != 0) return false;
        if (!GetWindowRect(h, out var wr)) return false;
        var rect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        if (rect.Contains(Screen.FromHandle(h).Bounds)) return false; // plein écran : un jeu, une vidéo
        return !IsGame(h);
    }

    private bool IsGame(nint h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        if (_gamePid.TryGetValue(pid, out bool game)) return game;
        game = true; // chemin illisible (processus protégé, élevé…) : dans le doute, on n'y touche pas
        nint p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (p != 0)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(p, 0, sb, ref size))
                {
                    string path = sb.ToString().ToLowerInvariant();
                    game = GameFolders.Any(path.Contains);
                }
            }
            finally { CloseHandle(p); }
        }
        if (_gamePid.Count > 512) _gamePid.Clear();
        _gamePid[pid] = game;
        return game;
    }

    public void Dispose()
    {
        _disposed = true;
        Settings.Changed -= OnSettingsChanged;
        Focus(0);
        if (_snapWasOn) SystemParametersSet(SPI_SETWINARRANGING, 1, 0, SPIF_SENDCHANGE);
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
        _debounce.Dispose();
        _settle.Dispose();
        _hover.Dispose();
    }
}
