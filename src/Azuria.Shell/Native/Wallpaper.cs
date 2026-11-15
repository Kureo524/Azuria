using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Azuria.Shell.Native;

/// <summary>Une image du dossier des fonds d'écran.</summary>
internal sealed record WallpaperEntry(string Path, string Name, string Thumb);

/// <summary>
/// Les fonds d'écran de la commande <c>&gt;wallpaper</c> du lanceur : les images du dossier
/// <c>Images\Wallpaper</c> de l'utilisateur (le dossier Images de Windows, même déplacé ou dans OneDrive).
/// Le fond est posé par <c>IDesktopWallpaper</c>, l'API documentée de Windows pour ça (celle qu'utilise
/// l'appli Paramètres), sur tous les écrans. Rien n'est injecté (CLAUDE.md).
///
/// Les miniatures sont mises en cache en PNG avec les icônes du lanceur (<see cref="Catalog.IconDir"/>),
/// nommées d'après le chemin, la date et la taille du fichier : une image modifiée a une nouvelle miniature.
/// Tout passe sur un fil STA à part : lire le dossier ou changer de fond ne bloque pas l'interface.
/// </summary>
internal static class Wallpapers
{
    private const int ThumbPx = 160;

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

    /// <summary>Les images du dossier (vide s'il n'existe pas) et les fonds posés en ce moment.</summary>
    public static Task<(List<WallpaperEntry> Items, HashSet<string> Current)> ScanAsync(string folder) => Sta.Run(() =>
    {
        var items = new List<WallpaperEntry>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (!Extensions.Contains(System.IO.Path.GetExtension(file))) continue;
                items.Add(new WallpaperEntry(file, System.IO.Path.GetFileNameWithoutExtension(file), Thumb(file)));
            }
        }
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return (items, Current());
    });

    /// <summary>Pose l'image en fond d'écran sur tous les écrans. Rend les fonds posés après coup.</summary>
    public static Task<HashSet<string>> SetAsync(string path) => Sta.Run(() =>
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
        return Current();
    });

    // Le fond de chaque écran (ils peuvent différer). Les chemins sont normalisés pour la comparaison.
    private static HashSet<string> Current()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                        if (!string.IsNullOrEmpty(wp)) set.Add(System.IO.Path.GetFullPath(wp));
                    }
                    catch { /* écran inactif : pas de fond à lire */ }
                }
            }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch { /* on ne sait pas : aucun fond n'est marqué */ }
        return set;
    }

    private static string Thumb(string file)
    {
        try
        {
            var info = new FileInfo(file);
            string key = $"{info.FullName.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
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

    /// <summary>Exécute un travail sur un fil STA à part (le shell et COM le demandent).</summary>
    private static class Sta
    {
        public static Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var t = new Thread(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception e) { tcs.SetException(e); }
            }) { IsBackground = true, Name = "Azuria.Wallpaper" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return tcs.Task;
        }
    }
}
