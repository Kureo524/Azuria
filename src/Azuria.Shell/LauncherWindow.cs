using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Le lanceur (DA §9) : un tiroir qui sort de la bande basse du cadre, au clic sur le logo Azuria de
/// la barre ou à la touche Windows, centré sur l'écran du curseur. On y tape : il prend le focus, et
/// se range sur Échap ou dès qu'on clique ailleurs.
///
/// Sa hauteur suit le nombre de résultats, jusqu'à sa taille maximale. La fenêtre garde toujours la
/// taille maximale (la page ne se remet jamais en page) : seul le verre, collé en bas, monte ou
/// descend, et les clics suivent sa forme. Les résultats sont posés en bas, contre le champ, donc
/// toujours sur le verre.
/// </summary>
internal sealed class LauncherWindow : GlassWindow
{
    private const int BodyWLogical = 560;
    private const int BodyHLogical = 380;   // hauteur maximale
    private const int MinHLogical = 64;     // champ de recherche + marges
    private const int RadiusLogical = 18;   // DA §8 : lanceur
    private const double ResizeMs = 140;    // DA §11 : changement d'état

    private Screen _current = Screen.PrimaryScreen ?? Screen.AllScreens[0];
    private double _glassH = BodyHLogical;  // hauteur visible du verre (px logiques), animée
    private int _targetH = BodyHLogical;
    private int _generation;

    public LauncherWindow() : base("launcher.html")
    {
        // La page annonce la hauteur de son contenu (« height:<px CSS> », 1 px CSS = 1 px logique).
        MessageReceived += msg =>
        {
            if (msg.StartsWith("height:") && int.TryParse(msg[7..], out int h)) SetGlassHeight(h);
        };
    }

    public override Screen AnchorScreen => _current;
    public override DrawerEdge Edge => DrawerEdge.Bottom;
    protected override bool NoActivate => false;  // on tape dans la recherche
    protected override bool HoverDriven => false; // ouvert sur commande

    protected override Padding ContentInsets => new(ShellMetrics.Fillet, 0, ShellMetrics.Fillet, 0);

    protected override void BuildShape(GlassShape s, int w, int h)
    {
        int f = Scale(ShellMetrics.Fillet);
        int body = w - 2 * f;
        int vh = Math.Clamp((int)Math.Round(_glassH * Monitors.Dpi(AnchorScreen) / 96.0), Scale(MinHLogical), h);
        s.Body(f, h - vh, body, vh, Scale(RadiusLogical), true, true, false, false)
         .Concave(0, h - f, f, 0, h - f)
         .Concave(f + body, h - f, f, w, h - f);
    }

    /// <summary>Ouvre le lanceur sur l'écran du point donné, ou le range s'il est ouvert.</summary>
    public void Toggle(Point at)
    {
        if (IsShown) { Conceal(); return; }
        _current = Screen.FromPoint(at);
        LayoutGeometry();
        Reveal();
    }

    // Le verre glisse vers la hauteur voulue ; un nouveau changement en cours de route repart de là.
    private async void SetGlassHeight(int logical)
    {
        int target = Math.Clamp(logical, MinHLogical, BodyHLogical);
        if (target == _targetH) return;
        _targetH = target;
        int gen = ++_generation;
        double from = _glassH;
        if (!IsShown) { _glassH = target; ShapeChanged(); return; } // rangé : pas d'animation
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < ResizeMs)
        {
            _glassH = from + (target - from) * Easing.In(sw.Elapsed.TotalMilliseconds / ResizeMs);
            ShapeChanged();
            await Task.Run(Dwm.Flush);
            if (gen != _generation || IsDisposed) return;
        }
        _glassH = target;
        ShapeChanged();
    }

    // Clic ailleurs : on range.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Conceal();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;
        int w = Scale(BodyWLogical) + 2 * Scale(ShellMetrics.Fillet);
        int h = Scale(BodyHLogical);
        int x = work.Left + (work.Width - w) / 2;  // centré

        var shown = new Rectangle(x, work.Bottom - h, w, h); // collé au bord intérieur du cadre
        var hidden = new Rectangle(x, work.Bottom, w, h);    // sous la zone de travail

        ConfigureReveal(hidden, shown, Rectangle.Empty, Rectangle.Empty);
    }
}
