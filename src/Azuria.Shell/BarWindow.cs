namespace Azuria.Shell;

/// <summary>
/// La barre d'Azuria (DA §9), façon Caelestia : cachée au-dessus du bord haut, elle descend quand le
/// curseur touche le haut de l'écran, et remonte quand il s'en éloigne. Pleine largeur, hauteur 40,
/// rayon 14, à 12 px des bords. Elle ne fait que glisser : rien ne bouge à l'intérieur.
/// </summary>
internal sealed class BarWindow : GlassWindow
{
    private const int MarginLogical = 12;   // DA §4 : marge avec les bords de l'écran
    private const int HeightLogical = 40;   // DA §9 : hauteur de la barre
    private const int TriggerLogical = 3;   // épaisseur de la bande de déclenchement en haut

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
        int margin = Scale(MarginLogical);
        int height = Scale(HeightLogical);
        int width = work.Width - 2 * margin;
        int x = work.Left + margin;

        var shown = new Rectangle(x, work.Top + margin, width, height);
        // Cachée entièrement au-dessus du bord haut de l'écran.
        var hidden = new Rectangle(x, work.Top - height - margin, width, height);

        // Déclenchement : une fine bande sur tout le bord haut de l'écran.
        var trigger = new Rectangle(work.Left, work.Top, work.Width, Scale(TriggerLogical));
        // Maintien : du bord haut jusqu'au bas de la barre révélée (pour ne pas osciller quand le
        // curseur est au-dessus de la barre, dans la marge).
        int keepBottom = shown.Bottom;
        var keep = new Rectangle(work.Left, work.Top, work.Width, keepBottom - work.Top);

        ConfigureReveal(hidden, shown, trigger, keep);
    }
}
