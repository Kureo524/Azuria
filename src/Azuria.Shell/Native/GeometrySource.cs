using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI.Composition;
using WinRT;

namespace Azuria.Shell.Native;

/// <summary>
/// Fournit au compositeur une forme vectorielle (géométrie Direct2D) pour un
/// <see cref="CompositionPath"/> : c'est le mécanisme documenté (<c>IGeometrySource2DInterop</c>)
/// pour découper un visuel suivant un contour quelconque, avec des bords lissés. Le flou « host
/// backdrop » ne peut pas passer par un masque : seule une découpe géométrique le rogne.
///
/// L'objet COM est construit à la main (vtables natives), sans dépendance : il expose
/// IGeometrySource2D (marqueur WinRT) et IGeometrySource2DInterop, et construit la géométrie à la
/// demande — union (remplissage « winding ») des pièces d'une <see cref="GlassShape"/>.
/// </summary>
internal static unsafe class GeometrySource
{
    /// <summary>Crée le chemin de composition d'une forme (union de ses pièces).</summary>
    public static CompositionPath CreatePath(GlassShape shape)
    {
        nint obj = Create(shape.Pieces.ToArray());
        try
        {
            var source = MarshalInterface<IGeometrySource2D>.FromAbi(obj);
            return new CompositionPath(source);
        }
        finally { Marshal.Release(obj); }
    }

    // ----- objet COM -----

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    private static readonly Guid IID_IGeometrySource2D = new("CAFF7902-670C-4181-A624-DA977203B845");
    private static readonly Guid IID_IGeometrySource2DInterop = new("0657AF73-53FD-47CF-84FF-C8492D2A80A3");

    private const int S_OK = 0, E_NOINTERFACE = unchecked((int)0x80004002), E_POINTER = unchecked((int)0x80004003);

    [StructLayout(LayoutKind.Sequential)]
    private struct Obj
    {
        public void* VtblInspectable; // IUnknown / IInspectable / IGeometrySource2D / IAgileObject
        public void* VtblInterop;     // IGeometrySource2DInterop
        public int RefCount;
        public nint Pieces;           // GCHandle vers GlassShape.Piece[]
    }

    private static readonly void** s_vtblInspectable = BuildInspectableVtbl();
    private static readonly void** s_vtblInterop = BuildInteropVtbl();

    private static void** BuildInspectableVtbl()
    {
        var v = (void**)NativeMemory.Alloc((nuint)(6 * sizeof(void*)));
        v[0] = (delegate* unmanaged<Obj*, Guid*, void**, int>)&QI;
        v[1] = (delegate* unmanaged<Obj*, uint>)&AddRef;
        v[2] = (delegate* unmanaged<Obj*, uint>)&Release;
        v[3] = (delegate* unmanaged<Obj*, uint*, Guid**, int>)&GetIids;
        v[4] = (delegate* unmanaged<Obj*, nint*, int>)&GetRuntimeClassName;
        v[5] = (delegate* unmanaged<Obj*, int*, int>)&GetTrustLevel;
        return v;
    }

    private static void** BuildInteropVtbl()
    {
        var v = (void**)NativeMemory.Alloc((nuint)(5 * sizeof(void*)));
        v[0] = (delegate* unmanaged<byte*, Guid*, void**, int>)&QI2;
        v[1] = (delegate* unmanaged<byte*, uint>)&AddRef2;
        v[2] = (delegate* unmanaged<byte*, uint>)&Release2;
        v[3] = (delegate* unmanaged<byte*, nint*, int>)&GetGeometry;
        v[4] = (delegate* unmanaged<byte*, nint, nint*, int>)&TryGetGeometryUsingFactory;
        return v;
    }

    private static nint Create(GlassShape.Piece[] pieces)
    {
        var o = (Obj*)NativeMemory.AllocZeroed((nuint)sizeof(Obj));
        o->VtblInspectable = s_vtblInspectable;
        o->VtblInterop = s_vtblInterop;
        o->RefCount = 1;
        o->Pieces = GCHandle.ToIntPtr(GCHandle.Alloc(pieces));
        return (nint)o;
    }

    private static Obj* FromInterop(byte* p) => (Obj*)(p - sizeof(void*));

    [UnmanagedCallersOnly]
    private static int QI(Obj* o, Guid* iid, void** ppv)
    {
        if (ppv == null) return E_POINTER;
        Guid g = *iid;
        if (g == IID_IUnknown || g == IID_IInspectable || g == IID_IGeometrySource2D || g == IID_IAgileObject)
            *ppv = o;
        else if (g == IID_IGeometrySource2DInterop)
            *ppv = &o->VtblInterop;
        else { *ppv = null; return E_NOINTERFACE; }
        Interlocked.Increment(ref o->RefCount);
        return S_OK;
    }

    [UnmanagedCallersOnly] private static uint AddRef(Obj* o) => (uint)Interlocked.Increment(ref o->RefCount);

    [UnmanagedCallersOnly]
    private static uint Release(Obj* o)
    {
        int n = Interlocked.Decrement(ref o->RefCount);
        if (n == 0)
        {
            GCHandle.FromIntPtr(o->Pieces).Free();
            NativeMemory.Free(o);
        }
        return (uint)n;
    }

    [UnmanagedCallersOnly]
    private static int GetIids(Obj* o, uint* count, Guid** iids) { *count = 0; *iids = null; return S_OK; }

    [UnmanagedCallersOnly]
    private static int GetRuntimeClassName(Obj* o, nint* name) { *name = 0; return S_OK; }

    [UnmanagedCallersOnly]
    private static int GetTrustLevel(Obj* o, int* level) { *level = 0; return S_OK; }

    // Entrées de la 2e vtable : « this » pointe sur VtblInterop, on revient au début de l'objet.
    [UnmanagedCallersOnly]
    private static int QI2(byte* p, Guid* iid, void** ppv)
    {
        var vtbl = (delegate* unmanaged<Obj*, Guid*, void**, int>)s_vtblInspectable[0];
        return vtbl(FromInterop(p), iid, ppv);
    }

    [UnmanagedCallersOnly] private static uint AddRef2(byte* p) => (uint)Interlocked.Increment(ref FromInterop(p)->RefCount);

    [UnmanagedCallersOnly]
    private static uint Release2(byte* p)
    {
        var vtbl = (delegate* unmanaged<Obj*, uint>)s_vtblInspectable[2];
        return vtbl(FromInterop(p));
    }

    [UnmanagedCallersOnly]
    private static int GetGeometry(byte* p, nint* geometry)
    {
        try
        {
            *geometry = D2D.Build(D2D.SharedFactory, Pieces(FromInterop(p)));
            return S_OK;
        }
        catch (Exception e) { *geometry = 0; return e.HResult; }
    }

    [UnmanagedCallersOnly]
    private static int TryGetGeometryUsingFactory(byte* p, nint factory, nint* geometry)
    {
        try
        {
            *geometry = factory == 0 ? 0 : D2D.Build(factory, Pieces(FromInterop(p)));
            return S_OK;
        }
        catch (Exception e) { *geometry = 0; return e.HResult; }
    }

    private static GlassShape.Piece[] Pieces(Obj* o) => (GlassShape.Piece[])GCHandle.FromIntPtr(o->Pieces).Target!;

    // ----- Direct2D (appels par vtable) -----

    private static class D2D
    {
        [StructLayout(LayoutKind.Sequential)] private struct PointF { public float X, Y; public PointF(float x, float y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct RectF { public float L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] private struct RoundedRect { public RectF Rect; public float Rx, Ry; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ArcSegment { public PointF Point; public float W, H; public float Rotation; public int Sweep; public int Size; }

        [DllImport("d2d1.dll")]
        private static extern int D2D1CreateFactory(int type, in Guid riid, nint options, out nint factory);

        private static readonly Guid IID_ID2D1Factory = new("06152247-6F50-465A-9245-118BFD3B6007");
        private const int D2D1_FACTORY_TYPE_MULTI_THREADED = 1;
        private const int D2D1_FILL_MODE_WINDING = 1;

        private static nint s_factory;
        public static nint SharedFactory
        {
            get
            {
                if (s_factory == 0)
                    Marshal.ThrowExceptionForHR(D2D1CreateFactory(D2D1_FACTORY_TYPE_MULTI_THREADED, IID_ID2D1Factory, 0, out s_factory));
                return s_factory;
            }
        }

        private static void** V(nint p) => *(void***)p;

        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

        /// <summary>Union des pièces (groupe en remplissage « winding », toutes orientées pareil).</summary>
        public static nint Build(nint factory, GlassShape.Piece[] pieces)
        {
            var parts = new nint[pieces.Length];
            try
            {
                for (int i = 0; i < pieces.Length; i++) parts[i] = Piece(factory, pieces[i]);
                nint group;
                fixed (nint* arr = parts)
                {
                    var create = (delegate* unmanaged[Stdcall]<nint, int, nint*, uint, nint*, int>)V(factory)[8];
                    Check(create(factory, D2D1_FILL_MODE_WINDING, arr, (uint)parts.Length, &group));
                }
                return group;
            }
            finally
            {
                foreach (var g in parts) if (g != 0) Marshal.Release(g); // le groupe garde ses références
            }
        }

        private static nint Piece(nint factory, GlassShape.Piece piece)
        {
            nint g;
            switch (piece)
            {
                case GlassShape.Rect r:
                {
                    var rc = new RectF { L = r.X, T = r.Y, R = r.X + r.W, B = r.Y + r.H };
                    var create = (delegate* unmanaged[Stdcall]<nint, RectF*, nint*, int>)V(factory)[5];
                    Check(create(factory, &rc, &g));
                    return g;
                }
                case GlassShape.Round r:
                {
                    var rr = new RoundedRect { Rect = new RectF { L = r.X, T = r.Y, R = r.X + r.W, B = r.Y + r.H }, Rx = r.R, Ry = r.R };
                    var create = (delegate* unmanaged[Stdcall]<nint, RoundedRect*, nint*, int>)V(factory)[6];
                    Check(create(factory, &rr, &g));
                    return g;
                }
                case GlassShape.Fillet f:
                    return Fillet(factory, f);
                default:
                    throw new NotSupportedException();
            }
        }

        // Congé : le carré privé du quart de disque centré sur son coin C. Contour : coin opposé O,
        // coin adjacent A, arc de cercle (centre C) jusqu'à l'autre coin adjacent B, retour en O.
        // Parcouru dans le sens horaire, comme les rectangles de Direct2D (sinon l'union s'annule).
        private static nint Fillet(nint factory, GlassShape.Fillet f)
        {
            float x0 = f.X, y0 = f.Y, x1 = f.X + f.R, y1 = f.Y + f.R;
            var c = new PointF(f.Cx, f.Cy);
            var o = new PointF(Near(f.Cx, x0, x1) ? x1 : x0, Near(f.Cy, y0, y1) ? y1 : y0);
            var a = new PointF(o.X, c.Y);  // sur le bord vertical du carré, à hauteur du centre
            var b = new PointF(c.X, o.Y);  // sur le bord horizontal

            // Sens horaire (axe y vers le bas) = aire signée positive du triangle O, A, B.
            float area = (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            if (area < 0) (a, b) = (b, a);
            // Arc de A vers B autour de C : horaire si le produit vectoriel (A-C)×(B-C) est positif.
            float cross = (a.X - c.X) * (b.Y - c.Y) - (a.Y - c.Y) * (b.X - c.X);

            nint path;
            var createPath = (delegate* unmanaged[Stdcall]<nint, nint*, int>)V(factory)[10];
            Check(createPath(factory, &path));
            nint sink;
            var open = (delegate* unmanaged[Stdcall]<nint, nint*, int>)V(path)[17];
            Check(open(path, &sink));
            try
            {
                var vs = V(sink);
                ((delegate* unmanaged[Stdcall]<nint, PointF, int, void>)vs[5])(sink, o, 0);  // BeginFigure, rempli
                ((delegate* unmanaged[Stdcall]<nint, PointF, void>)vs[10])(sink, a);         // AddLine
                var arc = new ArcSegment { Point = b, W = f.R, H = f.R, Rotation = 0, Sweep = cross > 0 ? 1 : 0, Size = 0 };
                ((delegate* unmanaged[Stdcall]<nint, ArcSegment*, void>)vs[14])(sink, &arc); // AddArc
                ((delegate* unmanaged[Stdcall]<nint, int, void>)vs[8])(sink, 1);             // EndFigure, fermé
                Check(((delegate* unmanaged[Stdcall]<nint, int>)vs[9])(sink));               // Close
            }
            finally { Marshal.Release(sink); }
            return path;

            static bool Near(float v, float lo, float hi) => Math.Abs(v - lo) < Math.Abs(v - hi);
        }
    }
}
