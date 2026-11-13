namespace Azuria.Shell;

/// <summary>
/// Un tiroir latéral pleine hauteur (DA §9), qui sort du cadre au bord gauche ou droit de l'espace
/// des écrans : la barre gauche s'ancre au moniteur le plus à gauche, le panneau droit au plus à
/// droite, ce qui couvre les deux écrans comme une seule zone. Il glisse depuis son bord quand le
/// curseur y touche. Il occupe toute la hauteur intérieure du cadre ; des congés concaves le
/// raccordent aux bandes haute et basse du cadre (dont les coins s'effacent pendant ce temps).
/// </summary>
internal sealed class SidePanelWindow : GlassWindow
{
    private const int TriggerLogical = 3; // épaisseur de la bande de déclenchement au bord

    private readonly bool _right;
    private readonly int _bodyWLogical;

    public SidePanelWindow(string page, bool right, int widthLogical) : base(page)
    {
        _right = right;
        _bodyWLogical = widthLogical;
    }

    public override Screen AnchorScreen => _right
        ? Screen.AllScreens.OrderByDescending(s => s.Bounds.Right).First()
        : Screen.AllScreens.OrderBy(s => s.Bounds.Left).First();

    public override DrawerEdge Edge => _right ? DrawerEdge.Right : DrawerEdge.Left;

    protected override Padding ContentInsets => _right
        ? new Padding(ShellMetrics.Fillet, 0, 0, 0)
        : new Padding(0, 0, ShellMetrics.Fillet, 0);

    protected override void BuildShape(GlassShape s, int w, int h)
    {
        int r = Scale(ShellMetrics.Fillet);
        int body = w - r;
        if (!_right)
        {
            s.Rectangle(0, 0, body, h)
             .Concave(body, 0, r, body + r, r)          // raccord à la bande haute du cadre
             .Concave(body, h - r, r, body + r, h - r); // raccord à la bande basse
        }
        else
        {
            s.Rectangle(r, 0, body, h)
             .Concave(0, 0, r, 0, r)
             .Concave(0, h - r, r, 0, h - r);
        }
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;                // intérieur du cadre
        var screen = AnchorScreen.Bounds;
        int body = Scale(_bodyWLogical);
        int w = body + Scale(ShellMetrics.Fillet);
        int h = work.Height;
        int trig = Scale(TriggerLogical);

        Rectangle shown, hidden, trigger, keep;
        if (!_right)
        {
            shown = new Rectangle(work.Left, work.Top, w, h);
            hidden = new Rectangle(screen.Left - w, work.Top, w, h);
            trigger = new Rectangle(screen.Left, work.Top, trig, h);
            keep = Rectangle.FromLTRB(screen.Left, work.Top, work.Left + body, work.Bottom);
        }
        else
        {
            shown = new Rectangle(work.Right - w, work.Top, w, h);
            hidden = new Rectangle(screen.Right, work.Top, w, h);
            trigger = new Rectangle(screen.Right - trig, work.Top, trig, h);
            keep = Rectangle.FromLTRB(work.Right - body, work.Top, screen.Right, work.Bottom);
        }

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
