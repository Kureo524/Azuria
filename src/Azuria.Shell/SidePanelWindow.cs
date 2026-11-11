using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Un tiroir latéral pleine hauteur (DA §9), collé au bord gauche ou droit de l'espace des écrans :
/// le rail gauche s'ancre au moniteur le plus à gauche, le panneau droit au plus à droite, ce qui
/// couvre les deux écrans comme une seule zone. Il glisse depuis son bord quand le curseur y touche.
/// Congés concaves en haut et en bas du côté libre (le tiroir semble sortir du bord).
/// </summary>
internal sealed class SidePanelWindow : GlassWindow
{
    private const int TriggerLogical = 3; // épaisseur de la bande de déclenchement au bord

    private readonly bool _right;
    private readonly int _bodyWLogical;
    private readonly int _radiusLogical;

    public SidePanelWindow(string page, bool right, int widthLogical, int radiusLogical = 14) : base(page)
    {
        _right = right;
        _bodyWLogical = widthLogical;
        _radiusLogical = radiusLogical;
    }

    protected override Screen AnchorScreen => _right
        ? Screen.AllScreens.OrderByDescending(s => s.Bounds.Right).First()
        : Screen.AllScreens.OrderBy(s => s.Bounds.Left).First();

    protected override DrawerEdge? Shape => _right ? DrawerEdge.Right : DrawerEdge.Left;
    protected override int ShapeRadiusLogical => _radiusLogical;

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;
        int r = Scale(_radiusLogical);
        int w = Scale(_bodyWLogical) + r;  // + les congés côté libre
        int h = work.Height;               // pleine hauteur
        int trig = Scale(TriggerLogical);

        Rectangle shown, hidden, trigger, keep;
        if (!_right)
        {
            shown = new Rectangle(work.Left, work.Top, w, h);
            hidden = new Rectangle(work.Left - w, work.Top, w, h);
            trigger = new Rectangle(work.Left, work.Top, trig, h);
            keep = new Rectangle(work.Left, work.Top, w, h);
        }
        else
        {
            shown = new Rectangle(work.Right - w, work.Top, w, h);
            hidden = new Rectangle(work.Right, work.Top, w, h);
            trigger = new Rectangle(work.Right - trig, work.Top, trig, h);
            keep = new Rectangle(work.Right - w, work.Top, w, h);
        }

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
