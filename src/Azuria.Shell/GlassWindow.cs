using System.Runtime.InteropServices;
using Azuria.Shell.Native;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>
/// Fenêtre de verre d'Azuria : hôte natif sans cadre, toujours au-dessus, hors barre des tâches,
/// avec l'Acrylic de DWM, qui affiche une page de l'UI (HTML/CSS) dans un WebView2 transparent.
/// Aucune UI n'est dessinée ici : WinForms ne sert que de fenêtre. La présentation est dans <c>web/</c>.
/// </summary>
internal class GlassWindow : Form
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly string _page;
    private readonly WebView2 _web = new();
    private bool _blurAvailable = true;

    /// <param name="page">Nom du fichier dans <c>web/</c> (ex. « bar.html »).</param>
    public GlassWindow(string page)
    {
        _page = page;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // Fond noir : dans le cadre étendu par DWM, le noir devient le matériau (verre). Le WebView2
        // transparent laisse ce verre transparaître là où l'UI ne peint rien.
        BackColor = Color.Black;
        KeyPreview = true;

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Color.Transparent; // laisse passer le verre derrière l'UI
        Controls.Add(_web);
    }

    // Fenêtre-outil : hors Alt-Tab et hors barre des tâches.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        Dwm.UseDarkMode(Handle);
        Dwm.ExtendFrameFull(Handle);
        Dwm.SetCorner(Handle, Dwm.Corner.DoNotRound); // on gère nous-mêmes le rayon via l'UI
        _blurAvailable = TransparencyEnabled();
        Dwm.SetAcrylic(Handle, _blurAvailable);

        await InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        // Dossier de données du WebView2 hors du dossier de l'appli (évite d'écrire à côté de l'exe).
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Azuria", "WebView2");
        Directory.CreateDirectory(dataDir);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        string webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        // Sert le dossier web/ sous une origine locale stable (évite les limites de file://).
        core.SetVirtualHostNameToFolderMapping(
            "azuria.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);

        // Confort pour un shell : pas de menu contextuel, pas de raccourcis navigateur.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        ApplyBlurClass();
        core.Navigate($"https://azuria.local/{_page}");
    }

    // ----- repli quand la transparence de Windows est coupée -----

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SETTINGCHANGE)
        {
            bool now = TransparencyEnabled();
            if (now != _blurAvailable)
            {
                _blurAvailable = now;
                Dwm.SetAcrylic(Handle, now);
                ApplyBlurClass();
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>Prévient l'UI de l'état du flou : la classe CSS <c>no-blur</c> déclenche le repli --solid.</summary>
    private void ApplyBlurClass()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        string js = _blurAvailable
            ? "document.documentElement.classList.remove('no-blur')"
            : "document.documentElement.classList.add('no-blur')";
        _ = core.ExecuteScriptAsync(js);
    }

    private static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int v || v != 0;
        }
        catch { return true; }
    }

    /// <summary>Place la fenêtre en coordonnées physiques (pixels écran).</summary>
    public void PlacePhysical(int x, int y, int width, int height)
    {
        Location = new Point(x, y);
        Size = new Size(width, height);
    }
}
