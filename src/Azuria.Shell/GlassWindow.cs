using System.Diagnostics;
using System.Runtime.InteropServices;
using Azuria.Shell.Native;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>Bord d'écran auquel un tiroir est collé (None = tiroir flottant).</summary>
internal enum DrawerEdge { None, Top, Bottom, Left, Right }

/// <summary>Côtés d'un tiroir collés au cadre.</summary>
[Flags]
internal enum DrawerSides { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>Mesures communes du shell (px logiques, DA §8–9).</summary>
internal static class ShellMetrics
{
    /// <summary>Épaisseur du cadre permanent autour de la zone de travail.</summary>
    public const int Frame = 8;
    /// <summary>Rayon des coins intérieurs du cadre et des congés où un tiroir le rejoint.</summary>
    public const int Fillet = 18;
    /// <summary>Largeur du fondu du voile, du cadre (plus couvrant) vers le tiroir, côté collé.</summary>
    public const int Fade = 16;
}

/// <summary>
/// Fenêtre de verre d'Azuria : hôte natif sans cadre, toujours au-dessus, hors barre des tâches, qui
/// affiche une page de l'UI (HTML/CSS) dans un WebView2 transparent. Son verre (flou + voile à la
/// forme exacte du tiroir) est dessiné dessous par la surface de l'écran (<see cref="ScreenSurface"/>),
/// d'un seul tenant avec le cadre ; la fenêtre ne garde que sa page et ses clics.
///
/// Les panneaux sont des « tiroirs » qui sortent du cadre de l'écran (organisation inspirée de
/// Caelestia, entièrement réécrite ici) : collés au bord intérieur du cadre, avec des congés concaves
/// là où ils le rejoignent, coins arrondis côté libre. Un tiroir se révèle en glissant depuis son
/// bord (seule la position bouge, cadencée par DWM), au survol du bord ou sur commande.
/// </summary>
internal class GlassWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DPICHANGED = 0x02E0;
    private const int SPI_SETWORKAREA = 0x002F;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private readonly string _page;
    private readonly WebView2 _web = new();
    private bool _blurAvailable = true;

    // Géométrie (pixels physiques), fixée par la classe dérivée.
    private Rectangle _hidden;      // position cachée (hors écran)
    private Rectangle _shown;       // position révélée
    private Rectangle _triggerZone; // curseur ici (caché) → on révèle
    private Rectangle _keepZone;    // curseur ici (révélé) → on reste ; sinon on range
    private bool _shownState;
    private bool _geometryReady;
    private bool _animating;
    private bool _pinned;      // ouvert sur commande, indépendamment du curseur
    private long _pinnedAt;

    // Survol (sondage du curseur) et stabilisation après le premier affichage
    private readonly System.Windows.Forms.Timer _hover = new() { Interval = 30 };
    private readonly System.Windows.Forms.Timer _settle = new() { Interval = 50 };
    private int _settleTicks;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = -1;

    /// <summary>Message envoyé par la page (<c>chrome.webview.postMessage</c>), sauf « close » qui range le tiroir.</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Le tiroir a bougé (glissé, mise en place) : le verre de son écran doit suivre.</summary>
    public event Action<GlassWindow>? Moved;

    /// <summary>Le tiroir commence à sortir (true) ou vient de finir de rentrer (false).</summary>
    public event Action<GlassWindow, bool>? PresenceChanged;

    /// <summary>
    /// Vrai quand une appli est en plein écran (jeu…) : aucun tiroir ne s'ouvre au survol, et ceux qui
    /// sont ouverts se rangent. Azuria ne dessine jamais par-dessus un jeu (CLAUDE.md).
    /// </summary>
    public static bool Suspended { get; set; }

    protected GlassWindow(string page)
    {
        _page = page;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None; // on gère les tailles en pixels physiques

        _web.Dock = DockStyle.Fill;         // taille de fenêtre constante → pas de re-layout
        _web.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(_web);

        _hover.Tick += OnHoverTick;
        _settle.Tick += (_, _) =>
        {
            LayoutGeometry();
            if (++_settleTicks >= 6) { _settle.Stop(); _settleTicks = 0; }
        };
    }

    // ----- ce que les tiroirs dérivés peuvent régler -----

    /// <summary>Écran d'ancrage (par défaut l'écran principal).</summary>
    public virtual Screen AnchorScreen => Screen.PrimaryScreen ?? Screen.AllScreens[0];

    /// <summary>Bord du cadre d'où sort le tiroir.</summary>
    public virtual DrawerEdge Edge => DrawerEdge.None;

    /// <summary>Côtés collés au cadre (fondus) : un tiroir latéral pleine hauteur touche aussi le haut et le bas.</summary>
    public virtual DrawerSides AttachedSides => Edge switch
    {
        DrawerEdge.Top => DrawerSides.Top,
        DrawerEdge.Bottom => DrawerSides.Bottom,
        DrawerEdge.Left => DrawerSides.Left | DrawerSides.Top | DrawerSides.Bottom,
        DrawerEdge.Right => DrawerSides.Right | DrawerSides.Top | DrawerSides.Bottom,
        _ => DrawerSides.None,
    };

    /// <summary>Ne vole pas le focus (vrai pour les barres/panneaux, faux pour le lanceur où l'on tape).</summary>
    protected virtual bool NoActivate => true;

    /// <summary>Révélé au survol de son bord (faux pour un tiroir ouvert sur commande).</summary>
    protected virtual bool HoverDriven => true;

    /// <summary>Appelé quand la fenêtre doit (re)calculer sa géométrie (affichage, DPI, écran).</summary>
    protected virtual void LayoutGeometry() { }

    /// <summary>Appelé juste avant une révélation au survol, avec la position du curseur.</summary>
    protected virtual void PrepareReveal(Point cursor) { }

    /// <summary>Forme du verre pour une fenêtre de <paramref name="w"/>×<paramref name="h"/> px.</summary>
    protected virtual void BuildShape(GlassShape s, int w, int h) =>
        s.Body(0, 0, w, h, Scale(ShellMetrics.Fillet), true, true, true, true);

    /// <summary>Marges (px logiques) entre la fenêtre et la zone utile de la page : les congés.</summary>
    protected virtual Padding ContentInsets => Padding.Empty;

    public bool IsShown => _shownState;

    /// <summary>Autre zone où le curseur garde le tiroir ouvert (ex. le popout sorti de la barre).</summary>
    public Func<Point, bool>? KeepAlso { get; set; }

    /// <summary>Le tiroir est en train de glisser (ouverture ou fermeture).</summary>
    public bool IsAnimating => _animating;

    /// <summary>Zone de travail de l'écran d'ancrage : intérieur du cadre, hors barre des tâches.</summary>
    protected Rectangle WorkArea => Monitors.WorkArea(AnchorScreen);

    /// <summary>Convertit une taille logique (px à 100 %) en pixels physiques pour l'écran d'ancrage.</summary>
    protected int Scale(int logical) => Monitors.Scale(AnchorScreen, logical);

    /// <summary>
    /// Configure la révélation par glissement. <paramref name="triggerZone"/> : où amener le curseur
    /// pour révéler ; <paramref name="keepZone"/> : où il doit rester pour que ça le reste.
    /// </summary>
    protected void ConfigureReveal(Rectangle hidden, Rectangle shown, Rectangle triggerZone, Rectangle keepZone)
    {
        _hidden = hidden;
        _shown = shown;
        _triggerZone = triggerZone;
        _keepZone = keepZone;
        _geometryReady = true;
        if (_animating) { _geometryPending = true; return; } // appliquée à la fin du glissé
        ApplyGeometry();
    }

    private bool _geometryPending;

    private void ApplyGeometry()
    {
        _geometryPending = false;
        Bounds = _shownState ? _shown : _hidden;
        // Au repos caché : fenêtre réellement invisible (ne déborde sur aucun autre écran).
        Visible = _shownState;
        Track();
    }

    /// <summary>Forme du verre pour la taille actuelle (coordonnées de la fenêtre).</summary>
    public GlassShape Shape
    {
        get
        {
            var s = new GlassShape();
            if (Width > 0 && Height > 0) BuildShape(s, Width, Height);
            return s;
        }
    }

    /// <summary>La forme du verre a changé sans que la taille change (ex. lanceur qui se rétracte).</summary>
    protected void ShapeChanged()
    {
        ShapeVersion++;
        Track();
    }

    /// <summary>Change à chaque modification de la forme : la surface de l'écran redessine alors le verre.</summary>
    public int ShapeVersion { get; private set; }

    // Après chaque déplacement : clics limités à la forme, rognée au bord intérieur du cadre (le
    // tiroir en sort), et le verre de l'écran suit (ScreenSurface).
    private void Track()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;
        ShapeRegion.Apply(Handle, Shape,
            new Rectangle(work.X - Left, work.Y - Top, work.Width, work.Height));
        Moved?.Invoke(this);
    }

    /// <summary>Repasse la fenêtre au-dessus des autres fenêtres « toujours au-dessus » (dont le verre).</summary>
    public void RaiseAbove()
    {
        if (IsHandleCreated)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Envoie un message JSON à la page (<c>chrome.webview</c>, évènement « message »).</summary>
    public void PostJson(string json) => _web.CoreWebView2?.PostWebMessageAsJson(json);

    /// <summary>
    /// Révèle le tiroir (sur commande). <paramref name="pinned"/> : il reste ouvert même si le curseur
    /// n'est pas dessus, jusqu'à ce qu'il y entre puis en sorte, ou qu'on clique ailleurs.
    /// </summary>
    public void Reveal(bool pinned = false)
    {
        if (!_geometryReady || _animating || Suspended) return;
        if (pinned) { _pinned = true; _pinnedAt = Environment.TickCount64; }
        if (!_shownState) Slide(true);
    }

    /// <summary>Range le tiroir (sur commande).</summary>
    public void Conceal()
    {
        if (_geometryReady && !_animating && _shownState) Slide(false);
    }

    // ----- fenêtre native -----

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;                 // hors Alt-Tab et hors barre des tâches
            cp.ExStyle |= WS_EX_NOREDIRECTIONBITMAP;        // seule la page s'affiche (le verre est dessous)
            if (NoActivate) cp.ExStyle |= WS_EX_NOACTIVATE; // ne vole pas le focus
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => NoActivate;

    // Rien à peindre : la fenêtre n'a pas de surface GDI, le verre est dessous.
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutGeometry();
        // Pas encore de place connue (popout jamais survolé…) : la fenêtre resterait affichée à 0,0
        // avec sa taille par défaut. Elle reste cachée jusqu'à sa première géométrie.
        if (!_geometryReady) Visible = false;
        _settle.Start(); // re-calcule quelques fois le temps que tout se stabilise
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Dwm.UseDarkMode(Handle);
        Dwm.SetCorner(Handle, Dwm.Corner.DoNotRound); // la forme est la nôtre
        _blurAvailable = TransparencyEnabled();

        await InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Azuria", "WebView2");
        Directory.CreateDirectory(dataDir);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        string webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        core.SetVirtualHostNameToFolderMapping(
            "azuria.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);
        // Icônes des applis et des jeux, mises en cache par le catalogue du lanceur.
        Directory.CreateDirectory(Catalog.IconDir);
        core.SetVirtualHostNameToFolderMapping(
            Catalog.IconHost, Catalog.IconDir, CoreWebView2HostResourceAccessKind.Allow);

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.WebMessageReceived += (_, e) =>
        {
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            if (msg == "close") Conceal();
            else MessageReceived?.Invoke(msg);
        };

        ApplyBlurClass();
        // Teinte d'accent choisie dans les réglages : posée à chaque chargement, et en direct.
        await ApplyThemeAsync();
        Settings.Changed += OnSettingsChanged;
        // La page reçoit la zone utile (hors congés) en px CSS (drawer.js).
        var i = ContentInsets;
        string query = $"?l={i.Left}&t={i.Top}&r={i.Right}&b={i.Bottom}";
        core.Navigate($"https://azuria.local/{_page}{query}");

        if (HoverDriven) _hover.Start();
    }

    private string? _themeScriptId;
    private string _theme = "";

    private async Task ApplyThemeAsync()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        string js = Settings.Current.ThemeScript();
        if (js == _theme) return;
        _theme = js;
        if (_themeScriptId is not null) core.RemoveScriptToExecuteOnDocumentCreated(_themeScriptId);
        _themeScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(js);
        _ = core.ExecuteScriptAsync(js);
    }

    private async void OnSettingsChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(OnSettingsChanged); return; }
        try { await ApplyThemeAsync(); } catch { /* page en cours de fermeture */ }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Settings.Changed -= OnSettingsChanged;
        base.OnHandleDestroyed(e);
    }

    // ----- révélation par glissement -----

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (!_geometryReady || _animating) return;
        if (Suspended)
        {
            if (_shownState) Slide(false);
            return;
        }
        GetCursorPos(out POINT p);
        var cursor = new Point(p.X, p.Y);
        // Ouvert sur commande (« épinglé ») : reste ouvert jusqu'à ce que le curseur y entre puis en
        // sorte, ou qu'on clique ailleurs.
        if (_pinned)
        {
            if (!_shownState || _keepZone.Contains(cursor)) _pinned = false;
            else if (GetAsyncKeyState(0x01 /* VK_LBUTTON */) < 0 && Environment.TickCount64 - _pinnedAt > 400)
            {
                _pinned = false;
                Slide(false);
            }
            return;
        }
        Rectangle hot = _shownState ? _keepZone : _triggerZone;
        bool want = hot.Contains(cursor) || (_shownState && KeepAlso?.Invoke(cursor) == true);
        // Bouton gauche enfoncé (une fenêtre qu'on glisse, une sélection) : un tiroir fermé ne sort pas.
        if (want && !_shownState && GetAsyncKeyState(0x01 /* VK_LBUTTON */) < 0) return;
        if (want != _shownState)
        {
            if (want) PrepareReveal(cursor); // ex. se recentrer sur l'écran du curseur
            Slide(want);
        }
    }

    /// <summary>
    /// Glisse la fenêtre entre caché et révélé, cadencé par DWM (synchronisé au rafraîchissement).
    /// Seule la position change (taille constante), donc rien ne se remet en page pendant le glissé.
    ///
    /// Entre deux pas, on rend la main à la boucle de messages : c'est là que Windows.UI.Composition
    /// envoie les changements du verre (position de la couche) à DWM. Une boucle bloquante déplaçait
    /// la page à chaque image mais ne livrait le verre qu'à la fin : le contenu arrivait avant le
    /// panneau. L'attente de l'image suivante (DwmFlush) se fait donc hors du fil d'interface.
    /// </summary>
    private async void Slide(bool shown)
    {
        _shownState = shown;
        _animating = true;
        try
        {
            // Apparaît au point caché avant de glisser vers l'intérieur.
            if (shown && !Visible)
            {
                Bounds = _hidden;
                Track();        // région vide : rien ne dépasse du cadre
                Visible = true;
                // Au-dessus du verre de l'écran (sa page se pose sur son propre verre).
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            if (shown) PresenceChanged?.Invoke(this, true); // un tiroir latéral libère les coins du cadre
            var from = Bounds;
            var to = shown ? _shown : _hidden;
            double dur = shown ? 200 : 140;   // DA §11 : ouverture ~0,2 s, fermeture 0,14 s
            var sw = Stopwatch.StartNew();
            while (true)
            {
                double t = sw.Elapsed.TotalMilliseconds;
                if (t >= dur) break;
                double p = t / dur;
                double e = shown ? Easing.In(p)    // entrée : décélération
                                 : Easing.Out(p);  // sortie : accélération
                SetWindowPos(Handle, 0, Easing.Lerp(from.X, to.X, e), Easing.Lerp(from.Y, to.Y, e), 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                Track();
                await Task.Run(Dwm.Flush); // la boucle de messages tourne : le verre part avec la page
                if (IsDisposed || !IsHandleCreated) return;
            }
            SetWindowPos(Handle, 0, to.X, to.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            // Une fois rangée, la fenêtre est vraiment masquée.
            if (!shown) Visible = false;
            Track();
            if (!shown) PresenceChanged?.Invoke(this, false);
        }
        finally
        {
            _animating = false;
            if (_geometryPending && !IsDisposed && IsHandleCreated) ApplyGeometry(); // taille ou place changée en route
        }

        if (shown && !NoActivate)
        {
            // Tiroir où l'on tape (lanceur) : on prend le focus et on prévient la page.
            Activate();
            _web.Focus();
            _web.CoreWebView2?.PostWebMessageAsString("shown");
        }
    }

    // ----- messages système -----

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_DPICHANGED:
                // Changement d'écran : on recalcule nous-mêmes la géométrie (au DPI de l'écran
                // d'ancrage) au lieu de laisser WinForms redimensionner la fenêtre.
                if (_geometryReady && !_animating) LayoutGeometry();
                m.Result = 0;
                return;

            case WM_SETTINGCHANGE:
                // Zone de travail changée (cadre réservé, barre des tâches déplacée…).
                if (m.WParam == SPI_SETWORKAREA && _geometryReady && !_animating) LayoutGeometry();
                bool now = TransparencyEnabled();
                if (now != _blurAvailable)
                {
                    _blurAvailable = now;
                    ApplyBlurClass();
                }
                break;
        }
        base.WndProc(ref m);
    }

    private void ApplyBlurClass()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        string js = _blurAvailable
            ? "document.documentElement.classList.remove('no-blur')"
            : "document.documentElement.classList.add('no-blur')";
        _ = core.ExecuteScriptAsync(js);
    }

    internal static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }
}
