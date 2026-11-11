namespace Azuria.Shell;

/// <summary>
/// Le contexte de l'application : tient toutes les fenêtres du shell (panneau haut + panneaux
/// latéraux) et garde la boucle de messages vivante tant qu'elles existent.
/// </summary>
internal sealed class ShellContext : ApplicationContext
{
    private readonly List<Form> _windows = new();

    public ShellContext()
    {
        Add(new BarWindow());
        Add(new SidePanelWindow("left.html", right: false, widthLogical: 56));  // rail d'icônes fin
        Add(new SidePanelWindow("right.html", right: true, widthLogical: 380)); // réglages + notifs

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
