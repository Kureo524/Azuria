using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Azuria.Shell.Native;

/// <summary>Une image du dossier des fonds d'écran.</summary>
internal sealed record WallpaperEntry(string Path, string Name, string Thumb);

/// <summary>
/// Les fonds d'écran de la commande <c>&gt;wallpaper</c> du lanceur : les images du dossier
/// <c>Images\Wallpaper</c> de l'utilisateur (le dossier Images de Windows, même déplacé ou dans OneDrive).
/// Le fond est posé par <c>IDesktopWallpaper</c>, l'API documentée de Windows pour ça (celle de
/// l'appli Paramètres). Rien n'est injecté (CLAUDE.md).
///
/// Tout passe par un seul fil STA, dans l'ordre : lire le dossier ou changer de fond ne bloque pas
/// l'interface, et un aperçu ne double jamais une validation.
///
/// Les miniatures sont mises en cache en PNG avec les icônes du lanceur (<see cref="Catalog.IconDir"/>),
/// nommées d'après le chemin, la date et la taille du fichier : une image modifiée a une nouvelle miniature.
/// </summary>
internal static class Wallpapers
{
    private const int ThumbPx = 320; // carte de 160 px CSS, nette jusqu'à 200 %

    // Formats que Windows sait poser en fond d'écran.
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    /// <summary>Le dossier lu : <c>Images\Wallpaper</c>, ou <c>Images\Wallpapers</c> s'il est le seul à exister.</summary>
    public static string Folder
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string one = System.IO.Path.Combine(pictures, "Wallpaper");
            string many = System.IO.Path.Combine(pictures, "Wallpapers");
            return !Directory.Exists(one) && Directory.Exists(many) ? many : one;
        }
    }

    /// <summary>Les images du dossier (vide s'il n'existe pas), triées par nom.</summary>
    public static Task<List<WallpaperEntry>> ScanAsync(string folder) => Worker.Run(() =>
    {
        var items = new List<WallpaperEntry>();
        if (Directory.Exists(folder))
            foreach (var file in Directory.EnumerateFiles(folder))
                if (Extensions.Contains(System.IO.Path.GetExtension(file)))
                    items.Add(new WallpaperEntry(file, System.IO.Path.GetFileNameWithoutExtension(file), Thumb(file)));
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return items;
    });

    /// <summary>Le fond de chaque écran en ce moment (identifiant d'écran → chemin).</summary>
    public static Task<Dictionary<string, string>> SnapshotAsync() => Worker.Run(Snapshot);

    /// <summary>
    /// Pose l'image sur tous les écrans, sans rien enregistrer d'autre (aperçu). Rend les fonds posés.
    /// </summary>
    public static Task<Dictionary<string, string>> PreviewAsync(string path) => Worker.Run(() =>
    {
        SetAll(path);
        return Snapshot();
    });

    /// <summary>Remet chaque écran sur le fond qu'il avait (fin d'un aperçu).</summary>
    public static Task<Dictionary<string, string>> RestoreAsync(IReadOnlyDictionary<string, string> before) => Worker.Run(() =>
    {
        try
        {
            var dw = Create();
            try
            {
                foreach (var (monitor, path) in before)
                    try { dw.SetWallpaper(monitor, path); } catch { /* écran débranché entre-temps */ }
            }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch { /* sans IDesktopWallpaper, l'aperçu n'a pas pu être posé non plus */ }
        return Snapshot();
    });

    /// <summary>
    /// Pose l'image pour de bon : tous les écrans, et tous les bureaux virtuels (Windows 11 garde un
    /// fond par bureau, noté dans le registre de l'Explorateur). Rend les fonds posés.
    /// </summary>
    public static Task<Dictionary<string, string>> CommitAsync(string path) => Worker.Run(() =>
    {
        SetAll(path);
        WriteDesktops(path);
        return Snapshot();
    });

    /// <summary>
    /// Fond du bureau courant différent de <paramref name="path"/> (bureau créé ou laissé à son ancien
    /// fond) : on le repose. Vrai s'il a fallu le faire.
    /// </summary>
    public static Task<bool> EnsureAsync(string path) => Worker.Run(() =>
    {
        if (!File.Exists(path)) return false;
        var now = Snapshot();
        string full = System.IO.Path.GetFullPath(path);
        if (now.Count > 0 && now.Values.All(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase))) return false;
        SetAll(path);
        WriteDesktops(path);
        return true;
    });

    // ----- fond -----

    private static void SetAll(string path)
    {
        try
        {
            var dw = Create();
            try { dw.SetWallpaper(null, path); }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch
        {
            // Sans IDesktopWallpaper : l'ancienne API, qui enregistre le choix et prévient Windows.
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
    }

    private static Dictionary<string, string> Snapshot()
    {
        var map = new Dictionary<string, string>();
        try
        {
            var dw = Create();
            try
            {
                dw.GetMonitorDevicePathCount(out uint n);
                for (uint i = 0; i < n; i++)
                {
                    try
                    {
                        dw.GetMonitorDevicePathAt(i, out string monitor);
                        dw.GetWallpaper(monitor, out string wp);
                        if (!string.IsNullOrEmpty(wp)) map[monitor] = System.IO.Path.GetFullPath(wp);
                    }
                    catch { /* écran inactif : pas de fond à lire */ }
                }
            }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch { /* on ne sait pas : aucun fond n'est connu */ }
        return map;
    }

    // Le fond de chaque bureau virtuel, là où l'Explorateur le note (un sous-dossier par bureau).
    // Il le relit quand il redémarre ; pour la session en cours, ShellContext repose le fond au
    // changement de bureau (EnsureAsync).
    private const string DesktopsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private static void WriteDesktops(string path)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(DesktopsKey);
            if (root?.GetValue("VirtualDesktopIDs") is not byte[] ids) return;
            for (int i = 0; i + 16 <= ids.Length; i += 16)
            {
                string id = new Guid(ids.AsSpan(i, 16)).ToString("B").ToUpperInvariant();
                using var k = Registry.CurrentUser.OpenSubKey($@"{DesktopsKey}\Desktops\{id}", writable: true);
                k?.SetValue("Wallpaper", path, RegistryValueKind.String);
            }
        }
        catch { /* registre illisible : le fond reste posé sur le bureau courant */ }
    }

    // ----- miniatures -----

    private static string Thumb(string file)
    {
        try
        {
            var info = new FileInfo(file);
            string key = $"{info.FullName.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{ThumbPx}";
            string name = "fond-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + ".png";
            string path = System.IO.Path.Combine(Catalog.IconDir, name);
            string url = $"https://{Catalog.IconHost}/{name}";
            if (File.Exists(path)) return url;
            using var bmp = ShellIcon.Get(info.FullName, ThumbPx);
            if (bmp is null) return "";
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return url;
        }
        catch { return ""; }
    }

    // ----- COM -----

    private static IDesktopWallpaper Create() =>
        (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"))!)!;

    // Seules les premières méthodes servent ; l'ordre est celui de shobjidl_core.h.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        void GetMonitorDevicePathCount(out uint count);
    }

    private const uint SPI_SETDESKWALLPAPER = 0x14, SPIF_UPDATEINIFILE = 0x1, SPIF_SENDCHANGE = 0x2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string pvParam, uint winIni);

    /// <summary>Un fil STA à part (le shell et COM le demandent) qui exécute les travaux dans l'ordre.</summary>
    private static class Worker
    {
        private static readonly BlockingCollection<Action> Queue = new();

        static Worker()
        {
            var t = new Thread(() => { foreach (var job in Queue.GetConsumingEnumerable()) job(); })
                { IsBackground = true, Name = "Azuria.Wallpaper" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public static Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }
    }
}
