using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Azuria.Shell.Native;

/// <summary>
/// Actions de session (verrouiller, veille, se déconnecter, redémarrer, éteindre) et mode jeu de
/// Windows. Les mêmes commandes que le menu Démarrer : API documentées et <c>shutdown.exe</c>.
/// </summary>
internal static class Session
{
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);

    public static void Lock() => LockWorkStation();

    public static void Sleep() => SetSuspendState(false, false, false);

    public static void SignOut() => Run("/l");

    public static void Restart() => Run("/r /t 0");

    public static void ShutDown() => Run("/s /t 0");

    private static void Run(string args)
    {
        try { Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true }); }
        catch { }
    }

    // Mode jeu : le réglage de Windows (Paramètres › Jeux), activé par défaut si la valeur manque.
    private const string GameBarKey = @"Software\Microsoft\GameBar";

    public static bool GameMode
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(GameBarKey);
                return k?.GetValue("AutoGameModeEnabled") is not int v || v != 0;
            }
            catch { return true; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(GameBarKey);
                k.SetValue("AutoGameModeEnabled", value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
