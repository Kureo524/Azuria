using System.Management;

namespace Azuria.Shell.Native;

/// <summary>
/// Luminosité de l'écran intégré (portable), par l'interface WMI que Windows expose pour cela
/// (<c>WmiMonitorBrightness</c>). Les écrans externes n'y répondent pas : le curseur est alors masqué.
/// Les appels WMI prennent quelques dizaines de millisecondes : à faire hors du fil d'interface.
/// </summary>
internal static class Brightness
{
    /// <summary>Niveau actuel (0–100), ou null si aucun écran ne se règle ainsi.</summary>
    public static int? Get()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in s.Get())
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    public static void Set(int level)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in s.Get())
                using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(level, 0, 100) });
        }
        catch { }
    }
}
