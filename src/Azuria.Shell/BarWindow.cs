namespace Azuria.Shell;

/// <summary>
/// La barre d'Azuria, en « dynamic island » : au repos, une petite pastille centrée en haut qui
/// ne montre que l'heure ; au survol, elle s'agrandit pour révéler les bureaux (gauche) et la zone
/// d'état (droite). (Écart assumé avec la DA §9/§11, acté — voir docs/contexte.md §3.)
/// </summary>
internal sealed class BarWindow : GlassWindow
{
    private const int MarginTopLogical = 12; // DA §4 : marge avec le bord de l'écran

    // États (tailles logiques). Replié : juste l'heure. Étendu : barre complète centrée.
    private const int CollapsedWLogical = 136;
    private const int CollapsedHLogical = 30;
    private const int ExpandedWLogical = 560;
    private const int ExpandedHLogical = 40;

    public BarWindow() : base("bar.html") { }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutGeometry();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = Screen.FromHandle(Handle).WorkingArea; // pixels physiques
        int centerX = work.Left + work.Width / 2;
        int top = work.Top + Scale(MarginTopLogical);

        Rectangle Centered(int wLogical, int hLogical)
        {
            int w = Scale(wLogical), h = Scale(hLogical);
            return new Rectangle(centerX - w / 2, top, w, h);
        }

        ConfigureGeometry(
            collapsed: Centered(CollapsedWLogical, CollapsedHLogical),
            expanded: Centered(ExpandedWLogical, ExpandedHLogical));
    }
}
