using System.Diagnostics;
using System.Numerics;
using Microsoft.Win32;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using static VerreFloute.Interop;

namespace VerreFloute;

internal enum BackdropKind
{
    /// <summary>Acrylic posé par DWM (DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW).</summary>
    DwmAcrylic,
    /// <summary>Flou composé par nous : pinceau d'arrière-plan de l'hôte + découpe arrondie.</summary>
    HostBackdrop,
}

internal sealed class GlassOptions
{
    public string Title = "Azuria — prototype verre";
    public bool Frameless;
    public bool Topmost;
    public bool ToolWindow;
    public BackdropKind Backdrop = BackdropKind.DwmAcrylic;
    public bool RegionClip;                 // découpe par SetWindowRgn
    public bool ContentClip;                // découpe arrondie du contenu composé
    public float Radius = 18f;              // rayon logique (px à 100 %)
    public bool Panel;                      // panneau : glissé d'ouverture/fermeture
    public bool TryAccentHostBackdrop;      // SetWindowCompositionAttribute(ACCENT 5) — non documenté
    public bool SetDwmBackdropToo;          // en mode HostBackdrop, poser aussi DWMSBT acrylic
    public int CornerPreference = -1;       // DWMWCP_* si >= 0
    public int Width = 520;
    public int Height = 360;
    public int? X;
    public int? Y;
}

/// <summary>La fenêtre de verre du prototype. Toutes les variantes passent par ici.</summary>
internal sealed class GlassWindow
{
    // Jetons de la DA « Encre » (docs/da.md §3)
    private static readonly Color Surface = Color.FromArgb(214, 16, 18, 25);        // rgba(16,18,25,0.84)
    private static readonly Color Solid = Color.FromArgb(255, 16, 18, 25);          // --solid
    private static readonly Color Raised = Color.FromArgb(15, 230, 234, 242);       // rgba(230,234,242,0.06)
    private static readonly Color BorderStrong = Color.FromArgb(38, 230, 234, 242); // rgba(230,234,242,0.15)

    private const string ClassName = "AzuriaVerreFloute";
    private static bool s_classRegistered;
    private static readonly WndProc s_wndProc = StaticWndProc;
    private static readonly Dictionary<nint, GlassWindow> s_instances = new();
    private static GlassWindow? s_pending;

    private readonly GlassOptions _opt;
    private nint _hwnd;
    private uint _dpi = 96;

    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;     // porte la découpe
    private ContainerVisual? _content;  // animé pour le glissé
    private SpriteVisual? _backdropVisual;
    private SpriteVisual? _tintVisual;
    private CompositionColorBrush? _tintBrush;
    private ShapeVisual? _chromeVisual; // bordure + cartes
    private CompositionRoundedRectangleGeometry? _clipGeometry;
    private CompositionRoundedRectangleGeometry? _borderGeometry;
    private CompositionGeometricClip? _clip;

    private static readonly float[] TintSteps = [0.84f, 0.6f, 0.45f, 0f];
    private int _tintStep;

    private bool _glassAvailable = true;  // transparence de Windows active
    private bool _manualSolid;            // repli forcé à la main (touche S)
    private bool _panelShown = true;
    private int _dwmBackdropApplied = -1;
    private bool _layeredTest;
    private bool _darkMode = true;

    public nint Hwnd => _hwnd;

    public GlassWindow(GlassOptions opt) => _opt = opt;

    public void Create()
    {
        RegisterClassOnce();

        uint style = _opt.Frameless ? WS_POPUP : WS_OVERLAPPEDWINDOW;
        uint exStyle = WS_EX_NOREDIRECTIONBITMAP;
        if (_opt.Topmost) exStyle |= WS_EX_TOPMOST;
        if (_opt.ToolWindow) exStyle |= WS_EX_TOOLWINDOW;

        SystemParametersInfoW(SPI_GETWORKAREA, 0, out RECT work, 0);
        // DPI du moniteur principal : on crée d'abord à 96 puis WM_DPICHANGED ajusterait ;
        // pour un prototype on lit le DPI après création et on redimensionne.
        int w = _opt.Width, h = _opt.Height;
        int x = _opt.X ?? (work.Left + (work.Right - work.Left - w) / 2);
        int y = _opt.Y ?? (work.Top + (work.Bottom - work.Top - h) / 2);

        s_pending = this;
        _hwnd = CreateWindowExW(exStyle, ClassName, _opt.Title, style, x, y, w, h, 0, 0, 0, 0);
        s_pending = null;
        if (_hwnd == 0) throw new InvalidOperationException("CreateWindowExW a échoué");
        s_instances[_hwnd] = this;

        _dpi = GetDpiForWindow(_hwnd);
        if (_dpi != 96)
        {
            w = Scale(_opt.Width, _dpi);
            h = Scale(_opt.Height, _dpi);
            if (_opt.X is null && _opt.Y is null)
            {
                x = work.Left + (work.Right - work.Left - w) / 2;
                y = work.Top + (work.Bottom - work.Top - h) / 2;
            }
            SetWindowPos(_hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        // Thème sombre d'abord (docs/flou-windows.md §8)
        int dark = 1;
        LogHr("DWMWA_USE_IMMERSIVE_DARK_MODE", DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4));

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        LogHr("DwmExtendFrameIntoClientArea(-1)", DwmExtendFrameIntoClientArea(_hwnd, ref margins));

        if (_opt.CornerPreference >= 0)
        {
            int corner = _opt.CornerPreference;
            LogHr($"DWMWA_WINDOW_CORNER_PREFERENCE={corner}", DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4));
        }

        if (_opt.TryAccentHostBackdrop) ApplyAccentHostBackdrop(true);

        _glassAvailable = ReadTransparencyEnabled();
        BuildVisualTree();
        ApplyBackdrop();
        UpdateLayout();

        ShowWindow(_hwnd, SW_SHOW);
        UpdateWindow(_hwnd);
        Log($"fenêtre créée : hwnd=0x{_hwnd:X}, dpi={_dpi}, transparence Windows={(_glassAvailable ? "active" : "coupée")}");
    }

    // ---------- arbre de visuels ----------

    private void BuildVisualTree()
    {
        CompositionHost.EnsureInitialized();
        var c = CompositionHost.Compositor;

        _target = CompositionHost.CreateTarget(_hwnd);

        _root = c.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _root;

        _content = c.CreateContainerVisual();
        _content.RelativeSizeAdjustment = Vector2.One;
        _root.Children.InsertAtTop(_content);

        if (_opt.Backdrop == BackdropKind.HostBackdrop)
        {
            try
            {
                var backdropBrush = c.CreateHostBackdropBrush();
                _backdropVisual = c.CreateSpriteVisual();
                _backdropVisual.Brush = backdropBrush;
                _backdropVisual.RelativeSizeAdjustment = Vector2.One;
                _content.Children.InsertAtTop(_backdropVisual);
                Log("CreateHostBackdropBrush : OK (reste à voir ce qu'il affiche)");
            }
            catch (Exception e)
            {
                Log($"CreateHostBackdropBrush : ÉCHEC — {e.Message}");
            }
        }

        _tintBrush = c.CreateColorBrush(Surface);
        _tintVisual = c.CreateSpriteVisual();
        _tintVisual.Brush = _tintBrush;
        _tintVisual.RelativeSizeAdjustment = Vector2.One;
        _content.Children.InsertAtTop(_tintVisual);

        // Bordure (approximation : trait uniforme --border-strong ; le liseré haut/gauche
        // demanderait un tracé partiel, hors sujet pour ce prototype) + deux « cartes ».
        _chromeVisual = c.CreateShapeVisual();
        _borderGeometry = c.CreateRoundedRectangleGeometry();
        var borderShape = c.CreateSpriteShape(_borderGeometry);
        borderShape.StrokeBrush = c.CreateColorBrush(BorderStrong);
        borderShape.StrokeThickness = 1f;
        _chromeVisual.Shapes.Add(borderShape);

        for (int i = 0; i < 2; i++)
        {
            var cardGeo = c.CreateRoundedRectangleGeometry();
            cardGeo.CornerRadius = new Vector2(ScaleF(12, _dpi));
            cardGeo.Size = new Vector2(ScaleF(200, _dpi), ScaleF(64, _dpi));
            cardGeo.Offset = new Vector2(ScaleF(24, _dpi), ScaleF(24 + i * 80, _dpi));
            var card = c.CreateSpriteShape(cardGeo);
            card.FillBrush = c.CreateColorBrush(Raised);
            _chromeVisual.Shapes.Add(card);
        }
        _content.Children.InsertAtTop(_chromeVisual);

        _clipGeometry = c.CreateRoundedRectangleGeometry();
        _clip = c.CreateGeometricClip(_clipGeometry);
        if (_opt.ContentClip) _root.Clip = _clip;
    }

    private void UpdateLayout()
    {
        if (_root is null) return;
        GetClientRect(_hwnd, out RECT rc);
        float w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        float r = ScaleF(_opt.Radius, _dpi);

        if (_clipGeometry is not null)
        {
            _clipGeometry.Size = new Vector2(w, h);
            _clipGeometry.CornerRadius = new Vector2(r);
        }
        if (_chromeVisual is not null) _chromeVisual.Size = new Vector2(w, h);
        if (_borderGeometry is not null)
        {
            _borderGeometry.Offset = new Vector2(0.5f, 0.5f);
            _borderGeometry.Size = new Vector2(w - 1, h - 1);
            _borderGeometry.CornerRadius = new Vector2(Math.Max(0, r - 0.5f));
        }
        if (_opt.RegionClip) ApplyRegion();
    }

    // ---------- région, accent, repli ----------

    private void ApplyRegion()
    {
        GetWindowRect(_hwnd, out RECT wr);
        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        int d = Scale((int)(_opt.Radius * 2), _dpi);
        nint rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d);
        // Après SetWindowRgn, la région appartient au système : pas de DeleteObject.
        int res = SetWindowRgn(_hwnd, rgn, true);
        Log($"SetWindowRgn(arrondi {_opt.Radius}) : {(res != 0 ? "OK" : "ÉCHEC")}");
    }

    private void ClearRegion()
    {
        SetWindowRgn(_hwnd, 0, true);
        Log("SetWindowRgn(aucune)");
    }

    private unsafe void ApplyAccentHostBackdrop(bool enable) => ApplyAccent(enable ? ACCENT_ENABLE_HOSTBACKDROP : ACCENT_DISABLED);

    private static readonly int[] AccentStates = [ACCENT_DISABLED, ACCENT_ENABLE_BLURBEHIND, ACCENT_ENABLE_ACRYLICBLURBEHIND, ACCENT_ENABLE_HOSTBACKDROP];
    private int _accentStep;

    private unsafe void ApplyAccent(int state)
    {
        // gradient teinté (encre à ~40 %) en ABGR, requis par ACRYLICBLURBEHIND pour un rendu lisible
        uint gradient = 0x66151210;
        var policy = new ACCENT_POLICY { AccentState = state, AccentFlags = 0, GradientColor = gradient };
        var data = new WINDOWCOMPOSITIONATTRIBDATA
        {
            Attrib = WCA_ACCENT_POLICY,
            pvData = (nint)(&policy),
            cbData = sizeof(ACCENT_POLICY),
        };
        int res = SetWindowCompositionAttribute(_hwnd, ref data);
        string[] noms = ["DISABLED", "?", "?", "BLURBEHIND", "ACRYLICBLURBEHIND", "HOSTBACKDROP"];
        Log($"SetWindowCompositionAttribute(ACCENT_{noms[state]}={state}) [NON DOCUMENTÉE] : retour {res}");
    }

    private void ApplyBackdrop()
    {
        bool glass = _glassAvailable && !_manualSolid;

        if (_opt.Backdrop == BackdropKind.DwmAcrylic || _opt.SetDwmBackdropToo)
        {
            int type = glass ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
            if (type != _dwmBackdropApplied)
            {
                LogHr($"DWMWA_SYSTEMBACKDROP_TYPE={(type == DWMSBT_TRANSIENTWINDOW ? "ACRYLIC" : "NONE")}",
                    DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4));
                _dwmBackdropApplied = type;
            }
        }
        if (_backdropVisual is not null) _backdropVisual.IsVisible = glass;
        if (_tintBrush is not null)
        {
            var tint = Color.FromArgb((byte)(255 * TintSteps[_tintStep]), Surface.R, Surface.G, Surface.B);
            _tintBrush.Color = glass ? tint : Solid;
        }
        Log($"rendu : {(glass ? "verre (flou)" : "repli --solid (opaque)")}");
    }

    private static bool ReadTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }

    // ---------- glissé de panneau ----------

    /// <summary>Glissé « composition » : le contenu bouge et fond, la fenêtre ne bouge pas.
    /// (Recette DA §11 : 0,20 s, fondu + glissé de 12, décélération.)</summary>
    private void SlideComposition(bool show)
    {
        if (_content is null) return;
        var c = CompositionHost.Compositor;
        float dx = ScaleF(12, _dpi);

        var easeIn = c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f));
        var easeOut = c.CreateCubicBezierEasingFunction(new Vector2(0.3f, 0f), new Vector2(1f, 1f));

        var offset = c.CreateVector3KeyFrameAnimation();
        offset.Duration = TimeSpan.FromMilliseconds(show ? 200 : 140);
        if (show)
        {
            _content.Offset = new Vector3(dx, 0, 0);
            offset.InsertKeyFrame(1f, Vector3.Zero, easeIn);
        }
        else
        {
            offset.InsertKeyFrame(1f, new Vector3(dx, 0, 0), easeOut);
        }
        _content.StartAnimation("Offset", offset);

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.Duration = offset.Duration;
        fade.InsertKeyFrame(1f, show ? 1f : 0f, show ? easeIn : easeOut);
        _content.StartAnimation("Opacity", fade);
        Log($"glissé composition : {(show ? "ouverture 0,20 s" : "fermeture 0,14 s")} (animé par le compositeur, fluidité à juger à l'œil)");
    }

    /// <summary>Glissé « fenêtre » : la fenêtre elle-même est déplacée image par image,
    /// cadencée par DwmFlush. Mesure les temps d'image (la partie mesurable de la question 3).</summary>
    private void SlideWindowMove(int distanceLogical, int durationMs)
    {
        GetWindowRect(_hwnd, out RECT wr);
        int x0 = wr.Left, y0 = wr.Top;
        int dx = Scale(distanceLogical, _dpi);

        var times = new List<double>();
        var sw = Stopwatch.StartNew();
        double last = 0;
        while (true)
        {
            double t = sw.Elapsed.TotalMilliseconds;
            if (t >= durationMs) break;
            double p = CubicBezier(0.2, 0.0, 0.0, 1.0, t / durationMs);
            SetWindowPos(_hwnd, 0, x0 + (int)(p * dx), y0, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            DwmFlush();
            double now = sw.Elapsed.TotalMilliseconds;
            times.Add(now - last);
            last = now;
        }
        SetWindowPos(_hwnd, 0, x0, y0, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        if (times.Count > 1)
        {
            times.RemoveAt(0); // la première image paie la mise en route
            double avg = times.Average(), max = times.Max();
            int late = times.Count(d => d > 20.0);
            Log($"glissé fenêtre {distanceLogical} px / {durationMs} ms : {times.Count} images, " +
                $"moyenne {avg:F1} ms, max {max:F1} ms, images > 20 ms : {late}");
        }
    }

    private static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        x = Math.Clamp(x, 0, 1);
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double xt = Bez(x1, x2, t) - x;
            double dxt = BezDeriv(x1, x2, t);
            if (Math.Abs(dxt) < 1e-6) break;
            t = Math.Clamp(t - xt / dxt, 0, 1);
        }
        return Bez(y1, y2, t);

        static double Bez(double p1, double p2, double t) =>
            3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        static double BezDeriv(double p1, double p2, double t) =>
            3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    // ---------- messages ----------

    private static nint StaticWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        var self = s_instances.TryGetValue(hWnd, out var w) ? w : s_pending;
        return self?.HandleMessage(hWnd, msg, wParam, lParam) ?? DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private nint HandleMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_SIZE:
                UpdateLayout();
                return 0;

            case WM_ERASEBKGND:
                return 1;

            case WM_NCHITTEST when _opt.Frameless:
            {
                nint hit = DefWindowProcW(hWnd, msg, wParam, lParam);
                return hit == HTCLIENT ? HTCAPTION : hit; // glisser la fenêtre à la souris
            }

            case WM_SETTINGCHANGE:
            {
                bool now = ReadTransparencyEnabled();
                if (now != _glassAvailable)
                {
                    _glassAvailable = now;
                    Log($"transparence de Windows : {(now ? "réactivée" : "coupée")}");
                    ApplyBackdrop();
                }
                return 0;
            }

            case WM_DPICHANGED:
            {
                _dpi = (uint)(wParam.ToInt64() & 0xFFFF);
                unsafe
                {
                    var rc = *(RECT*)lParam;
                    SetWindowPos(hWnd, 0, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, SWP_NOZORDER | SWP_NOACTIVATE);
                }
                return 0;
            }

            case WM_KEYDOWN:
                HandleKey((int)wParam);
                return 0;

            case WM_CLOSE:
                DestroyWindow(hWnd);
                return 0;

            case WM_DESTROY:
                s_instances.Remove(hWnd);
                if (s_instances.Count == 0) PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void HandleKey(int vk)
    {
        switch (vk)
        {
            case VK_ESCAPE:
                DestroyWindow(_hwnd);
                break;

            case >= '1' and <= '5':
            {
                float[] radii = [8, 14, 18, 22, 30];
                _opt.Radius = radii[vk - '1'];
                Log($"rayon = {_opt.Radius}");
                UpdateLayout();
                break;
            }

            case 'R':
                _opt.RegionClip = !_opt.RegionClip;
                if (_opt.RegionClip) ApplyRegion(); else ClearRegion();
                break;

            case 'C':
                _opt.ContentClip = !_opt.ContentClip;
                if (_root is not null) _root.Clip = _opt.ContentClip ? _clip : null;
                Log($"découpe du contenu composé : {(_opt.ContentClip ? "activée" : "retirée")}");
                break;

            case 'T':
                _tintStep = (_tintStep + 1) % TintSteps.Length;
                Log($"voile : opacité {TintSteps[_tintStep]:0.00}");
                ApplyBackdrop();
                break;

            case 'S':
                _manualSolid = !_manualSolid;
                Log($"repli forcé : {(_manualSolid ? "oui" : "non")}");
                ApplyBackdrop();
                break;

            case 'A':
                // cycle : DISABLED -> BLURBEHIND -> ACRYLICBLURBEHIND -> HOSTBACKDROP
                _accentStep = (_accentStep + 1) % AccentStates.Length;
                // en désactivant l'accent, on remet d'abord le matériau DWM visible
                ApplyAccent(AccentStates[_accentStep]);
                break;

            case 'D':
            {
                // bascule DWMSBT acrylic <-> none, indépendamment du reste
                int type = _dwmBackdropApplied == DWMSBT_TRANSIENTWINDOW ? DWMSBT_NONE : DWMSBT_TRANSIENTWINDOW;
                LogHr($"DWMWA_SYSTEMBACKDROP_TYPE={(type == DWMSBT_TRANSIENTWINDOW ? "ACRYLIC" : "NONE")}",
                    DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4));
                _dwmBackdropApplied = type;
                break;
            }

            case 'M':
            {
                // cycle des matériaux DWM : none -> mica -> acrylic -> tabbed -> none
                int next = _dwmBackdropApplied switch
                {
                    DWMSBT_NONE => DWMSBT_MAINWINDOW,
                    DWMSBT_MAINWINDOW => DWMSBT_TRANSIENTWINDOW,
                    DWMSBT_TRANSIENTWINDOW => DWMSBT_TABBEDWINDOW,
                    _ => DWMSBT_NONE,
                };
                string[] noms = ["?", "NONE", "MICA", "ACRYLIC", "MICA_ALT"];
                LogHr($"DWMWA_SYSTEMBACKDROP_TYPE={noms[next]}", DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref next, 4));
                _dwmBackdropApplied = next;
                break;
            }

            case 'E':
            {
                _darkMode = !_darkMode;
                int dark = _darkMode ? 1 : 0;
                LogHr($"DWMWA_USE_IMMERSIVE_DARK_MODE={dark}", DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4));
                break;
            }

            case 'L':
            {
                _layeredTest = !_layeredTest;
                nint ex = GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
                if (_layeredTest)
                {
                    SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, ex | (nint)WS_EX_LAYERED);
                    SetLayeredWindowAttributes(_hwnd, 0, 160, LWA_ALPHA);
                }
                else
                {
                    SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, ex & ~(nint)WS_EX_LAYERED);
                }
                Log($"essai WS_EX_LAYERED alpha 160 : {(_layeredTest ? "activé" : "retiré")} (observer l'effet sur le flou)");
                break;
            }

            case VK_SPACE when _opt.Panel:
                _panelShown = !_panelShown;
                SlideComposition(_panelShown);
                break;

            case 'W' when _opt.Panel:
                SlideWindowMove(-100, 200);
                break;

            case 'X':
                SlideWindowMove(-300, 1000);
                break;
        }
    }

    private static void RegisterClassOnce()
    {
        if (s_classRegistered) return;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0003, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = s_wndProc,
            hCursor = LoadCursorW(0, IDC_ARROW),
            lpszClassName = ClassName,
        };
        if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("RegisterClassExW a échoué");
        s_classRegistered = true;
    }

    public static void RunMessageLoop()
    {
        while (GetMessageW(out MSG msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    // ---------- journal ----------

    private void Log(string message) => Console.WriteLine($"[{_opt.Title}] {message}");

    private void LogHr(string what, int hr) =>
        Console.WriteLine($"[{_opt.Title}] {what} : hr=0x{hr:X8}{(hr == 0 ? " (OK)" : " (ÉCHEC)")}");
}
