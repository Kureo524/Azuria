using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Azuria.Shell;

/// <summary>
/// Les réglages d'Azuria, modifiables sur place dans l'onglet « Réglages » du tableau de bord.
/// Enregistrés dans <c>%APPDATA%\Azuria\settings.json</c> ; le lancement au démarrage vit, lui, dans
/// la clé « Run » de l'utilisateur (aucun droit d'administrateur, rien hors de son profil).
/// </summary>
internal sealed class Settings
{
    /// <summary>
    /// Préréglages d'accent de la DA (§4) : désaturés et de même clarté, aucun ne ressemble à une
    /// couleur d'état. Le premier est celui par défaut.
    /// </summary>
    public static readonly (string Id, string Name, string Hex)[] Accents =
    {
        ("ink", "Ink", "#8fa4cc"),
        ("steel", "Steel", "#8fb3c4"),
        ("sage", "Sage", "#8fb5a0"),
        ("amber", "Amber", "#c9a877"),
        ("rose", "Rose", "#cf9bb3"),
        ("amethyst", "Amethyst", "#a89ad1"),
    };

    public string Accent { get; set; } = Accents[0].Id;
    public bool Tiling { get; set; } = true;
    public bool FocusFollowsMouse { get; set; } = true;

    // ----- instance courante, chargement, enregistrement -----

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Azuria");
    private static readonly string File = Path.Combine(Dir, "settings.json");

    public static Settings Current { get; private set; } = Load();

    /// <summary>Un réglage a changé (déjà enregistré).</summary>
    public static event Action? Changed;

    private static Settings Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
            {
                var s = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File)) ?? new();
                if (!Accents.Any(a => a.Id == s.Accent)) s.Accent = Accents[0].Id;
                return s;
            }
        }
        catch { /* fichier abîmé : on repart des valeurs par défaut */ }
        return new Settings();
    }

    /// <summary>Applique une modification, l'enregistre et prévient le shell.</summary>
    public static void Update(Action<Settings> change)
    {
        change(Current);
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* disque plein, dossier verrouillé : le réglage vaut pour cette session */ }
        Changed?.Invoke();
    }

    // ----- teinte -----

    [JsonIgnore] public string AccentHex => Accents.FirstOrDefault(a => a.Id == Accent).Hex ?? Accents[0].Hex;

    /// <summary>Couleur d'accent en COLORREF (0x00BBGGRR), pour le contour de la fenêtre active.</summary>
    [JsonIgnore] public uint AccentColorRef
    {
        get
        {
            var (r, g, b) = Rgb(AccentHex);
            return (uint)(r | (g << 8) | (b << 16));
        }
    }

    /// <summary>
    /// Script qui pose la teinte sur une page : les jetons d'accent de la DA (encre.css), dérivés de la
    /// couleur choisie comme le dit la DA §3 : texte = 10 points de luminosité en plus (90 % au plus),
    /// fonds à 14 % et 42 % d'opacité.
    /// </summary>
    public string ThemeScript()
    {
        var (r, g, b) = Rgb(AccentHex);
        var (tr, tg, tb) = Lighter(r, g, b, 0.10, 0.90);
        string inv(double d) => d.ToString(CultureInfo.InvariantCulture);
        string vars =
            $"--accent:{AccentHex};" +
            $"--accent-text:rgb({tr},{tg},{tb});" +
            $"--accent-soft:rgba({r},{g},{b},{inv(0.14)});" +
            $"--accent-tint:rgba({r},{g},{b},{inv(0.42)});";
        // La page peut ne pas avoir encore de <html> : on réessaie au chargement du DOM.
        return "(() => { const v = " + JsonSerializer.Serialize(vars) + ";" +
               " const put = () => { const s = document.documentElement?.style; if (!s) return false;" +
               " for (const d of v.split(';')) { const i = d.indexOf(':'); if (i > 0) s.setProperty(d.slice(0, i), d.slice(i + 1)); } return true; };" +
               " if (!put()) document.addEventListener('DOMContentLoaded', put, { once: true }); })();";
    }

    // Luminosité HSL augmentée de « by », plafonnée à « max » ; teinte et saturation gardées.
    private static (int R, int G, int B) Lighter(int r, int g, int b, double by, double max)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        double hi = Math.Max(R, Math.Max(G, B)), lo = Math.Min(R, Math.Min(G, B)), l = (hi + lo) / 2, d = hi - lo;
        double h = 0, s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        if (d != 0)
            h = hi == R ? ((G - B) / d % 6 + 6) % 6 : hi == G ? (B - R) / d + 2 : (R - G) / d + 4;
        l = Math.Min(max, l + by);
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = l - c / 2;
        var (r1, g1, b1) = (int)h switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        static int To(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return (To(r1 + m), To(g1 + m), To(b1 + m));
    }

    private static (int R, int G, int B) Rgb(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));

    // ----- lancement à l'ouverture de session -----

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Azuria";

    /// <summary>Vrai si Azuria se lance à l'ouverture de session (clé « Run » de l'utilisateur).</summary>
    public static bool LaunchAtStartup
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(RunName) is string;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
                else k.DeleteValue(RunName, throwOnMissingValue: false);
            }
            catch { }
            Changed?.Invoke();
        }
    }
}
