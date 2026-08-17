using System;

namespace camonlinux.Masking.Geometry;

/// <summary>
/// Pure, UI-free shape math. Every shape is evaluated as a signed distance to its
/// boundary (negative inside, positive outside, in pixels) so a single feathering
/// routine can produce soft, anti-aliased masks.
///
/// The signed-distance functions are the standard 2D SDFs (Iñigo Quílez style).
/// Ellipse uses the cheap "normalized" approximation — fine for masks with a feather;
/// superformula is an approximate radial distance (fine for soft edges).
/// </summary>
public static class ShapeGeometry
{
    // ------------------------------------------------------------------ //
    // Base size for the current scale type
    // ------------------------------------------------------------------ //

    /// <summary>The primary pixel size of the shape from the scale settings.</summary>
    public static double BaseSize(ShapeMaskSettings s, int frameW, int frameH)
    {
        return s.ScaleType switch
        {
            ScaleType.Width => s.WidthPx,
            ScaleType.Height => s.HeightPx,
            _ => (s.Scale / 100.0) * Math.Min(frameW, frameH),
        };
    }

    // ------------------------------------------------------------------ //
    // Shape signed distance (pixels; negative = inside)
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Precomputed polygon/star vertex array for the current shape (empty for shapes
    /// without discrete vertices). Called once per frame; reused by the per-pixel
    /// loop so nothing is allocated per pixel. Includes the scene-scale multiplier.
    /// </summary>
    public static (double X, double Y)[] ShapeVertices(ShapeMaskSettings s, int frameW, int frameH)
    {
        var size = BaseSize(s, frameW, frameH) * Math.Max(0.001, s.SceneScale);
        switch (s.Shape)
        {
            case ShapeKind.RegularPolygon:
                return RegularPolygonVertices(s.Sides, size / 2 * (s.PolygonRadius / 100.0));
            case ShapeKind.Star:
                return StarVertices(
                    s.StarPoints,
                    size / 2 * (s.StarOuter / 100.0),
                    size / 2 * (s.StarInner / 100.0));
            default:
                return Array.Empty<(double X, double Y)>();
        }
    }

    /// <summary>
    /// Signed distance (px) from the shape boundary; negative = inside.
    /// <paramref name="vertices"/> is the precomputed array from <see cref="ShapeVertices"/>
    /// (optional — recomputed when omitted, e.g. from tests).
    /// </summary>
    public static double ShapeDistance(double px, double py, ShapeMaskSettings s, int frameW, int frameH,
        ReadOnlySpan<(double X, double Y)> vertices = default)
    {
        var cx = (s.CenterX / 100.0) * frameW + s.PositionX;
        var cy = (s.CenterY / 100.0) * frameH + s.PositionY;
        var x = px - cx;
        var y = py - cy;
        var size = BaseSize(s, frameW, frameH) * Math.Max(0.001, s.SceneScale);

        switch (s.Shape)
        {
            case ShapeKind.Rectangle:
            {
                var halfW = size / 2;
                var halfH = halfW * 0.75;
                var r = s.CornerType == CornerRadiusType.Uniform
                    ? s.CornerRadius
                    : Math.Max(0, Math.Min(s.CornerTl, Math.Min(s.CornerTr, Math.Min(s.CornerBl, s.CornerBr))));
                return SdRoundBox(x, y, halfW, halfH, r);
            }
            case ShapeKind.Circle:
                return SdCircle(x, y, size / 2 * (s.Radius / 100.0));
            case ShapeKind.Ellipse:
            {
                var (rx, ry) = Rotate(x, y, s.EllipseRotation);
                var halfW = size / 2 * (s.EllipseWidth / 200.0);
                var halfH = size / 2 * (s.EllipseHeight / 120.0);
                return SdEllipse(rx, ry, halfW, halfH);
            }
            case ShapeKind.RegularPolygon:
            {
                var (rx, ry) = Rotate(x, y, s.PolygonRotation);
                var verts = vertices.Length > 0
                    ? vertices
                    : RegularPolygonVertices(s.Sides, size / 2 * (s.PolygonRadius / 100.0));
                return SdPolygon(rx, ry, verts) - s.PolygonCornerRadius;
            }
            case ShapeKind.Star:
            {
                var (rx, ry) = Rotate(x, y, s.StarRotation);
                var verts = vertices.Length > 0
                    ? vertices
                    : StarVertices(s.StarPoints, size / 2 * (s.StarOuter / 100.0), size / 2 * (s.StarInner / 100.0));
                return SdPolygon(rx, ry, verts) - s.StarCornerRadius;
            }
            case ShapeKind.Heart:
            {
                var (rx, ry) = Rotate(x, y, s.HeartRotation);
                var scale = size / 2 * (s.HeartSize / 100.0);
                return SdHeart(rx / (size / 2), ry / (size / 2)) * scale;
            }
            case ShapeKind.Superformula:
                return SuperformulaDistance(x, y, s, size / 2);
            default:
                return 1.0;
        }
    }

    // ------------------------------------------------------------------ //
    // Coverage from a signed distance + feather settings
    // ------------------------------------------------------------------ //

    /// <summary>Maps a signed distance (px) to a 0..1 coverage using the feather settings.</summary>
    public static double Coverage(double d, FeatherType feather, double amountPx)
    {
        var f = Math.Max(1.0, amountPx);
        switch (feather)
        {
            case FeatherType.Inner:
                return Clamp01(-d / f);           // soft transition entirely inside the shape
            case FeatherType.Outer:
                return Clamp01(1 - d / f);        // soft transition outside the boundary
            case FeatherType.Middle:
                return Clamp01(0.5 - d / f);      // band centred on the boundary
            default:
                return d <= 0 ? 1.0 : 0.0;        // hard edge
        }
    }

    // ------------------------------------------------------------------ //
    // SDFs
    // ------------------------------------------------------------------ //

    public static double SdCircle(double x, double y, double r) => Math.Sqrt(x * x + y * y) - r;

    public static double SdRoundBox(double x, double y, double halfW, double halfH, double radius)
    {
        var r = Math.Max(0.0, radius);
        var qx = Math.Abs(x) - (halfW - r);
        var qy = Math.Abs(y) - (halfH - r);
        var ax = Math.Max(qx, 0.0);
        var ay = Math.Max(qy, 0.0);
        return Math.Min(Math.Max(qx, qy), 0.0) + Math.Sqrt(ax * ax + ay * ay) - r;
    }

    /// <summary>Cheap ellipse approximation (normalised unit circle), adequate for feathered masks.</summary>
    public static double SdEllipse(double x, double y, double rx, double ry)
    {
        var s = Math.Min(rx, ry);
        var qx = x / Math.Max(0.0001, rx);
        var qy = y / Math.Max(0.0001, ry);
        return (Math.Sqrt(qx * qx + qy * qy) - 1.0) * s;
    }

    /// <summary>Signed distance to an arbitrary convex/concave polygon (mitered edges).</summary>
    public static double SdPolygon(double x, double y, ReadOnlySpan<(double X, double Y)> v)
    {
        if (v.Length == 0)
            return 1.0;
        var n = v.Length;
        var d = (x - v[0].X) * (x - v[0].X) + (y - v[0].Y) * (y - v[0].Y);
        var sign = 1.0;
        var j = n - 1;
        for (var i = 0; i < n; i++)
        {
            var ex = v[j].X - v[i].X;
            var ey = v[j].Y - v[i].Y;
            var wx = x - v[i].X;
            var wy = y - v[i].Y;
            var t = Clamp01((wx * ex + wy * ey) / Math.Max(1e-12, ex * ex + ey * ey));
            var bx = wx - ex * t;
            var by = wy - ey * t;
            d = Math.Min(d, bx * bx + by * by);

            var cross = (x - v[i].X) * (v[j].Y - v[i].Y) - (y - v[i].Y) * (v[j].X - v[i].X);
            sign = cross >= 0 ? sign : -sign;
            j = i;
        }
        return sign * Math.Sqrt(d);
    }

    public static (double X, double Y)[] RegularPolygonVertices(int sides, double radius)
    {
        var n = Math.Max(3, sides);
        var verts = new (double X, double Y)[n];
        for (var i = 0; i < n; i++)
        {
            var a = -Math.PI / 2 + i * 2 * Math.PI / n; // start at top
            verts[i] = (radius * Math.Cos(a), radius * Math.Sin(a));
        }
        return verts;
    }

    public static (double X, double Y)[] StarVertices(int points, double outer, double inner)
    {
        var n = Math.Max(2, points);
        var verts = new (double X, double Y)[n * 2];
        for (var i = 0; i < n * 2; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / n;
            var r = i % 2 == 0 ? outer : inner;
            verts[i] = (r * Math.Cos(a), r * Math.Sin(a));
        }
        return verts;
    }

    public static double SdHeart(double x, double y)
    {
        // iq heart SDF (y up, heart spans roughly [-1..1]).
        var ax = Math.Abs(x);
        if (y + ax > 1.0)
            return Math.Sqrt((ax - 0.25) * (ax - 0.25) + (y - 0.75) * (y - 0.75)) - 0.45;
        var d1 = ax * ax + (y - 1.0) * (y - 1.0);                       // dist to (0, 1)
        var qx = Math.Max(ax + ax - 1.0, 0.0);
        var qy = Math.Max(y + y, 0.0);
        var d2 = (ax - 0.5 * qx) * (ax - 0.5 * qx) + (y - 0.5 * qy) * (y - 0.5 * qy);
        return Math.Sqrt(Math.Min(d1, d2)) * Math.Sign(ax - y);
    }

    public static double SuperformulaDistance(double x, double y, ShapeMaskSettings s, double scale)
    {
        var rho = Math.Sqrt(x * x + y * y);
        if (rho < 1e-9)
            return -scale;
        var theta = Math.Atan2(y, x);

        var a = Math.Max(1e-9, s.A);
        var b = Math.Max(1e-9, s.B);
        var n1 = Math.Max(1e-9, s.N1);
        var n2 = Math.Max(1e-9, s.N2);
        var n3 = Math.Max(1e-9, s.N3);
        var m = s.M;

        var half = m * theta / 4.0;
        var t1 = Math.Pow(Math.Abs(Math.Cos(half) / a), n2);
        var t2 = Math.Pow(Math.Abs(Math.Sin(half) / b), n3);
        var r = Math.Pow(t1 + t2, -1.0 / n1);

        return rho - scale * r;
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static (double X, double Y) Rotate(double x, double y, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var c = Math.Cos(rad);
        var s = Math.Sin(rad);
        return (x * c - y * s, x * s + y * c);
    }
}
