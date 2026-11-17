using System.Text.Json;
using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// Les réglages d'Azuria (DA §9) : un panneau de verre flottant, rayon 22, centré sur l'écran du
/// curseur, avec une colonne de navigation à gauche. Il s'ouvre sur commande (panneau droit), prend le
/// focus, et se range sur Échap ou dès qu'on clique ailleurs. Chaque réglage s'applique tout de suite,
/// là où il est affiché.
///
/// Écart assumé avec la DA, qui demande une « vraie fenêtre » (Alt-Tab, déplaçable) : le verre d'Azuria
/// est dessiné par la surface de l'écran, qui ne sait poser que des tiroirs au-dessus des applis.
/// </summary>
internal sealed class SettingsWindow : GlassWindow
{
    private const int BodyWLogical = 760;
    private const int BodyHLogical = 520;
    private const int RadiusLogical = 22;   // DA §8 : fenêtres
    private const int RiseLogical = 12;     // DA §11 : glissé de 12 à l'ouverture

    private Screen _current = Screen.PrimaryScreen ?? Screen.AllScreens[0];

    public SettingsWindow() : base("settings.html")
    {
        MessageReceived += OnMessage;
        Settings.Changed += () => { if (Visible) Send(); };
    }

    public override Screen AnchorScreen => _current;
    protected override bool NoActivate => false;   // Échap, et le focus pour les contrôles
    protected override bool HoverDriven => false;  // ouvert sur commande

    protected override void BuildShape(GlassShape s, int w, int h) =>
        s.Body(0, 0, w, h, Scale(RadiusLogical), true, true, true, true);

    /// <summary>Ouvre les réglages sur l'écran du point donné.</summary>
    public void Open(Point at)
    {
        if (IsShown) return;
        _current = Screen.FromPoint(at);
        LayoutGeometry();
        Reveal();
        Send();
    }

    // Clic ailleurs : on range.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Conceal();
    }

    protected override void LayoutGeometry()
    {
        if (!IsHandleCreated) return;
        var work = WorkArea;
        int w = Math.Min(Scale(BodyWLogical), work.Width - 2 * Scale(ShellMetrics.Fillet));
        int h = Math.Min(Scale(BodyHLogical), work.Height - 2 * Scale(ShellMetrics.Fillet));
        var shown = new Rectangle(work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h);
        var hidden = shown with { Y = shown.Y + Scale(RiseLogical) };
        ConfigureReveal(hidden, shown, Rectangle.Empty, Rectangle.Empty);
    }

    private void OnMessage(string msg)
    {
        string[] p = msg.Split(':', 2);
        switch (p[0])
        {
            case "ready": Send(); return;
            case "accent":
                if (Settings.Accents.Any(a => a.Id == p.ElementAtOrDefault(1)))
                    Settings.Update(s => s.Accent = p[1]);
                return;
            case "startup": Settings.LaunchAtStartup = p.ElementAtOrDefault(1) == "1"; return;
            case "tiling": Settings.Update(s => s.Tiling = p.ElementAtOrDefault(1) == "1"); return;
            case "focus": Settings.Update(s => s.FocusFollowsMouse = p.ElementAtOrDefault(1) == "1"); return;
        }
    }

    private void Send()
    {
        var s = Settings.Current;
        PostJson(JsonSerializer.Serialize(new
        {
            type = "settings",
            accents = Settings.Accents.Select(a => new { id = a.Id, name = a.Name, hex = a.Hex }),
            accent = s.Accent,
            startup = Settings.LaunchAtStartup,
            tiling = s.Tiling,
            focus = s.FocusFollowsMouse,
            version = Application.ProductVersion.Split('+')[0],
        }));
    }
}
