using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// Doublures du pavage (idée des animations de Hyprland, réécrite ici). Redimensionner une fenêtre
/// à chaque image force l'appli à se redessiner à chaque fois : la plupart n'y arrivent pas à 60 i/s,
/// et l'animation saccade. Pendant l'animation, c'est donc une miniature DWM de la fenêtre qui glisse
/// et s'étire, composée par DWM sur une fenêtre transparente d'Azuria ; la vraie fenêtre ne prend sa
/// taille qu'une fois, à la fin.
///
/// Garde-fous (CLAUDE.md) : la miniature est l'API documentée de DWM (celle de l'aperçu de la barre
/// des tâches) ; rien n'est lu ni injecté dans l'appli. Jamais pour un jeu : le pavage n'en touche pas.
/// </summary>
internal sealed class StandIns : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags; public RECT rcDestination; public RECT rcSource; public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }
    private const uint DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4,
                       DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;
    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint dest, nint src, out nint thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumb);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, uint attr, out RECT value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);

    private static readonly nint HWND_TOP = 0;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    private sealed record Held(Stage Stage, nint Thumb) { public Rectangle Shown; }

    private readonly Dictionary<string, Stage> _stages = new();  // une scène par écran
    private readonly Dictionary<nint, Held> _held = new();       // fenêtre → sa doublure

    /// <summary>Vrai si la fenêtre est en ce moment montrée par sa doublure.</summary>
    public bool Holds(nint h) => _held.ContainsKey(h);

    /// <summary>Ce que la doublure montre à l'écran (pour enchaîner une animation sur la précédente).</summary>
    public Rectangle? Shown(nint h) => _held.TryGetValue(h, out var d) ? d.Shown : null;

    /// <summary>
    /// Pose une doublure de la fenêtre à <paramref name="at"/> (partie visible, en pixels écran), sur
    /// l'écran donné. Faux si DWM refuse : la fenêtre s'anime alors elle-même.
    /// </summary>
    public bool Hold(nint h, Screen screen, Rectangle at)
    {
        if (!_held.ContainsKey(h))
        {
            if (!_stages.TryGetValue(screen.DeviceName, out var stage) || stage.IsDisposed)
                _stages[screen.DeviceName] = stage = new Stage();
            stage.Cover(screen.Bounds);
            if (DwmRegisterThumbnail(stage.Handle, h, out nint thumb) != 0 || thumb == 0) return false;
            _held[h] = new Held(stage, thumb);
        }
        Show(h, at);
        return true;
    }

    /// <summary>Déplace la doublure (partie visible de la fenêtre, en pixels écran).</summary>
    public void Show(nint h, Rectangle r)
    {
        if (!_held.TryGetValue(h, out var d)) return;
        d.Shown = r;
        // Source : la partie visible de la fenêtre (sans la bordure invisible de Windows 11).
        var src = new RECT();
        if (GetWindowRect(h, out var wr))
        {
            src = new RECT { Left = 0, Top = 0, Right = wr.Right - wr.Left, Bottom = wr.Bottom - wr.Top };
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var fr, Marshal.SizeOf<RECT>()) == 0)
                src = new RECT { Left = fr.Left - wr.Left, Top = fr.Top - wr.Top, Right = fr.Right - wr.Left, Bottom = fr.Bottom - wr.Top };
        }
        var o = d.Stage.Origin;
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_OPACITY | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT { Left = r.Left - o.X, Top = r.Top - o.Y, Right = r.Right - o.X, Bottom = r.Bottom - o.Y },
            rcSource = src,
            opacity = 255, fVisible = true, fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(d.Thumb, ref props);
    }

    /// <summary>Retire la doublure : la vraie fenêtre se montre de nouveau seule.</summary>
    public void Release(nint h)
    {
        if (!_held.Remove(h, out var d)) return;
        DwmUnregisterThumbnail(d.Thumb);
        if (!_held.Values.Any(x => x.Stage == d.Stage)) d.Stage.Hide();
    }

    public void Dispose()
    {
        foreach (var h in _held.Keys.ToList()) Release(h);
        foreach (var s in _stages.Values) s.Dispose();
        _stages.Clear();
    }

    /// <summary>
    /// La scène d'un écran : couvre l'écran, sans contenu propre (seules les miniatures s'y voient),
    /// jamais active, transparente aux clics, au-dessus des fenêtres d'applis et sous les barres
    /// d'Azuria (toujours au premier plan).
    /// </summary>
    private sealed class Stage : Form
    {
        private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        private const uint LWA_ALPHA = 0x2;
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

        public Point Origin { get; private set; }

        public Stage()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
            Dwm.SetCorner(Handle, Dwm.Corner.DoNotRound);
        }

        /// <summary>Couvre l'écran et passe devant les fenêtres d'applis.</summary>
        public void Cover(Rectangle bounds)
        {
            _ = Handle; // fenêtre créée avant d'y poser une miniature
            Origin = bounds.Location;
            SetWindowPos(Handle, HWND_TOP, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (!Visible) Visible = true;
        }
    }
}
