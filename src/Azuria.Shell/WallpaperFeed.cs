using System.Text.Json;
using Azuria.Shell.Native;

namespace Azuria.Shell;

/// <summary>
/// La commande <c>&gt;wallpaper</c> du lanceur, côté C#. La page demande la liste des images, puis :
/// <list type="bullet">
/// <item>survol ou flèches : <b>aperçu</b>, le fond change sans rien enregistrer ;</item>
/// <item>Entrée ou clic : <b>validation</b>, le fond est posé sur tous les écrans et tous les bureaux ;</item>
/// <item>Échap, clic ailleurs ou sortie de la commande : <b>retour</b> au fond d'avant l'aperçu.</item>
/// </list>
/// Le retour est fait ici, à la fermeture du lanceur quelle qu'en soit la cause : un aperçu ne reste
/// jamais posé par accident.
///
/// Windows 11 garde un fond par bureau virtuel : au changement de bureau, le fond choisi est reposé
/// s'il n'y est pas (bureau créé après coup, ou que l'Explorateur n'a pas relu).
/// </summary>
internal sealed class WallpaperFeed : IDisposable
{
    private static readonly string ChoiceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Azuria", "fond-d-ecran.txt");

    private readonly GlassWindow _launcher;
    private readonly System.Windows.Forms.Timer _desktopWatch = new() { Interval = 500 };
    private HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase); // images de la dernière lecture
    private Dictionary<string, string>? _before;  // fond de chaque écran avant l'aperçu
    private bool _previewing;
    private string? _wanted;                      // dernier aperçu demandé (les autres sont sautés)
    private bool _busy;
    private int _desktop = -1;
    private bool _disposed;

    public WallpaperFeed(GlassWindow launcher)
    {
        _launcher = launcher;
        launcher.MessageReceived += OnMessage;
        launcher.PresenceChanged += (_, present) => { if (!present) Revert(); };

        _desktop = VirtualDesktops.State().Current;
        _desktopWatch.Tick += (_, _) => OnDesktopTick();
        _desktopWatch.Start();
    }

    private void OnMessage(string msg)
    {
        if (msg == "wallpapers") SendList();
        else if (msg.StartsWith("wallpaper-preview:")) Preview(msg[18..]);
        else if (msg.StartsWith("wallpaper:")) Commit(msg[10..]);
        else if (msg == "wallpaper-revert") Revert();
    }

    private async void SendList()
    {
        string folder = Wallpapers.Folder;
        var items = await Wallpapers.ScanAsync(folder);
        var now = await Wallpapers.SnapshotAsync();
        if (_disposed) return;
        _allowed = items.Select(w => w.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!_previewing) _before = now; // point de retour : le fond tel qu'il est à l'ouverture
        var current = (_before ?? now).Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _launcher.PostJson(JsonSerializer.Serialize(new
        {
            type = "wallpapers",
            folder,
            missing = !Directory.Exists(folder),
            items = items.Select(w => new { path = w.Path, name = w.Name, thumb = w.Thumb, current = current.Contains(w.Path) }),
        }));
    }

    // Les aperçus se suivent vite (survol) : un seul est posé à la fois, et seul le dernier demandé
    // est posé ensuite.
    private async void Preview(string path)
    {
        // Lanceur déjà rangé (aperçu parti juste avant la fermeture) : rien n'est posé.
        if (!_launcher.IsShown || _before is null || !_allowed.Contains(path) || !File.Exists(path)) return;
        _previewing = true;
        _wanted = path;
        if (_busy) return;
        _busy = true;
        try
        {
            while (_previewing && _wanted is string p)
            {
                _wanted = null;
                await Wallpapers.PreviewAsync(p);
                if (_disposed) return;
            }
        }
        finally { _busy = false; }
    }

    private async void Commit(string path)
    {
        if (!_allowed.Contains(path) || !File.Exists(path)) return;
        _previewing = false;
        _wanted = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ChoiceFile)!);
            File.WriteAllText(ChoiceFile, path);
        }
        catch { /* le choix vaut pour la session, sans être repris sur les nouveaux bureaux */ }
        var now = await Wallpapers.CommitAsync(path);
        if (_disposed) return;
        _before = now;
        SendCurrent(now);
    }

    // Retour au fond d'avant l'aperçu (passe après l'aperçu en cours : le fil est dans l'ordre).
    private async void Revert()
    {
        if (!_previewing || _before is null) return;
        _previewing = false;
        _wanted = null;
        var now = await Wallpapers.RestoreAsync(_before);
        if (_disposed) return;
        SendCurrent(now);
    }

    private void SendCurrent(Dictionary<string, string> now) =>
        _launcher.PostJson(JsonSerializer.Serialize(new { type = "wallpaper-current", paths = now.Values.Distinct() }));

    // Changement de bureau virtuel : le fond choisi par >wallpaper le suit.
    private async void OnDesktopTick()
    {
        int current = VirtualDesktops.State().Current;
        if (current == _desktop) return;
        _desktop = current;
        if (_previewing) return;
        string? choice = null;
        try { if (File.Exists(ChoiceFile)) choice = File.ReadAllText(ChoiceFile).Trim(); }
        catch { return; }
        if (string.IsNullOrEmpty(choice)) return;
        await Wallpapers.EnsureAsync(choice);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _desktopWatch.Dispose();
        // Azuria quitte pendant un aperçu : on rend le fond d'avant (sans attendre la fin).
        if (_previewing && _before is not null) _ = Wallpapers.RestoreAsync(_before);
        _disposed = true;
    }
}
