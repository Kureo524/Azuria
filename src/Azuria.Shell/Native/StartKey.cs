using System.Runtime.InteropServices;

namespace Azuria.Shell.Native;

/// <summary>
/// La touche Windows ouvre le lanceur d'Azuria au lieu du menu Démarrer (Ctrl+Échap aussi).
///
/// Crochet clavier bas niveau (<c>WH_KEYBOARD_LL</c>) : il tourne dans le processus d'Azuria, sur
/// son fil d'interface, et ne charge rien dans les autres processus (pas d'injection, pas de
/// pilote : CLAUDE.md). Il ne fait qu'observer la touche Windows :
/// - Win+T : ouvre le terminal d'Azuria ; la combinaison est retenue, Windows ne la voit pas ;
/// - Win + autre touche (Win+E, Win+D, Win+Maj+S…) : rien ne change, Windows s'en occupe ;
/// - Win seule, pressée puis relâchée : on retient le relâchement, on envoie une touche neutre
///   (non attribuée, 0xE8) puis le relâchement. Windows voit alors une combinaison et n'ouvre pas
///   le menu Démarrer ; Azuria ouvre son lanceur.
///
/// Quand une appli est en plein écran (un jeu), le crochet est retiré : rien d'Azuria ne se trouve
/// sur le chemin du clavier pendant une partie.
/// </summary>
internal sealed class StartKey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_ESCAPE = 0x1B, VK_CONTROL = 0x11, VK_T = 0x54;
    private const ushort VK_MASK = 0xE8; // non attribuée : n'a aucun effet, sinon d'interrompre « Win seule »
    private const uint LLKHF_INJECTED = 0x10;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint VkCode, ScanCode, Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public nint ExtraInfo; }

    // INPUT : type + union (la plus grande variante, MOUSEINPUT, fait 32 octets en 64 bits).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KEYBDINPUT Ki;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);

    private readonly HookProc _proc; // gardé vivant : Windows l'appelle tant que le crochet est posé
    private readonly SynchronizationContext _ui;
    private nint _hook;
    private int _winDown;   // touche Windows enfoncée (VK_LWIN / VK_RWIN), 0 sinon
    private bool _combo;    // une autre touche a été pressée pendant ce temps
    private bool _ours;     // ce « Win + touche » était un raccourci d'Azuria (Windows ne l'a pas vu)
    private bool _terminalKey; // T enfoncée pendant Win+T (son relâchement est retenu aussi)

    /// <summary>La touche Windows (seule) ou Ctrl+Échap vient d'être pressée.</summary>
    public event Action? Pressed;

    /// <summary>Win+T : ouvrir le terminal d'Azuria.</summary>
    public event Action? TerminalPressed;

    public StartKey()
    {
        _proc = OnKey;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    }

    /// <summary>Pose (true) ou retire (false) le crochet. Retiré pendant un jeu en plein écran.</summary>
    public bool Enabled
    {
        get => _hook != 0;
        set
        {
            if (value == Enabled) return;
            if (value) _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            else
            {
                UnhookWindowsHookEx(_hook);
                _hook = 0;
                _winDown = 0;
                _ours = _terminalKey = false;
            }
        }
    }

    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool up = msg is WM_KEYUP or WM_SYSKEYUP;
            int vk = (int)k.VkCode;
            bool injected = (k.Flags & LLKHF_INJECTED) != 0;

            if (!injected)
            {
                if (vk is VK_LWIN or VK_RWIN)
                {
                    if (down && _winDown == 0) { _winDown = vk; _combo = false; _ours = false; }
                    else if (up && vk == _winDown)
                    {
                        _winDown = 0;
                        if (!_combo || _ours)
                        {
                            // Win seule (ou Win + un raccourci d'Azuria, que Windows n'a pas vu) :
                            // touche neutre puis relâchement, envoyés après ce message (qu'on
                            // retient), pour que Windows n'ouvre pas le menu Démarrer.
                            SendMaskedRelease((ushort)vk);
                            if (!_combo) Raise();
                            _ours = false;
                            return 1;
                        }
                    }
                }
                else if (up && vk == VK_T && _terminalKey)
                {
                    _terminalKey = false; // relâchement de Win+T : retenu comme l'appui
                    return 1;
                }
                else if (down)
                {
                    if (_winDown != 0 && vk == VK_T)
                    {
                        // Win+T ouvre le terminal (raccourci de Caelestia). Dans Windows, Win+T
                        // parcourt la barre des tâches, masquée par Azuria : rien n'est perdu.
                        _combo = _ours = true;
                        if (!_terminalKey) { _terminalKey = true; _ui.Post(_ => TerminalPressed?.Invoke(), null); }
                        return 1; // la répétition de la touche ne rouvre pas de fenêtre
                    }
                    if (_winDown != 0) _combo = true;
                    else if (vk == VK_ESCAPE && GetAsyncKeyState(VK_CONTROL) < 0)
                    {
                        Raise();  // Ctrl+Échap : le raccourci du menu Démarrer
                        return 1;
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    // Rendre la main vite à Windows (un crochet lent ralentit tout le clavier) : le lanceur
    // s'ouvre au tour suivant de la boucle de messages.
    private void Raise() => _ui.Post(_ => Pressed?.Invoke(), null);

    private static void SendMaskedRelease(ushort win)
    {
        var inputs = new[]
        {
            Key(VK_MASK, 0),
            Key(VK_MASK, KEYEVENTF_KEYUP),
            Key(win, KEYEVENTF_KEYUP),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static INPUT Key(ushort vk, uint flags) =>
            new() { Type = INPUT_KEYBOARD, Ki = new KEYBDINPUT { Vk = vk, Flags = flags } };
    }

    public void Dispose() => Enabled = false;
}
