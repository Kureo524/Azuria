using System.Numerics;
using System.Runtime.InteropServices;
using Azuria.Shell.Native;
using Color = Windows.UI.Color;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Azuria.Shell;

/// <summary>
/// Forme du verre d'une fenêtre, en pixels physiques (coordonnées de la fenêtre) : une union de
/// rectangles, de rectangles arrondis et de congés concaves (carré privé d'un quart de disque).
/// De quoi décrire le cadre de l'écran et les tiroirs qui en sortent (organisation façon Caelestia).
/// </summary>
internal sealed class GlassShape
{
    internal abstract record Piece;
    internal sealed record Rect(float X, float Y, float W, float H) : Piece;
    internal sealed record Round(float X, float Y, float W, float H, float R) : Piece;
    /// <summary>Carré (X, Y, R×R) privé du disque de rayon R centré en (Cx, Cy), l'un de ses coins.</summary>
    internal sealed record Fillet(float X, float Y, float R, float Cx, float Cy) : Piece;

    public List<Piece> Pieces { get; } = new();

    public GlassShape Rectangle(float x, float y, float w, float h)
    {
        if (w > 0 && h > 0) Pieces.Add(new Rect(x, y, w, h));
        return this;
    }

    /// <summary>
    /// Corps de rayon <paramref name="r"/> dont seuls les coins demandés sont arrondis
    /// (tl = haut gauche, tr = haut droit, br = bas droit, bl = bas gauche).
    /// </summary>
    public GlassShape Body(float x, float y, float w, float h, float r, bool tl, bool tr, bool br, bool bl)
    {
        if (w <= 0 || h <= 0) return this;
        r = Math.Min(r, Math.Min(w, h) / 2);
        if (r <= 0 || !(tl || tr || br || bl)) return Rectangle(x, y, w, h);
        Pieces.Add(new Round(x, y, w, h, r));
        // Coins voulus carrés : on les recouvre d'un carré plein.
        if (!tl) Rectangle(x, y, r, r);
        if (!tr) Rectangle(x + w - r, y, r, r);
        if (!br) Rectangle(x + w - r, y + h - r, r, r);
        if (!bl) Rectangle(x, y + h - r, r, r);
        return this;
    }

    /// <summary>Congé concave dans le carré (x, y, r×r), creusé autour de son coin (cx, cy).</summary>
    public GlassShape Concave(float x, float y, float r, float cx, float cy)
    {
        if (r > 0) Pieces.Add(new Fillet(x, y, r, cx, cy));
        return this;
    }
}

/// <summary>
/// Le verre composé par Azuria sur une fenêtre à nous : flou de l'arrière-plan (pinceau « host
/// backdrop » de Windows) + voile <c>--surface</c>, découpés suivant le contour exact de la forme,
/// lissé (géométrie Direct2D, <see cref="GeometrySource"/>). Remplace l'Acrylic de DWM, qui ignore
/// toute découpe de fenêtre et retombe sur sa couleur de repli (gris plat) dès que la fenêtre n'est
/// pas active — or nos panneaux ne prennent jamais le focus. API documentées uniquement, sur nos
/// propres fenêtres (voir CLAUDE.md).
///
/// La fenêtre doit avoir <c>WS_EX_NOREDIRECTIONBITMAP</c> : seuls ces visuels et les fenêtres
/// enfants (WebView2) sont affichés. Une région de fenêtre, un pixel plus large que la forme, rend
/// le reste transparent aux clics sans rogner le lissage.
/// </summary>
internal sealed class GlassSurface
{
    // DA §3 : --surface rgba(16, 18, 25, 0.62) ; --solid rgb(16, 18, 25) (repli sans flou).
    private static readonly Color Surface = Color.FromArgb(158, 16, 18, 25);
    private static readonly Color Solid = Color.FromArgb(255, 16, 18, 25);

    private readonly nint _hwnd;
    private readonly Compositor _c;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _glass;
    private readonly SpriteVisual _backdrop;
    private bool _blur = true;

    public GlassSurface(nint hwnd)
    {
        _hwnd = hwnd;
        _c = CompositionHost.Compositor;
        Dwm.EnableHostBackdrop(hwnd);

        _target = CompositionHost.CreateTarget(hwnd);
        _glass = _c.CreateContainerVisual(); // découpé à la forme (Clip)
        _glass.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _glass;

        _backdrop = _c.CreateSpriteVisual();
        _backdrop.Brush = _c.CreateHostBackdropBrush();
        _backdrop.RelativeSizeAdjustment = Vector2.One;

        var tint = _c.CreateSpriteVisual();
        tint.Brush = _c.CreateColorBrush(Surface);
        tint.RelativeSizeAdjustment = Vector2.One;

        _glass.Children.InsertAtTop(_backdrop);
        _glass.Children.InsertAtTop(tint);
    }

    /// <summary>Flou actif (true) ou repli opaque <c>--solid</c> (transparence de Windows coupée).</summary>
    public bool Blur
    {
        get => _blur;
        set
        {
            if (_blur == value) return;
            _blur = value;
            _backdrop.Brush = value ? _c.CreateHostBackdropBrush() : _c.CreateColorBrush(Solid);
        }
    }

    /// <summary>Applique une forme à une fenêtre de <paramref name="w"/>×<paramref name="h"/> px.</summary>
    public void SetShape(GlassShape shape, int w, int h)
    {
        var geometry = _c.CreatePathGeometry(GeometrySource.CreatePath(shape));
        _glass.Clip = _c.CreateGeometricClip(geometry);
        ApplyRegion(shape);
    }

    // ----- région (clics) -----

    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern nint CreateEllipticRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint a, nint b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint rgn, bool redraw);
    private const int RGN_OR = 2, RGN_DIFF = 4;

    // Région = la forme élargie d'un pixel : hors d'elle, les clics passent aux fenêtres dessous.
    private void ApplyRegion(GlassShape shape)
    {
        nint all = CreateRectRgn(0, 0, 0, 0);
        foreach (var p in shape.Pieces)
        {
            nint part;
            switch (p)
            {
                case GlassShape.Rect r:
                    part = CreateRectRgn(F(r.X) - 1, F(r.Y) - 1, C(r.X + r.W) + 1, C(r.Y + r.H) + 1);
                    break;
                case GlassShape.Round r:
                    int d = (int)(2 * r.R);
                    part = CreateRoundRectRgn(F(r.X) - 1, F(r.Y) - 1, C(r.X + r.W) + 2, C(r.Y + r.H) + 2, d, d);
                    break;
                case GlassShape.Fillet f:
                    part = CreateRectRgn(F(f.X) - 1, F(f.Y) - 1, C(f.X + f.R) + 1, C(f.Y + f.R) + 1);
                    float rr = f.R - 1;
                    nint disk = CreateEllipticRgn(C(f.Cx - rr), C(f.Cy - rr), F(f.Cx + rr), F(f.Cy + rr));
                    CombineRgn(part, part, disk, RGN_DIFF);
                    DeleteObject(disk);
                    break;
                default: continue;
            }
            CombineRgn(all, all, part, RGN_OR);
            DeleteObject(part);
        }
        // Après SetWindowRgn, la région appartient au système : pas de DeleteObject.
        SetWindowRgn(_hwnd, all, true);

        static int F(float v) => (int)Math.Floor(v);
        static int C(float v) => (int)Math.Ceiling(v);
    }
}
