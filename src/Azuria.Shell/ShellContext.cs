namespace Azuria.Shell;

/// <summary>
/// Le contexte de l'application : tient toutes les fenêtres du shell et garde la boucle de
/// messages vivante tant qu'elles existent. Organisation (inspirée de Caelestia, réécrite) :
/// barre verticale à gauche, tableau de bord en haut, lanceur en bas, panneau à droite.
/// </summary>
internal sealed class ShellContext : ApplicationContext
{
    private readonly List<Form> _windows = new();

    public ShellContext()
    {
        var bar = new SidePanelWindow("left.html", right: false, widthLogical: 56);       // barre principale
        var dashboard = new DashboardWindow();                                            // haut
        var sidebar = new SidePanelWindow("right.html", right: true, widthLogical: 200);  // réglages + notifs
        var launcher = new LauncherWindow();                                              // bas

        // Le logo Azuria de la barre ouvre / range le lanceur, sur l'écran du curseur.
        bar.MessageReceived += msg =>
        {
            if (msg == "launcher") launcher.Toggle(Cursor.Position);
        };

        Add(bar);
        Add(dashboard);
        Add(sidebar);
        Add(launcher);
        foreach (var w in _windows) w.Show();
    }

    private void Add(Form w)
    {
        w.FormClosed += (_, _) =>
        {
            _windows.Remove(w);
            if (_windows.Count == 0) ExitThread();
        };
        _windows.Add(w);
    }
}
