using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Azuria.Shell.Native;

/// <summary>
/// Les bureaux virtuels de Windows. Windows n'offre pas d'API publique pour les lister ni pour en
/// changer : on lit la liste et le bureau courant là où l'Explorateur les note (registre de
/// l'utilisateur, lecture seule), et on change de bureau avec ses propres raccourcis
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
