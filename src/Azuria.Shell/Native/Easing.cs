namespace Azuria.Shell.Native;

/// <summary>Courbes de mouvement de la DA (§11), communes aux tiroirs et au pavage des fenêtres.</summary>
internal static class Easing
{
    /// <summary>Entrée, déplacement : décélération (cubic-bezier(0.2, 0, 0, 1)).</summary>
    public static double In(double p) => Bezier(0.2, 0, 0, 1, p);

    /// <summary>Sortie : accélération (cubic-bezier(0.3, 0, 1, 1)).</summary>
    public static double Out(double p) => Bezier(0.3, 0, 1, 1, p);

    public static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);

    // Courbe de Bézier cubique (x->y) résolue par Newton, comme en CSS.
    private static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        x = Math.Clamp(x, 0, 1);
        double t = x;
        for (int i = 0; i < 6; i++)
        {
            double xt = Bez(x1, x2, t) - x;
            double dxt = BezD(x1, x2, t);
            if (Math.Abs(dxt) < 1e-6) break;
            t = Math.Clamp(t - xt / dxt, 0, 1);
        }
        return Bez(y1, y2, t);

        static double Bez(double a, double b, double t) =>
            3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        static double BezD(double a, double b, double t) =>
            3 * (1 - t) * (1 - t) * a + 6 * (1 - t) * t * (b - a) + 3 * t * t * (1 - b);
    }
}
