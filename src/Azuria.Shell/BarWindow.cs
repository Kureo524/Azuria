namespace Azuria.Shell;

/// <summary>
/// Le panneau haut d'Azuria, façon Caelestia : caché au-dessus du bord haut, il descend quand le
/// curseur touche le haut de l'écran, et remonte quand il s'en éloigne. Centré, collé au bord haut.
/// Il apparaît sur l'écran dont le curseur touche le haut (pas un écran fixe) : le déclenchement
/// couvre le haut de tous les écrans, et la barre se recentre sur celui du curseur avant de descendre.
/// </summary>
internal sealed class BarWindow : GlassWindow
{
    private const int WidthLogical = 560;    // moins large que l'écran
    private const int HeightLogical = 96;    // descend plus bas qu'une simple barre
    private const int TriggerLogical = 3;    // épaisseur de la bande de déclenchement en haut
    private const int OvershootLogical = 10; // dépassement hors écran → bord haut droit et collé

    // Écran courant de la barre (celui dont le curseur a touché le haut en dernier).
    private Screen _current = Screen.PrimaryScreen ?? Screen.AllScreens[0];

    public BarWindow() : base("bar.html") { }

    protected override Screen AnchorScreen => _current;

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutGeometry();
    }

    // Avant de descendre : se recentrer sur l'écran dont le curseur touche le haut.
    protected override void PrepareReveal(System.Drawing.Point cursor)
    {
        _current = Screen.FromPoint(cursor);
        LayoutGeometry();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea; // = écran courant (AnchorScreen)
        int width = Scale(WidthLogical);
        int over = Scale(OvershootLogical);
        int height = Scale(HeightLogical) + over; // le haut arrondi dépasse au-dessus de l'écran
        int x = work.Left + (work.Width - width) / 2; // centré sur l'écran courant

        var shown = new Rectangle(x, work.Top - over, width, height);          // collé au bord haut
        var hidden = new Rectangle(x, work.Top - height - over, width, height); // entièrement au-dessus

        // Déclenchement : le haut de TOUS les écrans (on révèle sur celui du curseur).
        var vs = SystemInformation.VirtualScreen;
        var trigger = new Rectangle(vs.Left, vs.Top, vs.Width, Scale(TriggerLogical));
        // Maintien : la bande haute de l'écran courant, du bord jusqu'au bas de la barre.
        var keep = new Rectangle(work.Left, work.Top, work.Width, shown.Bottom - work.Top);

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
