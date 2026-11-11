using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace VerreFloute;

/// <summary>File d'attente de répartition + compositeur Windows.UI.Composition pour le thread
/// d'interface, et création d'un DesktopWindowTarget sur un HWND (interop documentée).</summary>
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

    public static Compositor Compositor { get; private set; } = null!;

    public static void EnsureInitialized()
    {
        if (_controller is not null) return;

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,
            apartmentType = 2,
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out nint abi));
        _controller = MarshalInterface<DispatcherQueueController>.FromAbi(abi);

        Compositor = new Compositor();
    }

    /// <summary>ICompositorDesktopInterop::CreateDesktopWindowTarget, appelé directement
    /// par la vtable COM (l'interface n'a que cette méthode, en 4e position après IUnknown).</summary>
    public static unsafe DesktopWindowTarget CreateTarget(nint hwnd)
    {
        nint compositorPtr = ((IWinRTObject)Compositor).NativeObject.ThisPtr;
        Guid iid = IID_ICompositorDesktopInterop;
        int hr = Marshal.QueryInterface(compositorPtr, ref iid, out nint interopPtr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var vtbl = *(void***)interopPtr;
            var createDesktopWindowTarget = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)vtbl[3];
            nint abi;
            Marshal.ThrowExceptionForHR(createDesktopWindowTarget(interopPtr, hwnd, 0, &abi));
            return MarshalInterface<DesktopWindowTarget>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(interopPtr);
        }
    }
}
