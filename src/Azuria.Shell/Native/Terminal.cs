using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Azuria.Shell.Native;

/// <summary>
/// Le terminal d'Azuria (idée de Caelestia : un terminal en verre, un prompt sobre, les infos du
/// système à l'ouverture ; rien n'est repris de ses fichiers). C'est Windows Terminal, habillé :
/// <list type="bullet">
/// <item>un profil « Azuria » posé par un <b>fragment</b> (mécanisme officiel de Windows Terminal) :
/// les réglages de l'utilisateur ne sont jamais modifiés, et le profil disparaît avec le dossier ;</item>
/// <item>son jeu de couleurs suit la DA Encre et l'accent choisi dans les réglages d'Azuria ;</item>
/// <item>le prompt et l'accueil sont dans <c>terminal/azuria.ps1</c>, lu par PowerShell à l'ouverture ;
/// ce qui ne change pas (processeur, carte graphique, Windows) est écrit ici une fois, pour que
/// l'accueil s'affiche sans attendre WMI.</item>
/// </list>
/// Rien n'est injecté nulle part : des fichiers dans le profil de l'utilisateur, et un lancement de
/// <c>wt.exe</c>.
/// </summary>
internal static class Terminal
{
    /// <summary>Identifiant fixe du profil (Windows Terminal le garde d'une session à l'autre).</summary>
    public const string ProfileGuid = "{6f3a5c1e-2b8d-4a7e-9c41-a2d5e0b7c3f9}";
    private const string ProfileName = "Azuria";

    private static readonly string FragmentDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows Terminal", "Fragments", "Azuria");

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Azuria", "terminal");

    private static readonly string Script = Path.Combine(DataDir, "azuria.ps1");
    private static readonly string Infos = Path.Combine(DataDir, "infos.json");

    private static Dictionary<string, string>? _static; // processeur, carte graphique… (lu une fois)

    /// <summary>
    /// Pose (ou met à jour) le profil, le script et les infos. À appeler au démarrage et quand
    /// l'accent change. Les lectures lentes (WMI) se font hors du fil d'interface.
    /// </summary>
    public static async Task InstallAsync()
    {
        string accent = Settings.Current.AccentHex;
        try
        {
            _static ??= await Task.Run(ReadStatic);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(DataDir);
                Directory.CreateDirectory(FragmentDir);
                // UTF-8 avec BOM : sans lui, PowerShell 5.1 lit le script en ANSI (accents, glyphes).
                string source = Path.Combine(AppContext.BaseDirectory, "terminal", "azuria.ps1");
                WriteIfChanged(Script, File.ReadAllText(source), new UTF8Encoding(true));
                WriteIfChanged(Infos, InfosJson(accent), new UTF8Encoding(false));
                WriteIfChanged(Path.Combine(FragmentDir, "azuria.json"), FragmentJson(accent), new UTF8Encoding(false));
            });
        }
        catch { /* disque, droits : le terminal reste celui de Windows, rien d'autre ne casse */ }
    }

    /// <summary>Ouvre une fenêtre du terminal d'Azuria (ou PowerShell seul si Windows Terminal manque).</summary>
    public static void Open()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-w new -p \"{ProfileName}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = home,
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", PowerShellArgs())
                {
                    UseShellExecute = true,
                    WorkingDirectory = home,
                });
            }
            catch { /* ni Windows Terminal ni PowerShell : rien à ouvrir */ }
        }
    }

    // -ExecutionPolicy Bypass ne vaut que pour ce processus (la stratégie de la machine ne change
    // pas) : sans lui, un PC où les scripts sont désactivés refuserait notre prompt.
    private static string PowerShellArgs() =>
        $"-NoLogo -NoExit -ExecutionPolicy Bypass -Command \". '{Script.Replace("'", "''")}'\"";

    // ----- contenu -----

    private static string FragmentJson(string accent)
    {
        var fragment = new JsonObject
        {
            ["profiles"] = new JsonArray
            {
                new JsonObject
                {
                    ["guid"] = ProfileGuid,
                    ["name"] = ProfileName,
                    ["commandline"] = "powershell.exe " + PowerShellArgs(),
                    ["icon"] = Path.Combine(AppContext.BaseDirectory, "assets", "azuria.ico"),
                    ["startingDirectory"] = "%USERPROFILE%",
                    ["colorScheme"] = ProfileName,
                    // Verre : acrylique de Windows Terminal, voile assez léger pour voir le fond flouté.
                    ["useAcrylic"] = true,
                    ["opacity"] = 72,
                    ["padding"] = "22, 18",
                    ["font"] = new JsonObject { ["face"] = "Cascadia Mono", ["size"] = 11 },
                    ["cursorShape"] = "bar",
                    ["bellStyle"] = "none",
                    ["scrollbarState"] = "hidden",
                    ["tabTitle"] = ProfileName,
                },
            },
            ["schemes"] = new JsonArray { Scheme(accent) },
        };
        return fragment.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Jeu de couleurs Encre (docs/da.md §3) : fond d'encre, textes de la DA, couleurs d'état pour
    // rouge, vert, jaune ; le bleu suit l'accent. Les teintes « vives » sont éclaircies de 10 points.
    private static JsonObject Scheme(string accent)
    {
        string accentText = Lighten(accent, 0.10);
        return new JsonObject
        {
            ["name"] = ProfileName,
            ["background"] = "#0B0C11",
            ["foreground"] = "#E6EAF2",
            ["cursorColor"] = accent,
            ["selectionBackground"] = accent,
            ["black"] = "#1A1C24",
            ["red"] = "#D9646C",
            ["green"] = "#93B07E",
            ["yellow"] = "#D9AA3A",
            ["blue"] = accent,
            ["purple"] = "#A89AD1",
            ["cyan"] = "#8FB3C4",
            ["white"] = "#A2A6B4",
            ["brightBlack"] = "#868A98",
            ["brightRed"] = Lighten("#D9646C", 0.10),
            ["brightGreen"] = Lighten("#93B07E", 0.10),
            ["brightYellow"] = Lighten("#D9AA3A", 0.10),
            ["brightBlue"] = accentText,
            ["brightPurple"] = Lighten("#A89AD1", 0.10),
            ["brightCyan"] = Lighten("#8FB3C4", 0.10),
            ["brightWhite"] = "#E6EAF2",
        };
    }

    private static string InfosJson(string accent)
    {
        var d = new Dictionary<string, string>(_static ?? new())
        {
            ["accent"] = accent,
            ["accentText"] = Lighten(accent, 0.10),
            ["version"] = typeof(Terminal).Assembly.GetName().Version?.ToString(3) ?? "",
            // Démarrage de Windows : l'accueil en déduit la durée de fonctionnement sans requête.
            ["boot"] = DateTimeOffset.Now.AddMilliseconds(-Environment.TickCount64).ToString("o"),
        };
        return JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, string> ReadStatic()
    {
        var d = new Dictionary<string, string>();
        try
        {
            using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = cv?.GetValue("ProductName") as string ?? "Windows";
            int build = int.TryParse(cv?.GetValue("CurrentBuildNumber") as string, out int b) ? b : 0;
            // Windows 11 se déclare encore « Windows 10 » dans le registre : on corrige d'après le build.
            if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");
            string display = cv?.GetValue("DisplayVersion") as string ?? "";
            d["os"] = $"{product} {display}".Trim();
            d["build"] = $"{build}.{cv?.GetValue("UBR")}";
        }
        catch { }
        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            d["cpu"] = (cpu?.GetValue("ProcessorNameString") as string ?? "").Trim();
            d["threads"] = Environment.ProcessorCount.ToString();
        }
        catch { }
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var names = q.Get().Cast<ManagementObject>()
                         .Select(o => (o["Name"] as string ?? "").Trim())
                         .Where(n => n.Length > 0 && !n.Contains("Parsec", StringComparison.OrdinalIgnoreCase)
                                                  && !n.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                         .Distinct();
            d["gpu"] = string.Join(" · ", names);
        }
        catch { }
        return d;
    }

    // ----- outils -----

    private static void WriteIfChanged(string path, string content, Encoding enc)
    {
        // Réécrire à l'identique ferait recharger Windows Terminal pour rien.
        try { if (File.Exists(path) && File.ReadAllText(path) == content) return; } catch { }
        File.WriteAllText(path, content, enc);
    }

    // Éclaircit une couleur #RRGGBB de `amount` (0–1) en luminosité HSL, plafonnée à 90 % (DA §3).
    private static string Lighten(string hex, double amount)
    {
        var c = ColorTranslator.FromHtml(hex);
        double h = c.GetHue(), s = c.GetSaturation(), l = Math.Min(0.9, c.GetBrightness() + amount);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Ch(double t)
        {
            t = (t % 1 + 1) % 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        int R(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        double hh = h / 360;
        return $"#{R(Ch(hh + 1 / 3.0)):X2}{R(Ch(hh)):X2}{R(Ch(hh - 1 / 3.0)):X2}";
    }
}
