namespace Azuria.Shell;

/// <summary>
/// Le panneau haut d'Azuria, façon Caelestia : caché au-dessus du bord haut, il descend quand le
/// curseur touche le haut de l'écran (au centre, là où il apparaît), et remonte quand il s'en
/// éloigne. Centré, plus étroit que l'écran et un peu haut. Il ne fait que glisser verticalement.
/// </summary>
internal sealed class BarWindow : GlassWindow
{
    private const int MarginTopLogical = 12; // DA §4 : marge avec le bord haut
    private const int WidthLogical = 560;    // moins large que l'écran
    private const int HeightLogical = 96;    // descend plus bas qu'une simple barre
    private const int TriggerLogical = 3;    // épaisseur de la bande de déclenchement en haut

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
        int margin = Scale(MarginTopLogical);
        int width = Scale(WidthLogical);
        int height = Scale(HeightLogical);
        int x = work.Left + (work.Width - width) / 2; // centré

        var shown = new Rectangle(x, work.Top + margin, width, height);
        var hidden = new Rectangle(x, work.Top - height - margin, width, height); // entièrement au-dessus

        // Déclenchement : une fine bande en haut, au-dessus du panneau (là où il descend).
        var trigger = new Rectangle(x, work.Top, width, Scale(TriggerLogical));
        // Maintien : du bord haut jusqu'au bas du panneau révélé, sur sa largeur (pas d'oscillation).
        var keep = new Rectangle(x, work.Top, width, shown.Bottom - work.Top);

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
