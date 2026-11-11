namespace Azuria.Shell;

/// <summary>
/// Un panneau latéral (DA §9) : caché hors de l'écran, il glisse depuis le bord gauche ou droit
/// quand le curseur touche ce bord, et se range quand il s'en éloigne. Largeur 380, pleine hauteur
/// sous les marges, à 12 px des bords. Réutilise le glissé de <see cref="GlassWindow"/>.
/// </summary>
internal sealed class SidePanelWindow : GlassWindow
{
    private const int MarginLogical = 12;   // DA §4 : marge haut/bas
    private const int TriggerLogical = 3;   // épaisseur de la bande de déclenchement au bord
    private const int OvershootLogical = 10; // dépassement hors écran côté bord → bord collé et droit

    private readonly bool _right;
    private readonly int _widthLogical;

    public SidePanelWindow(string page, bool right, int widthLogical) : base(page)
    {
        _right = right;
        _widthLogical = widthLogical;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutGeometry();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = Screen.FromHandle(Handle).WorkingArea; // pixels physiques
        int margin = Scale(MarginLogical);
        int visibleW = Scale(_widthLogical);
        int over = Scale(OvershootLogical);
        int width = visibleW + over;   // la fenêtre inclut le dépassement (hors écran côté bord)
        int trig = Scale(TriggerLogical);
        int top = work.Top + margin;
        int height = work.Height - 2 * margin;

        Rectangle shown, hidden, trigger, keep;
        if (!_right)
        {
            // Collé au bord gauche : le dépassement passe à gauche de l'écran, bord gauche droit.
            shown = new Rectangle(work.Left - over, top, width, height);
            hidden = new Rectangle(work.Left - width - margin, top, width, height);
            trigger = new Rectangle(work.Left, work.Top, trig, work.Height);
            keep = new Rectangle(work.Left, work.Top, visibleW, work.Height);
        }
        else
        {
            // Collé au bord droit : le dépassement passe à droite de l'écran, bord droit droit.
            shown = new Rectangle(work.Right - visibleW, top, width, height);
            hidden = new Rectangle(work.Right + margin, top, width, height);
            trigger = new Rectangle(work.Right - trig, work.Top, trig, work.Height);
            keep = new Rectangle(work.Right - visibleW, work.Top, visibleW, work.Height);
        }

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
