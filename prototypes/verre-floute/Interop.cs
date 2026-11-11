using System.Runtime.InteropServices;

namespace VerreFloute;

/// <summary>P/Invoke Win32 : fenêtres, DWM, régions. Uniquement des API documentées,
/// sauf SetWindowCompositionAttribute (marquée comme telle, servant à un essai).</summary>
internal static class Interop
{
    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    // ---------- structures ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    // SetWindowCompositionAttribute — API NON DOCUMENTÉE (voir docs/flou-windows.md §4).
    // Présente ici uniquement pour l'essai « host backdrop » du prototype.
    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWCOMPOSITIONATTRIBDATA { public int Attrib; public nint pvData; public nint cbData; }

    // ---------- constantes ----------

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_VISIBLE = 0x10000000;

    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_DPICHANGED = 0x02E0;

    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;

    public const int SW_SHOW = 5;
    public const int SW_SHOWMAXIMIZED = 3;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const int GWL_EXSTYLE = -20;

    // DWM
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;     // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;   // Mica Alt

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    public const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_BLURBEHIND = 3;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    public const int ACCENT_ENABLE_HOSTBACKDROP = 5;

    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;

    public const uint SPI_GETWORKAREA = 0x0030;

    // ---------- user32 ----------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int cmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out MSG msg, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern bool PeekMessageW(out MSG msg, nint hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(nint hWnd, nint hRgn, bool redraw);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadCursorW(nint instance, nint cursorName);
    public const int IDC_ARROW = 32512;

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(nint value);
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("user32.dll")]
    public static extern nint SetWindowLongPtrW(nint hWnd, int index, nint value);

    [DllImport("user32.dll")]
    public static extern nint GetWindowLongPtrW(nint hWnd, int index);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint colorKey, byte alpha, uint flags);
    public const uint LWA_ALPHA = 2;

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfoW(uint action, uint param, out RECT rect, uint ini);

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    public static extern int FillRect(nint hdc, ref RECT rect, nint brush);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string? className, string? windowName);

    // NON DOCUMENTÉE — essai « host backdrop » uniquement.
    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(nint hWnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    // ---------- gdi32 ----------

    [DllImport("gdi32.dll")]
    public static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    public static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool TextOutW(nint hdc, int x, int y, string text, int length);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(nint hdc, uint color);

    [DllImport("gdi32.dll")]
    public static extern uint SetBkMode(nint hdc, int mode);
    public const int TRANSPARENT_BKMODE = 1;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint obj);

    // ---------- dwmapi ----------

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hWnd, uint attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hWnd, uint attr, ref uint value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref MARGINS margins);

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    // ---------- aides ----------

    public static int Scale(int logical, uint dpi) => (int)Math.Round(logical * dpi / 96.0);
    public static float ScaleF(float logical, uint dpi) => (float)(logical * dpi / 96.0);
}
