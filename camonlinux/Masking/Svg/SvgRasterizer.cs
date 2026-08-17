using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace camonlinux.Masking.Svg;

/// <summary>
/// Minimal SVG → raster-mask renderer (SkiaSharp). Supports the element set that
/// covers typical mask/icon SVGs: <c>rect, circle, ellipse, polygon, polyline,
/// line, path</c>. Path commands supported: M/L/H/V/C/S/Q/T/Z (arcs <c>A</c> are
/// drawn as straight lines — rare in masks). All shapes are filled; white =
/// opaque mask. Parsing is deliberately small and allocation-light per frame; the
/// caller caches the produced bitmap until the SVG text changes.
/// </summary>
public static class SvgRasterizer
{
    private const int MaxRaster = 1024;

    private static readonly Regex s_svgTag = new(@"<svg\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_element = new(
        @"<(rect|circle|ellipse|polygon|polyline|line|path)\b([^>]*?)/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Rasterizes an SVG string to a grayscale mask (white = 1), or null on error/empty.</summary>
    public static SKBitmap? Rasterize(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg))
            return null;

        try
        {
            var root = s_svgTag.Match(svg);
            var rootAttrs = root.Success ? ParseAttrs(root.Value) : new Dictionary<string, string>();
            var viewBox = ParseViewBox(rootAttrs);

            var logicalW = GetLength(rootAttrs, "width", viewBox is null ? 512.0 : viewBox.Value.Width);
            var logicalH = GetLength(rootAttrs, "height", viewBox is null ? 512.0 : viewBox.Value.Height);

            var px = (int)Math.Clamp(Math.Ceiling(logicalW), 1, MaxRaster);
            var py = (int)Math.Clamp(Math.Ceiling(logicalH), 1, MaxRaster);

            using var path = new SKPath();
            // Map logical (viewBox/width-height) coords into raster pixels.
            var sx = px / (viewBox is null ? logicalW : viewBox.Value.Width);
            var sy = py / (viewBox is null ? logicalH : viewBox.Value.Height);
            var ox = viewBox is null ? 0.0f : (float)(-viewBox.Value.MinX * sx);
            var oy = viewBox is null ? 0.0f : (float)(-viewBox.Value.MinY * sy);

            foreach (Match m in s_element.Matches(svg))
            {
                var kind = m.Groups[1].Value.ToLowerInvariant();
                var attrs = ParseAttrs(m.Groups[2].Value);
                BuildShape(path, kind, attrs, (float)sx, (float)sy, ox, oy);
            }

            if (path.IsEmpty)
                return null;

            var bitmap = new SKBitmap(new SKImageInfo(px, py, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using var fill = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = SKColors.White,
                    IsAntialias = true
                };
                canvas.DrawPath(path, fill);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void BuildShape(
        SKPath path, string kind, Dictionary<string, string> a,
        float sx, float sy, float ox, float oy)
    {
        switch (kind)
        {
            case "rect":
            {
                var x = Num(a, "x") * sx + ox;
                var y = Num(a, "y") * sy + oy;
                var w = Num(a, "width") * sx;
                var h = Num(a, "height") * sy;
                var rx = Has(a, "rx") ? Num(a, "rx") * sx : (Has(a, "ry") ? Num(a, "ry") * sx : 0);
                var rect = new SKRect((float)x, (float)y, (float)(x + w), (float)(y + h));
                if (rx > 0)
                    path.AddRoundRect(rect, (float)rx, (float)rx);
                else
                    path.AddRect(rect);
                break;
            }
            case "circle":
            {
                var cx = Num(a, "cx") * sx + ox;
                var cy = Num(a, "cy") * sy + oy;
                var r = Num(a, "r") * Math.Min(sx, sy);
                path.AddCircle((float)cx, (float)cy, (float)r);
                break;
            }
            case "ellipse":
            {
                var cx = Num(a, "cx") * sx + ox;
                var cy = Num(a, "cy") * sy + oy;
                var rx = Num(a, "rx") * sx;
                var ry = Num(a, "ry") * sy;
                path.AddOval(new SKRect((float)(cx - rx), (float)(cy - ry), (float)(cx + rx), (float)(cy + ry)));
                break;
            }
            case "polygon":
            case "polyline":
            {
                var pts = ParsePoints(a.GetValueOrDefault("points") ?? "");
                if (pts.Count < 2)
                    return;
                path.MoveTo(pts[0].X * sx + ox, pts[0].Y * sy + oy);
                for (var i = 1; i < pts.Count; i++)
                    path.LineTo(pts[i].X * sx + ox, pts[i].Y * sy + oy);
                if (kind == "polygon")
                    path.Close();
                break;
            }
            case "line":
            {
                path.MoveTo((float)(Num(a, "x1") * sx + ox), (float)(Num(a, "y1") * sy + oy));
                path.LineTo((float)(Num(a, "x2") * sx + ox), (float)(Num(a, "y2") * sy + oy));
                break;
            }
            case "path":
            {
                ParsePath(path, a.GetValueOrDefault("d") ?? "", sx, sy, ox, oy);
                break;
            }
        }
    }

    /// <summary>Parses SVG path 'd' data (M/L/H/V/C/S/Q/T/Z). Arcs (A) become lines.</summary>
    private static void ParsePath(SKPath path, string d, float sx, float sy, float ox, float oy)
    {
        if (string.IsNullOrWhiteSpace(d))
            return;

        var tokens = Tokenize(d);
        var i = 0;
        float cx = 0, cy = 0;          // current point
        float startX = 0, startY = 0;  // subpath start (for Z)
        float lastCx = 0, lastCy = 0;  // last control point (for S/T)
        var haveCur = false;

        while (i < tokens.Count)
        {
            var cmd = tokens[i].ToUpperInvariant();
            var isRel = tokens[i] != cmd;
            i++;

            switch (cmd)
            {
                case "M":
                {
                    while (i + 1 < tokens.Count && IsNumber(tokens[i]))
                    {
                        var x = F(tokens[i]) * sx + ox;
                        var y = F(tokens[i + 1]) * sy + oy;
                        if (isRel) { x += cx; y += cy; }
                        path.MoveTo(x, y);
                        cx = x; cy = y; startX = x; startY = y;
                        i += 2;
                        if (i < tokens.Count && IsNumber(tokens[i]))
                        {
                            // subsequent pairs are implicit L
                            while (i + 1 < tokens.Count && IsNumber(tokens[i]))
                            {
                                x = F(tokens[i]) * sx + ox; y = F(tokens[i + 1]) * sy + oy;
                                if (isRel) { x += cx; y += cy; }
                                path.LineTo(x, y);
                                cx = x; cy = y; i += 2;
                            }
                        }
                    }
                    break;
                }
                case "L":
                {
                    while (i + 1 < tokens.Count && IsNumber(tokens[i]))
                    {
                        var x = F(tokens[i]) * sx + ox;
                        var y = F(tokens[i + 1]) * sy + oy;
                        if (isRel) { x += cx; y += cy; }
                        path.LineTo(x, y);
                        cx = x; cy = y; i += 2;
                    }
                    break;
                }
                case "H":
                {
                    while (i < tokens.Count && IsNumber(tokens[i]))
                    {
                        var x = F(tokens[i]) * sx + ox;
                        if (isRel) x += cx;
                        path.LineTo(x, cy);
                        cx = x; i++;
                    }
                    break;
                }
                case "V":
                {
                    while (i < tokens.Count && IsNumber(tokens[i]))
                    {
                        var y = F(tokens[i]) * sy + oy;
                        if (isRel) y += cy;
                        path.LineTo(cx, y);
                        cy = y; i++;
                    }
                    break;
                }
                case "C":
                {
                    while (i + 5 < tokens.Count && IsNumber(tokens[i]))
                    {
                        var x1 = F(tokens[i]) * sx + ox; var y1 = F(tokens[i + 1]) * sy + oy;
                        var x2 = F(tokens[i + 2]) * sx + ox; var y2 = F(tokens[i + 3]) * sy + oy;
                        var x3 = F(tokens[i + 4]) * sx + ox; var y3 = F(tokens[i + 5]) * sy + oy;
                        if (isRel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x3 += cx; y3 += cy; }
                        path.CubicTo(x1, y1, x2, y2, x3, y3);
                        lastCx = x2; lastCy = y2; haveCur = true;
                        cx = x3; cy = y3; i += 6;
                    }
                    break;
                }
                case "S":
                {
                    while (i + 3 < tokens.Count && IsNumber(tokens[i]))
                    {
                        float x1, y1;
                        if (haveCur) { x1 = 2 * cx - lastCx; y1 = 2 * cy - lastCy; }
                        else { x1 = cx; y1 = cy; }
                        var x2 = F(tokens[i]) * sx + ox; var y2 = F(tokens[i + 1]) * sy + oy;
                        var x3 = F(tokens[i + 2]) * sx + ox; var y3 = F(tokens[i + 3]) * sy + oy;
                        if (isRel) { x2 += cx; y2 += cy; x3 += cx; y3 += cy; }
                        path.CubicTo(x1, y1, x2, y2, x3, y3);
                        lastCx = x2; lastCy = y2; haveCur = true;
                        cx = x3; cy = y3; i += 4;
                    }
                    break;
                }
                case "Q":
                {
                    while (i + 3 < tokens.Count && IsNumber(tokens[i]))
                    {
                        var x1 = F(tokens[i]) * sx + ox; var y1 = F(tokens[i + 1]) * sy + oy;
                        var x2 = F(tokens[i + 2]) * sx + ox; var y2 = F(tokens[i + 3]) * sy + oy;
                        if (isRel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; }
                        path.QuadTo(x1, y1, x2, y2);
                        lastCx = x1; lastCy = y1; haveCur = true;
                        cx = x2; cy = y2; i += 4;
                    }
                    break;
                }
                case "T":
                {
                    while (i + 1 < tokens.Count && IsNumber(tokens[i]))
                    {
                        float x1, y1;
                        if (haveCur) { x1 = 2 * cx - lastCx; y1 = 2 * cy - lastCy; }
                        else { x1 = cx; y1 = cy; }
                        var x2 = F(tokens[i]) * sx + ox; var y2 = F(tokens[i + 1]) * sy + oy;
                        if (isRel) { x2 += cx; y2 += cy; }
                        path.QuadTo(x1, y1, x2, y2);
                        lastCx = x1; lastCy = y1; haveCur = true;
                        cx = x2; cy = y2; i += 2;
                    }
                    break;
                }
                case "A":
                {
                    // SVG arcs: approximated with a straight line (rare in mask SVGs).
                    while (i + 6 < tokens.Count && IsNumber(tokens[i]))
                    {
                        var x = F(tokens[i + 5]) * sx + ox;
                        var y = F(tokens[i + 6]) * sy + oy;
                        if (isRel) { x += cx; y += cy; }
                        path.LineTo(x, y);
                        cx = x; cy = y; i += 7;
                    }
                    break;
                }
                case "Z":
                    path.Close();
                    cx = startX; cy = startY;
                    break;
                default:
                    // Skip unknown command letters (and any trailing numbers).
                    while (i < tokens.Count && IsNumber(tokens[i])) i++;
                    break;
            }
        }
    }

    private static List<string> Tokenize(string d)
    {
        var list = new List<string>();
        var current = "";
        foreach (var ch in d)
        {
            if (char.IsLetter(ch))
            {
                if (current.Length > 0) { list.Add(current); current = ""; }
                list.Add(ch.ToString());
            }
            else if (ch == '-' || ch == '+' || ch == '.' || char.IsDigit(ch))
            {
                current += ch;
            }
            else if (ch == ',' || char.IsWhiteSpace(ch))
            {
                if (current.Length > 0) { list.Add(current); current = ""; }
            }
        }
        if (current.Length > 0) list.Add(current);
        return list;
    }

    private static bool IsNumber(string s)
        => s.Length > 0 && (char.IsDigit(s[0]) || s[0] == '-' || s[0] == '+' || s[0] == '.');

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static List<(float X, float Y)> ParsePoints(string points)
    {
        var list = new List<(float, float)>();
        if (string.IsNullOrWhiteSpace(points))
            return list;
        var nums = points.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < nums.Length; i += 2)
            list.Add((F(nums[i]), F(nums[i + 1])));
        return list;
    }

    private static Dictionary<string, string> ParseAttrs(string tag)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var re = new Regex(@"([\w:.-]+)\s*=\s*""([^""]*)""", RegexOptions.Compiled);
        foreach (Match m in re.Matches(tag))
            attrs[m.Groups[1].Value] = m.Groups[2].Value;
        return attrs;
    }

    private static bool Has(Dictionary<string, string> a, string k) => a.ContainsKey(k);

    private static double Num(Dictionary<string, string> a, string k)
        => a.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;

    private static double GetLength(Dictionary<string, string> a, string k, double fallback)
        => a.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    private static (double MinX, double MinY, double Width, double Height)? ParseViewBox(Dictionary<string, string> a)
    {
        if (!a.TryGetValue("viewBox", out var vb))
            return null;
        var nums = vb.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (nums.Length < 4)
            return null;
        return (double.TryParse(nums[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var mx)
            && double.TryParse(nums[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var my)
            && double.TryParse(nums[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && double.TryParse(nums[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            ? (mx, my, w, h)
            : null;
    }
}
