namespace Azuria.Shell;

/// <summary>
/// Le tableau de bord (DA §9) : un tiroir qui sort de la bande haute du cadre quand le curseur touche
/// le haut d'un écran — sur CET écran, centré et à son DPI — et remonte quand il s'en éloigne.
/// Congés concaves en haut, coins arrondis en bas. Onglets Accueil, Média, Performances, Météo
/// (web/dashboard.html).
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

    public override Screen AnchorScreen => _current;
    public override DrawerEdge Edge => DrawerEdge.Top;

    protected override Padding ContentInsets => new(ShellMetrics.Fillet, 0, ShellMetrics.Fillet, 0);

    protected override void BuildShape(GlassShape s, int w, int h)
    {
        int f = Scale(ShellMetrics.Fillet);
        int body = w - 2 * f;
        s.Body(f, 0, body, h, Scale(RadiusLogical), false, false, true, true)
         .Concave(0, 0, f, 0, f)
         .Concave(f + body, 0, f, w, f);
    }

    // Avant de descendre : se recentrer sur l'écran dont le curseur touche le haut.
    protected override void PrepareReveal(Point cursor)
    {
        _current = Screen.FromPoint(cursor);
        LayoutGeometry();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;                       // intérieur du cadre, écran courant
        var screen = AnchorScreen.Bounds;
        int w = Scale(BodyWLogical) + 2 * Scale(ShellMetrics.Fillet);
        int h = Scale(BodyHLogical);
        int x = work.Left + (work.Width - w) / 2;  // centré

        var shown = new Rectangle(x, work.Top, w, h);       // collé au bord intérieur du cadre
        var hidden = new Rectangle(x, screen.Top - h, w, h); // entièrement au-dessus de l'écran

        // Déclenchement : le haut de TOUS les écrans (on descend sur celui du curseur).
        var vs = SystemInformation.VirtualScreen;
        var trigger = new Rectangle(vs.Left, vs.Top, vs.Width, Scale(TriggerLogical));
        // Maintien : la bande haute de l'écran courant, jusqu'au bas du tiroir.
        var keep = Rectangle.FromLTRB(work.Left, screen.Top, work.Right, shown.Bottom);

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
