using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Azuria.Shell.Native;

/// <summary>
/// Compositeur Windows.UI.Composition du thread d'interface, et cible de composition posée sur un
/// HWND à nous (interop documentée <c>ICompositorDesktopInterop</c>). Sert à composer le verre
/// nous-mêmes : flou de l'arrière-plan + voile, découpés à la forme exacte et lissée du shell.
/// </summary>
internal static class CompositionHost
{
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;    // 2 = DQTYPE_THREAD_CURRENT
        public int apartmentType; // 2 = DQTAT_COM_STA
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    private static DispatcherQueueController? _controller;
    private static Compositor? _compositor;

    public static Compositor Compositor
    {
        get
        {
            if (_compositor is null)
            {
                var options = new DispatcherQueueOptions
                {
                    dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                    threadType = 2,
                    apartmentType = 2,
                };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out nint abi));
                _controller = MarshalInterface<DispatcherQueueController>.FromAbi(abi);
                _compositor = new Compositor();
            }
            return _compositor;
        }
    }

    /// <summary>
    /// <c>ICompositorDesktopInterop::CreateDesktopWindowTarget</c> (4e entrée de la vtable, après
    /// IUnknown). <paramref name="topmost"/> = false : nos visuels passent sous les fenêtres enfants
    /// (le WebView2 de l'interface reste au-dessus du verre).
    /// </summary>
    public static unsafe DesktopWindowTarget CreateTarget(nint hwnd, bool topmost = false)
    {
        nint compositorPtr = ((IWinRTObject)Compositor).NativeObject.ThisPtr;
        Guid iid = IID_ICompositorDesktopInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(compositorPtr, ref iid, out nint interop));
        try
        {
            var vtbl = *(void***)interop;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)vtbl[3];
            nint abi;
            Marshal.ThrowExceptionForHR(create(interop, hwnd, topmost ? 1 : 0, &abi));
            return MarshalInterface<DesktopWindowTarget>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }
}
