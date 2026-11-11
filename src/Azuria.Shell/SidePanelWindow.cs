namespace Azuria.Shell;

/// <summary>
/// Un panneau latéral (DA §9) : caché hors de l'écran, il glisse depuis le bord gauche ou droit
/// quand le curseur touche ce bord, et se range quand il s'en éloigne. Largeur 380, pleine hauteur
/// sous les marges, à 12 px des bords. Réutilise le glissé de <see cref="GlassWindow"/>.
/// </summary>
internal sealed class SidePanelWindow : GlassWindow
{
    private const int MarginLogical = 12;  // DA §4 : marge avec les bords
    private const int WidthLogical = 380;  // DA §9 : largeur des panneaux
    private const int TriggerLogical = 3;  // épaisseur de la bande de déclenchement au bord

    private readonly bool _right;

    public SidePanelWindow(string page, bool right) : base(page) => _right = right;

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
        int width = Scale(WidthLogical);
        int trig = Scale(TriggerLogical);
        int top = work.Top + margin;
        int height = work.Height - 2 * margin;

        Rectangle shown, hidden, trigger, keep;
        if (!_right)
        {
            shown = new Rectangle(work.Left + margin, top, width, height);
            hidden = new Rectangle(work.Left - width - margin, top, width, height);
            trigger = new Rectangle(work.Left, work.Top, trig, work.Height);
            keep = new Rectangle(work.Left, work.Top, shown.Right - work.Left, work.Height);
        }
        else
        {
            shown = new Rectangle(work.Right - margin - width, top, width, height);
            hidden = new Rectangle(work.Right + margin, top, width, height);
            trigger = new Rectangle(work.Right - trig, work.Top, trig, work.Height);
            keep = new Rectangle(shown.Left, work.Top, work.Right - shown.Left, work.Height);
        }

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
