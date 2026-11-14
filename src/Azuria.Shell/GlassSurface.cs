using System.Numerics;
using System.Runtime.InteropServices;
using Azuria.Shell.Native;
using Color = Windows.UI.Color;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Azuria.Shell;

/// <summary>
/// Forme de verre, en pixels physiques : une union de rectangles, de rectangles arrondis et de
/// congés concaves (carré privé d'un quart de disque). De quoi décrire le cadre de l'écran et les
/// tiroirs qui en sortent (organisation façon Caelestia).
/// </summary>
internal sealed class GlassShape
{
    internal abstract record Piece;
    internal sealed record Rect(float X, float Y, float W, float H) : Piece;
    internal sealed record Round(float X, float Y, float W, float H, float R) : Piece;
    /// <summary>Carré (X, Y, R×R) privé du disque de rayon R centré en (Cx, Cy), l'un de ses coins.</summary>
    internal sealed record Fillet(float X, float Y, float R, float Cx, float Cy) : Piece;
    /// <summary>Polygone fermé (sert aux bandes de fondu, pas aux formes des tiroirs).</summary>
    internal sealed record Polygon(PointF[] Points) : Piece;

    public List<Piece> Pieces { get; } = new();

    public GlassShape Poly(params PointF[] points)
    {
        if (points.Length >= 3) Pieces.Add(new Polygon(points));
        return this;
    }

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
/// Le verre composé par Azuria, sur une seule fenêtre par écran (<see cref="ScreenSurface"/>) :
/// flou de l'arrière-plan (pinceau « host backdrop » de Windows) + voile <c>--surface</c>, découpés
/// suivant le contour exact des formes, lissé (géométrie Direct2D, <see cref="GeometrySource"/>).
///
/// Une seule fenêtre pour le cadre ET les tiroirs : le flou d'une fenêtre ne voit que ce qui est
/// derrière son propre rectangle, si bien que deux fenêtres voisines n'ont pas la même teinte à leur
/// jonction. Ici tout le verre partage le même flou, d'un seul tenant (comme Caelestia).
///
/// Remplace l'Acrylic de DWM, qui ignore toute découpe de fenêtre et retombe sur sa couleur de
/// repli (gris plat) dès que la fenêtre n'est pas active. API documentées uniquement, sur nos
/// propres fenêtres (voir CLAUDE.md). La fenêtre doit avoir <c>WS_EX_NOREDIRECTIONBITMAP</c>.
/// </summary>
internal sealed class GlassSurface
{
    // DA §3 et §9 : voile des tiroirs rgba(16, 18, 25, 0.62) ; cadre plus couvrant, à la valeur
    // --surface de la DA (0.84) ; --solid rgb(16, 18, 25) (repli sans flou).
    internal const float DrawerAlpha = 0.62f;
    internal const float FrameAlpha = 0.84f;
    internal static readonly Color Tint = Ink(DrawerAlpha);
    internal static readonly Color FrameTint = Ink(FrameAlpha);
    private static readonly Color Solid = Color.FromArgb(255, 16, 18, 25);

    /// <summary>
    /// Voile à poser par-dessus celui d'un tiroir pour retrouver, cumulé, l'opacité du cadre :
    /// 1 − (1 − cadre) / (1 − tiroir). Départ du fondu au bord collé au cadre.
    /// </summary>
    internal static readonly Color FadeStart = Ink(1 - (1 - FrameAlpha) / (1 - DrawerAlpha));
    internal static readonly Color FadeEnd = Ink(0);

    private static Color Ink(float alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), 16, 18, 25);

    private readonly Compositor _c;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly List<GlassLayer> _layers = new();
    private bool _blur = true;

    public GlassSurface(nint hwnd)
    {
        _c = CompositionHost.Compositor;
        Dwm.EnableHostBackdrop(hwnd);
        _target = CompositionHost.CreateTarget(hwnd);
        _root = _c.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _root;
    }

    /// <summary>Ajoute une couche de verre, couvrant au plus <paramref name="area"/> (coordonnées de la fenêtre).</summary>
    public GlassLayer AddLayer(Rectangle area, Color tint)
    {
        var holder = _c.CreateContainerVisual();
        holder.RelativeSizeAdjustment = Vector2.One;
        _root.Children.InsertAtTop(holder);
        var layer = new GlassLayer(this, _c, holder, tint);
        layer.SetArea(area);
        _layers.Add(layer);
        return layer;
    }

    internal CompositionBrush BackdropBrush() =>
        _blur ? _c.CreateHostBackdropBrush() : _c.CreateColorBrush(Solid);

    /// <summary>Flou actif (true) ou repli opaque <c>--solid</c> (transparence de Windows coupée).</summary>
    public bool Blur
    {
        get => _blur;
        set
        {
            if (_blur == value) return;
            _blur = value;
            foreach (var l in _layers) l.RefreshBackdrop();
        }
    }
}

/// <summary>Une forme de verre sur une <see cref="GlassSurface"/>, déplaçable sans tout recalculer.</summary>
internal sealed class GlassLayer
{
    private readonly GlassSurface _surface;
    private readonly Compositor _c;
    private readonly ContainerVisual _holder; // rogné à la zone permise
    private readonly ContainerVisual _glass;  // découpé à la forme, déplacé par Offset
    private readonly SpriteVisual _backdrop;
    private readonly SpriteVisual _tint;
    private readonly ContainerVisual _fade;   // bandes de fondu, liées à la forme (pas décalées)
    private Rectangle _area;

    internal GlassLayer(GlassSurface surface, Compositor c, ContainerVisual holder, Color tint)
    {
        _surface = surface;
        _c = c;
        _holder = holder;
        _glass = c.CreateContainerVisual();
        _holder.Children.InsertAtTop(_glass);

        // Flou et voile couvrent la zone permise, décalés à l'inverse de la forme : le flou reste
        // aligné sur l'écran pendant qu'elle se déplace.
        _backdrop = c.CreateSpriteVisual();
        _backdrop.Brush = surface.BackdropBrush();
        _tint = c.CreateSpriteVisual();
        _tint.Brush = c.CreateColorBrush(tint);
        _fade = c.CreateContainerVisual();
        _glass.Children.InsertAtTop(_backdrop);
        _glass.Children.InsertAtTop(_tint);
        _glass.Children.InsertAtTop(_fade);
        _glass.IsVisible = false;
    }

    /// <summary>
    /// Fondu le long des côtés collés au cadre : sur <paramref name="band"/> px, le voile passe de
    /// l'opacité du cadre à celle du tiroir, pour qu'aucune frontière nette ne marque la jonction.
    /// Coins en onglet (diagonale) là où deux côtés fondus se rencontrent : pas de double voile.
    /// </summary>
    public void SetFade(Size size, DrawerSides sides, int band)
    {
        _fade.Children.RemoveAll();
        float w = size.Width, h = size.Height, b = Math.Min(band, Math.Min(size.Width, size.Height) / 2);
        if (b <= 0 || sides == DrawerSides.None) return;

        float l = sides.HasFlag(DrawerSides.Left) ? b : 0, t = sides.HasFlag(DrawerSides.Top) ? b : 0;
        float r = sides.HasFlag(DrawerSides.Right) ? b : 0, bo = sides.HasFlag(DrawerSides.Bottom) ? b : 0;

        // Chaque bande : sa position, sa taille, le sens du dégradé et son contour (coordonnées locales).
        if (l > 0) Band(new(0, 0), new(b, h), new(0, 0), new(1, 0),
            new(0, 0), new(b, t), new(b, h - bo), new(0, h));
        if (t > 0) Band(new(0, 0), new(w, b), new(0, 0), new(0, 1),
            new(0, 0), new(w, 0), new(w - r, b), new(l, b));
        if (r > 0) Band(new(w - b, 0), new(b, h), new(1, 0), new(0, 0),
            new(b, 0), new(b, h), new(0, h - bo), new(0, t));
        if (bo > 0) Band(new(0, h - b), new(w, b), new(0, 1), new(0, 0),
            new(0, b), new(l, 0), new(w - r, 0), new(w, b));
    }

    private void Band(Vector2 at, Vector2 size, Vector2 from, Vector2 to, params PointF[] outline)
    {
        var brush = _c.CreateLinearGradientBrush();
        brush.StartPoint = from;
        brush.EndPoint = to;
        brush.ColorStops.Add(_c.CreateColorGradientStop(0, GlassSurface.FadeStart));
        brush.ColorStops.Add(_c.CreateColorGradientStop(1, GlassSurface.FadeEnd));
        var v = _c.CreateSpriteVisual();
        v.Brush = brush;
        v.Offset = new Vector3(at, 0);
        v.Size = size;
        v.Clip = _c.CreateGeometricClip(_c.CreatePathGeometry(
            GeometrySource.CreatePath(new GlassShape().Poly(outline))));
        _fade.Children.InsertAtTop(v);
    }

    internal void RefreshBackdrop() => _backdrop.Brush = _surface.BackdropBrush();

    /// <summary>Zone (coordonnées de la fenêtre) hors de laquelle la couche est rognée.</summary>
    public void SetArea(Rectangle area)
    {
        _area = area;
        var clip = _c.CreateInsetClip();
        clip.LeftInset = area.Left;
        clip.TopInset = area.Top;
        _holder.Clip = clip;
        // Le reste (droite, bas) : on borne la taille du conteneur.
        _holder.RelativeSizeAdjustment = Vector2.Zero;
        _holder.Size = new Vector2(area.Right, area.Bottom);
        foreach (var v in new[] { _backdrop, _tint }) v.Size = new Vector2(area.Right, area.Bottom);
    }

    /// <summary>Forme, en coordonnées de la couche.</summary>
    public void SetShape(GlassShape shape) =>
        _glass.Clip = _c.CreateGeometricClip(_c.CreatePathGeometry(GeometrySource.CreatePath(shape)));

    /// <summary>Place la couche (coordonnées de la fenêtre hôte) et l'affiche ou la masque.</summary>
    public void Place(Point at, bool visible)
    {
        _glass.Offset = new Vector3(at.X, at.Y, 0);
        _backdrop.Offset = _tint.Offset = new Vector3(-at.X, -at.Y, 0);
        _glass.IsVisible = visible;
    }
}

/// <summary>Régions de fenêtre (clics) tirées d'une forme de verre.</summary>
internal static class ShapeRegion
{
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern nint CreateEllipticRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint a, nint b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint rgn, bool redraw);
    private const int RGN_AND = 1, RGN_OR = 2, RGN_DIFF = 4;

    /// <summary>
    /// Région = la forme élargie d'un pixel (hors d'elle, les clics passent aux fenêtres dessous),
    /// limitée à <paramref name="within"/> (coordonnées de la fenêtre) si donné.
    /// </summary>
    public static void Apply(nint hwnd, GlassShape shape, Rectangle? within = null)
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
        if (within is Rectangle w)
        {
            nint box = CreateRectRgn(w.Left, w.Top, w.Right, w.Bottom);
            CombineRgn(all, all, box, RGN_AND);
            DeleteObject(box);
        }
        // Après SetWindowRgn, la région appartient au système : pas de DeleteObject.
        SetWindowRgn(hwnd, all, true);

        static int F(float v) => (int)Math.Floor(v);
        static int C(float v) => (int)Math.Ceiling(v);
    }
}
