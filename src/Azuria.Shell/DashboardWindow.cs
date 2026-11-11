using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Le tableau de bord (DA §9) : un tiroir collé au bord haut, qui descend quand le curseur touche le
/// haut d'un écran — sur CET écran, centré et à son DPI — et remonte quand il s'en éloigne.
/// Onglets Accueil, Média, Performances, Météo (web/dashboard.html).
/// </summary>
internal sealed class DashboardWindow : GlassWindow
{
    private const int BodyWLogical = 640;  // DA §9 : 720 au plus
    private const int BodyHLogical = 280;
    private const int RadiusLogical = 18;  // DA §8 : tableau de bord
    private const int TriggerLogical = 3;  // bande de déclenchement en haut

    // Écran courant (celui dont le curseur a touché le haut en dernier).
    private Screen _current = Screen.PrimaryScreen ?? Screen.AllScreens[0];

    public DashboardWindow() : base("dashboard.html") { }

    protected override Screen AnchorScreen => _current;
    protected override DrawerEdge? Shape => DrawerEdge.Top;
    protected override int ShapeRadiusLogical => RadiusLogical;

    // Avant de descendre : se recentrer sur l'écran dont le curseur touche le haut.
    protected override void PrepareReveal(Point cursor)
    {
        _current = Screen.FromPoint(cursor);
        LayoutGeometry();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;                       // écran courant
        int r = Scale(RadiusLogical);
        int w = Scale(BodyWLogical) + 2 * r;       // + les deux congés concaves
        int h = Scale(BodyHLogical);
        int x = work.Left + (work.Width - w) / 2;  // centré

        var shown = new Rectangle(x, work.Top, w, h);      // collé au bord haut
        var hidden = new Rectangle(x, work.Top - h, w, h); // entièrement au-dessus

        // Déclenchement : le haut de TOUS les écrans (on descend sur celui du curseur).
        var vs = SystemInformation.VirtualScreen;
        var trigger = new Rectangle(vs.Left, vs.Top, vs.Width, Scale(TriggerLogical));
        // Maintien : la bande haute de l'écran courant, jusqu'au bas du tiroir.
        var keep = new Rectangle(work.Left, work.Top, work.Width, h);

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
