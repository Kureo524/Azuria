using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Azuria.Shell.Native;

/// <summary>Une entrée du lanceur : une application ou un jeu installé.</summary>
internal sealed record CatalogEntry(string Id, string Name, string Sub, string Icon, bool Game, Action Launch);

/// <summary>
/// Ce que le lanceur peut ouvrir : les applications du menu Démarrer de Windows (dossier
/// <c>shell:AppsFolder</c>, applis classiques et du Store) et les jeux installés par Steam, Epic Games
/// et Riot Games, lus dans les fichiers que ces launchers tiennent à jour. Un jeu se lance par son
/// launcher, qui s'ouvre en arrière-plan (Steam <c>-silent</c>, Epic <c>silent=true</c>, client Riot).
///
/// Lecture de fichiers et du registre uniquement ; rien n'est injecté ni lu dans un processus
/// (CLAUDE.md). Le balayage tourne sur un fil à part (STA, pour le shell) : il ne bloque pas l'interface.
/// Les icônes sont mises en cache en PNG dans <c>%LOCALAPPDATA%\Azuria\Icones</c>, servies à la page
/// par <c>https://icones.azuria.local/</c>.
/// </summary>
internal sealed class Catalog
{
    public static readonly string IconDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Azuria", "Icones");
    public const string IconHost = "icones.azuria.local";
    private const int IconPx = 64;

    private readonly SynchronizationContext _ui;
    private Dictionary<string, CatalogEntry> _entries = new();
    private DateTime _scanned = DateTime.MinValue;
    private bool _scanning;

    /// <summary>Un balayage vient de finir (sur le fil d'interface).</summary>
    public event Action? Changed;

    public Catalog()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Directory.CreateDirectory(IconDir);
    }

    public IReadOnlyCollection<CatalogEntry> Entries => _entries.Values;

    /// <summary>Relance un balayage si le dernier date de plus de <paramref name="maxAge"/>.</summary>
    public void Refresh(TimeSpan maxAge)
    {
        if (_scanning || DateTime.UtcNow - _scanned < maxAge) return;
        _scanning = true;
        var t = new Thread(() =>
        {
            List<CatalogEntry> found;
            try { found = Scan(); }
            catch { found = new(); }
            _ui.Post(_ =>
            {
                _entries = found.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
                _scanned = DateTime.UtcNow;
                _scanning = false;
                Changed?.Invoke();
            }, null);
        }) { IsBackground = true, Name = "Azuria.Catalog" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    /// <summary>Lance l'entrée demandée par la page. Faux si elle n'existe plus.</summary>
    public bool Launch(string id)
    {
        if (!_entries.TryGetValue(id, out var e)) return false;
        try { e.Launch(); return true; }
        catch { return false; }
    }

    // ----- balayage -----

    private static List<CatalogEntry> Scan()
    {
        var games = new List<CatalogEntry>();
        foreach (var source in new Func<IEnumerable<CatalogEntry>>[] { Riot, Epic, Steam })
        {
            try { games.AddRange(source()); }
            catch { /* launcher absent ou fichier illisible : on passe */ }
        }
        // Les jeux ont souvent aussi un raccourci dans le menu Démarrer : on garde l'entrée « jeu ».
        var gameNames = games.Select(g => Key(g.Name)).ToHashSet();
        var apps = Apps().Where(a => !gameNames.Contains(Key(a.Name)));
        return games.Concat(apps).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string Key(string name) => Regex.Replace(name.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    // Ce qui n'est pas une appli à ouvrir : désinstalleurs, aides, sites web…
    private static readonly Regex Noise = new(
        @"(d[ée]sinstall|uninstall|\breadme\b|\blisez-moi\b|\bdocumentation\b|\bhelp\b|\baide\b|site web|website|release notes)",
        RegexOptions.IgnoreCase);

    private static IEnumerable<CatalogEntry> Apps()
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) yield break;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        foreach (dynamic item in folder.Items())
        {
            string name = item.Name, path = item.Path;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || Noise.IsMatch(name)) continue;
            string parsing = @"shell:AppsFolder\" + path;
            yield return new CatalogEntry("app:" + path, name, "Application", IconFor(parsing), false,
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parsing}\"") { UseShellExecute = true }));
        }
    }

    // Riot : un dossier par produit installé dans ProgramData\Riot Games\Metadata (ex.
    // league_of_legends.live), lancé par le client Riot comme le fait son propre raccourci.
    private static IEnumerable<CatalogEntry> Riot()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games");
        string installs = Path.Combine(root, "RiotClientInstalls.json");
        if (!File.Exists(installs)) yield break;
        using var doc = JsonDocument.Parse(File.ReadAllText(installs));
        string? client = doc.RootElement.TryGetProperty("rc_default", out var rc) ? rc.GetString() : null;
        if (client is null || !File.Exists(client)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(root, "Metadata")))
        {
            string id = Path.GetFileName(dir);          // produit.patchline
            int dot = id.LastIndexOf('.');
            if (dot <= 0) continue;
            string settings = Path.Combine(dir, id + ".product_settings.yaml");
            if (!File.Exists(settings)) continue;
            string text = File.ReadAllText(settings);
            string? install = Yaml(text, "product_install_full_path");
            if (install is null || !Directory.Exists(install)) continue;
            string name = Yaml(text, "shortcut_name") is string s ? Path.GetFileNameWithoutExtension(s) : id[..dot];
            string product = id[..dot], patchline = id[(dot + 1)..];
            string ico = Path.Combine(dir, id + ".ico");
            yield return new CatalogEntry("riot:" + id, name, "Jeu · Riot Games", IconFor(File.Exists(ico) ? ico : client), true,
                () => Process.Start(new ProcessStartInfo(client, $"--launch-product={product} --launch-patchline={patchline}") { UseShellExecute = false }));
        }

        static string? Yaml(string text, string key)
        {
            var m = Regex.Match(text, "^" + key + @":\s*""?([^""\r\n]+)""?", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
    }

    // Epic : un manifeste JSON par jeu installé (ProgramData\Epic\EpicGamesLauncher\Data\Manifests).
    private static IEnumerable<CatalogEntry> Epic()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            CatalogEntry? entry = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                if (r.TryGetProperty("AppCategories", out var cats) &&
                    !cats.EnumerateArray().Any(c => c.GetString() == "games")) continue;
                // Un DLC ou un module appartient à un autre jeu : seul le jeu principal est listé.
                if (r.TryGetProperty("MainGameAppName", out var main) && main.GetString() is string m &&
                    m.Length > 0 && m != Str(r, "AppName")) continue;

                string name = Str(r, "DisplayName"), app = Str(r, "AppName");
                string ns = Str(r, "CatalogNamespace"), item = Str(r, "CatalogItemId");
                string exe = Path.GetFullPath(Path.Combine(Str(r, "InstallLocation"), Str(r, "LaunchExecutable")));
                if (name.Length == 0 || app.Length == 0) continue;
                string url = $"com.epicgames.launcher://apps/{Uri.EscapeDataString($"{ns}:{item}:{app}")}?action=launch&silent=true";
                // L'exécutable lancé est souvent un amorceur sans icône : celle du jeu est sur le
                // binaire « -Shipping » d'Unreal, à côté, quand il existe.
                string? shipping = Directory.Exists(Path.GetDirectoryName(exe))
                    ? Directory.EnumerateFiles(Path.GetDirectoryName(exe)!, "*-Shipping.exe").FirstOrDefault()
                    : null;
                string icon = shipping is not null ? IconFor(shipping) : File.Exists(exe) ? IconFor(exe) : "";
                entry = new CatalogEntry("epic:" + app, name, "Jeu · Epic Games", icon, true,
                    () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
            }
            catch { continue; }
            if (entry is not null) yield return entry;
        }

        static string Str(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    // Steam : bibliothèques listées dans steamapps\libraryfolders.vdf, un appmanifest_<id>.acf par jeu.
    private static readonly HashSet<string> SteamTools = new() { "228980" /* Steamworks Common Redistributables */ };

    private static IEnumerable<CatalogEntry> Steam()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? steamPath = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        string? steamExe = (key?.GetValue("SteamExe") as string)?.Replace('/', '\\');
        if (steamPath is null || !Directory.Exists(steamPath)) yield break;
        if (steamExe is null || !File.Exists(steamExe)) steamExe = Path.Combine(steamPath, "steam.exe");

        var libraries = new List<string> { steamPath };
        string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

        var seen = new HashSet<string>();
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text = File.ReadAllText(acf);
                string? id = Vdf(text, "appid"), name = Vdf(text, "name");
                if (id is null || name is null || SteamTools.Contains(id) || !seen.Add(id)) continue;
                if (name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)) continue;
                string exe = steamExe;
                yield return new CatalogEntry("steam:" + id, name, "Jeu · Steam", IconFor(SteamIcon(steamPath, id) ?? exe), true,
                    () => Process.Start(new ProcessStartInfo(exe, $"-silent steam://rungameid/{id}") { UseShellExecute = false }));
            }
        }

        static string? Vdf(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    // Icône d'un jeu Steam dans son cache : ancien format <id>_icon.jpg, ou nouveau dossier <id>\ où
    // l'icône est le .jpg nommé par son empreinte (40 caractères hexadécimaux).
    private static string? SteamIcon(string steamPath, string id)
    {
        string cache = Path.Combine(steamPath, "appcache", "librarycache");
        string old = Path.Combine(cache, id + "_icon.jpg");
        if (File.Exists(old)) return old;
        string dir = Path.Combine(cache, id);
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.jpg")
            .FirstOrDefault(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), "^[0-9a-f]{40}$"));
    }

    // ----- icônes -----

    /// <summary>
    /// URL de l'icône (PNG en cache) d'un élément du shell : chemin de fichier ou <c>shell:AppsFolder\…</c>.
    /// Vide si Windows ne fournit rien (la page affiche alors l'initiale).
    /// </summary>
    private static string IconFor(string parsingName)
    {
        // Le shell refuse les chemins aux barres mélangées (« C:/Riot Games\… ») : on normalise.
        if (!parsingName.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            parsingName = Path.GetFullPath(parsingName);
        string file = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(parsingName.ToLowerInvariant()))) + ".png";
        string path = Path.Combine(IconDir, file);
        string url = $"https://{IconHost}/{file}";
        if (File.Exists(path)) return url;
        try
        {
            using var bmp = ShellIcon.Get(parsingName, IconPx);
            if (bmp is null) return "";
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return url;
        }
        catch { return ""; }
    }
}

/// <summary>Image d'un élément du shell (<c>IShellItemImageFactory</c>), avec sa transparence.</summary>
internal static class ShellIcon
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int Cx, Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size, Width, Height; public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint ctx, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint hdc, nint bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);

    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int SIIGBF_BIGGERSIZEOK = 0x1;

    public static Bitmap? Get(string parsingName, int px)
    {
        IShellItemImageFactory factory;
        try { SHCreateItemFromParsingName(parsingName, 0, IID_IShellItemImageFactory, out factory); }
        catch { return null; }
        try
        {
            if (factory.GetImage(new SIZE { Cx = px, Cy = px }, SIIGBF_BIGGERSIZEOK, out nint hbmp) < 0 || hbmp == 0) return null;
            try { return ToBitmap(hbmp, px); }
            finally { DeleteObject(hbmp); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    // Lecture des pixels en 32 bits haut-en-bas : l'alpha est conservé (FromHbitmap le perdrait).
    private static Bitmap? ToBitmap(nint hbmp, int px)
    {
        var bi = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>() };
        nint dc = GetDC(0);
        try
        {
            if (GetDIBits(dc, hbmp, 0, 0, null!, ref bi, 0) == 0 || bi.Width <= 0) return null;
            int w = bi.Width, h = Math.Abs(bi.Height);
            bi.Height = -h; bi.BitCount = 32; bi.Compression = 0; bi.SizeImage = 0;
            var bits = new byte[w * h * 4];
            if (GetDIBits(dc, hbmp, 0, (uint)h, bits, ref bi, 0) == 0) return null;

            // Sans canal alpha (vignette JPEG…), on rend l'image opaque.
            bool hasAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            for (int y = 0; y < h; y++) Marshal.Copy(bits, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            return bmp;
        }
        finally { ReleaseDC(0, dc); }
    }
}
