using static VerreFloute.Interop;

namespace VerreFloute;

/// <summary>Fenêtre d'arrière-plan opaque et contrastée (GDI) : moitié blanche, moitié noire,
/// bandes colorées et texte, pour juger le flou et la lisibilité du verre posé devant.</summary>
internal sealed class BackgroundWindow
{
    private const string ClassName = "AzuriaFondTest";
    private static readonly WndProc s_wndProc = StaticWndProc;
    private static nint s_hwnd;

    public static void CreateAndShow()
    {
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0003,
            lpfnWndProc = s_wndProc,
            hCursor = LoadCursorW(0, IDC_ARROW),
            lpszClassName = ClassName,
        };
        if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("RegisterClassExW a échoué");

        s_hwnd = CreateWindowExW(0, ClassName, "Fond de test — Azuria", WS_OVERLAPPEDWINDOW,
            0, 0, 1200, 800, 0, 0, 0, 0);
        if (s_hwnd == 0) throw new InvalidOperationException("CreateWindowExW a échoué");
        ShowWindow(s_hwnd, SW_SHOWMAXIMIZED);
        UpdateWindow(s_hwnd);
    }

    private static nint StaticWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
            {
                nint hdc = BeginPaint(hWnd, out PAINTSTRUCT ps);
                GetClientRect(hWnd, out RECT rc);
                int w = rc.Right, h = rc.Bottom;

                // moitié gauche blanche, moitié droite noire
                FillRectColor(hdc, 0, 0, w / 2, h, 0x00FFFFFF);
                FillRectColor(hdc, w / 2, 0, w, h, 0x00000000);

                // bandes colorées verticales au centre (contours nets pour voir le flou)
                uint[] colors = [0x000000FF, 0x0000A5FF, 0x0000FFFF, 0x0000FF00, 0x00FF0000, 0x00FF00FF]; // BGR
                int bandW = 60, x0 = w / 2 - colors.Length * bandW / 2;
                for (int i = 0; i < colors.Length; i++)
                    FillRectColor(hdc, x0 + i * bandW, h / 4, x0 + (i + 1) * bandW, 3 * h / 4, colors[i]);

                // damier en bas
                for (int yy = 0; yy < 4; yy++)
                    for (int xx = 0; xx < w / 40 + 1; xx++)
                        if ((xx + yy) % 2 == 0)
                            FillRectColor(hdc, xx * 40, h - 160 + yy * 40, (xx + 1) * 40, h - 160 + (yy + 1) * 40, 0x00808080);

                // texte net, grand, pour juger le flou
                nint font = CreateFontW(64, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
                nint old = SelectObject(hdc, font);
                SetBkMode(hdc, TRANSPARENT_BKMODE);
                SetTextColor(hdc, 0x00000000);
                TextOutW(hdc, 40, 60, "Texte net derrière", 18);
                SetTextColor(hdc, 0x00FFFFFF);
                TextOutW(hdc, w / 2 + 40, 60, "Texte net derrière", 18);
                SelectObject(hdc, old);
                DeleteObject(font);

                EndPaint(hWnd, ref ps);
                return 0;
            }
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void FillRectColor(nint hdc, int l, int t, int r, int b, uint bgr)
    {
        var rect = new RECT { Left = l, Top = t, Right = r, Bottom = b };
        nint brush = CreateSolidBrush(bgr);
        FillRect(hdc, ref rect, brush);
        DeleteObject(brush);
    }

    public static void RunMessageLoop()
    {
        while (GetMessageW(out MSG msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }
}
