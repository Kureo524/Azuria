using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Azuria.Shell.Native;

/// <summary>
/// Les bureaux virtuels de Windows. Windows n'offre pas d'API publique pour les lister ni pour en
/// changer : on lit la liste et le bureau courant là où l'Explorateur les note (registre de
/// l'utilisateur, lecture seule), le bureau d'une fenêtre par l'API documentée
/// <c>IVirtualDesktopManager</c>, et on change de bureau avec ses propres raccourcis
/// (Ctrl+Win+← / →, Ctrl+Win+D pour en créer un). Rien n'est injecté dans l'Explorateur.
/// </summary>
internal static class VirtualDesktops
{
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>Nombre de bureaux et index du bureau courant (0 = le premier).</summary>
    public static (int Count, int Current) State()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegKey);
            if (k?.GetValue("VirtualDesktopIDs") is not byte[] ids || ids.Length < 16) return (1, 0);
            int count = ids.Length / 16;
            if (k.GetValue("CurrentVirtualDesktop") is byte[] cur && cur.Length == 16)
                for (int i = 0; i < count; i++)
                    if (ids.AsSpan(i * 16, 16).SequenceEqual(cur)) return (count, i);
            return (count, 0);
        }
        catch { return (1, 0); }
    }

    /// <summary>Les identifiants des bureaux, dans l'ordre de Windows.</summary>
    public static List<Guid> Ids()
    {
        var list = new List<Guid>();
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegKey);
            if (k?.GetValue("VirtualDesktopIDs") is byte[] ids)
                for (int i = 0; i + 16 <= ids.Length; i += 16) list.Add(new Guid(ids.AsSpan(i, 16)));
        }
        catch { /* lecture impossible : un seul bureau, sans identifiant */ }
        return list;
    }

    /// <summary>Le nom donné au bureau dans Windows, ou « Bureau N ».</summary>
    public static string Name(int index, List<Guid> ids)
    {
        try
        {
            if (index < ids.Count)
            {
                using var k = Registry.CurrentUser.OpenSubKey($@"{RegKey}\Desktops\{ids[index]:B}");
                if (k?.GetValue("Name") is string { Length: > 0 } n) return n;
            }
        }
        catch { /* nom illisible : le nom par défaut */ }
        return $"Bureau {index + 1}";
    }

    /// <summary>
    /// Le bureau d'une fenêtre (<c>IVirtualDesktopManager</c>, l'API documentée de Windows pour ça),
    /// ou <see cref="Guid.Empty"/> si Windows ne le dit pas.
    /// </summary>
    public static Guid DesktopOf(nint hwnd)
    {
        try { return Manager.GetWindowDesktopId(hwnd, out var id) == 0 ? id : Guid.Empty; }
        catch { return Guid.Empty; }
    }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid id);
        [PreserveSig] int MoveWindowToDesktop(nint hwnd, ref Guid id);
    }

    private static IVirtualDesktopManager? _manager;
    private static IVirtualDesktopManager Manager => _manager ??= (IVirtualDesktopManager)Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"))!)!;

    /// <summary>Va au bureau <paramref name="index"/>, d'autant de pas que nécessaire.</summary>
    public static void SwitchTo(int index)
    {
        var (count, current) = State();
        index = Math.Clamp(index, 0, count - 1);
        int steps = index - current;
        if (steps == 0) return;
        ushort arrow = steps > 0 ? VK_RIGHT : VK_LEFT;
        var keys = new List<INPUT> { Key(VK_LCONTROL, false), Key(VK_LWIN, false) };
        for (int i = 0; i < Math.Abs(steps); i++) { keys.Add(Key(arrow, false, true)); keys.Add(Key(arrow, true, true)); }
        keys.Add(Key(VK_LWIN, true));
        keys.Add(Key(VK_LCONTROL, true));
        Send(keys);
    }

    /// <summary>Crée un bureau et y va (Ctrl+Win+D).</summary>
    public static void Create() =>
        Send(new List<INPUT> { Key(VK_LCONTROL, false), Key(VK_LWIN, false), Key(VK_D, false), Key(VK_D, true), Key(VK_LWIN, true), Key(VK_LCONTROL, true) });

    // ----- clavier -----

    private const ushort VK_LCONTROL = 0xA2, VK_LWIN = 0x5B, VK_LEFT = 0x25, VK_RIGHT = 0x27, VK_D = 0x44;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KEYBDINPUT Ki; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    private static INPUT Key(ushort vk, bool up, bool extended = false) => new()
    {
        Type = INPUT_KEYBOARD,
        Ki = new KEYBDINPUT { Vk = vk, Flags = (up ? KEYEVENTF_KEYUP : 0) | (extended || vk == VK_LWIN ? KEYEVENTF_EXTENDEDKEY : 0) },
    };

    private static void Send(List<INPUT> keys) => SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf<INPUT>());
}
