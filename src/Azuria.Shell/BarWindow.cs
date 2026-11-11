namespace Azuria.Shell;

/// <summary>
/// La barre d'Azuria (DA §9) : en haut, sur la largeur de l'écran, hauteur 40, rayon 14,
/// à 12 px des bords. Pour le squelette, elle affiche <c>bar.html</c> ; le contenu viendra ensuite.
/// </summary>
internal sealed class BarWindow : GlassWindow
{
    private const int MarginLogical = 12; // DA §4 : marge de 12 avec les bords de l'écran
    private const int HeightLogical = 40; // DA §9 : hauteur de la barre

    public BarWindow() : base("bar.html") { }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PlaceAsBar();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PlaceAsBar();
    }

    private void PlaceAsBar()
    {
        var work = Screen.FromHandle(Handle).WorkingArea; // en pixels physiques (DPI par moniteur)
        int margin = Scale(MarginLogical);
        int height = Scale(HeightLogical);
        PlacePhysical(
            work.Left + margin,
            work.Top + margin,
            work.Width - 2 * margin,
            height);
    }

    /// <summary>Convertit une taille logique (px à 100 %) en pixels physiques pour cet écran.</summary>
    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);
}
