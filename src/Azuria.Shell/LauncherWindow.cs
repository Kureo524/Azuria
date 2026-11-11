using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Le lanceur (DA §9) : un tiroir collé au bord bas, qui monte au clic sur le logo Azuria de la barre,
/// centré sur l'écran du curseur. Pas de révélation au survol (le bas de l'écran est occupé par la
/// barre des tâches de Windows). On y tape : il prend le focus, et se range sur Échap ou dès qu'on
/// clique ailleurs.
/// </summary>
internal sealed class LauncherWindow : GlassWindow
{
    private const int BodyWLogical = 560;
    private const int BodyHLogical = 380;
    private const int RadiusLogical = 18; // DA §8 : lanceur

    private Screen _current = Screen.PrimaryScreen ?? Screen.AllScreens[0];

    public LauncherWindow() : base("launcher.html") { }

    protected override Screen AnchorScreen => _current;
    protected override DrawerEdge? Shape => DrawerEdge.Bottom;
    protected override int ShapeRadiusLogical => RadiusLogical;
    protected override bool NoActivate => false;  // on tape dans la recherche
    protected override bool HoverDriven => false; // ouvert sur commande

    /// <summary>Ouvre le lanceur sur l'écran du point donné, ou le range s'il est ouvert.</summary>
    public void Toggle(Point at)
    {
        if (IsShown) { Conceal(); return; }
        _current = Screen.FromPoint(at);
        LayoutGeometry();
        Reveal();
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
        int r = Scale(RadiusLogical);
        int w = Scale(BodyWLogical) + 2 * r;       // + les deux congés concaves
        int h = Scale(BodyHLogical);
        int x = work.Left + (work.Width - w) / 2;  // centré

        var shown = new Rectangle(x, work.Bottom - h, w, h); // collé au bas de la zone de travail
        var hidden = new Rectangle(x, work.Bottom, w, h);    // sous le bord

        ConfigureReveal(hidden, shown, Rectangle.Empty, Rectangle.Empty);
    }
}
