using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>A horizontal run of pixels with one colour.</summary>
public struct Span
{
    /// <summary>Pixel row.</summary>
    public int Y;

    /// <summary>Left edge in device pixels.</summary>
    public float X;

    /// <summary>Width in device pixels.</summary>
    public float Width;

    /// <summary>Packed colour, including coverage in its alpha channel.</summary>
    public uint Color;
}

/// <summary>Accumulates the spans that make up one frame.</summary>
public sealed class SpanBuffer
{
    /// <summary>The runs, in paint order.</summary>
    public List<Span> Spans { get; } = new(16384);

    /// <summary>Removes every run.</summary>
    public void Clear() => Spans.Clear();

    /// <summary>Adds a run when it has visible width.</summary>
    public void Add(int y, float x, float width, uint color)
    {
        if (width < 0.02f || (color >> 24) == 0)
        {
            return;
        }

        Spans.Add(new Span { Y = y, X = x, Width = width, Color = color });
    }
}

/// <summary>
/// Scanline rasteriser for filled paths and stroke outlines. Curves are
/// flattened in device space, then sampled vertically and integrated
/// horizontally so edges receive proper fractional coverage.
/// </summary>
public static class Rasterizer
{
    private const int SubSamples = 4;

    /// <summary>Fills a path into horizontal runs.</summary>
    public static void FillPath(
        VPath path,
        Matrix3x2 transform,
        Bounds clip,
        uint color,
        float opacity,
        SpanBuffer output,
        FillRule? rule = null)
    {
        if (opacity <= 0.002f || Colour.A(color) == 0 || path.IsEmpty)
        {
            return;
        }

        List<FlatContour> contours = path.Flatten(transform, 0.2f);
        if (contours.Count == 0)
        {
            return;
        }

        List<Vec2[]> polygons = new(contours.Count);
        foreach (FlatContour contour in contours)
        {
            polygons.Add(contour.Points);
        }

        FillPolygons(polygons, rule ?? path.FillRule, clip, color, opacity, output);
    }

    /// <summary>Strokes a path into horizontal runs.</summary>
    public static void StrokePath(
        VPath path,
        Matrix3x2 transform,
        Bounds clip,
        StrokeStyle stroke,
        float opacity,
        SpanBuffer output)
    {
        if (!stroke.IsVisible || opacity <= 0.002f || path.IsEmpty)
        {
            return;
        }

        float scale = MathF.Max(transform.ScaleFactor(), 0.02f);
        float width = MathF.Max(stroke.Width * scale, 0.3f);
        float half = width * 0.5f;
        List<FlatContour> contours = path.Flatten(transform, 0.18f);

        List<Vec2[]> polygons = new(contours.Count * 8);
        foreach (FlatContour contour in contours)
        {
            if (contour.Points.Length < 2)
            {
                if (contour.Points.Length == 1)
                {
                    AddDisc(polygons, contour.Points[0], half, 14);
                }

                continue;
            }

            if (stroke.Dash.Length > 0)
            {
                foreach (Vec2[] run in BuildDashes(contour.Points, contour.Closed, stroke.Dash, stroke.DashOffset, scale))
                {
                    BuildPolyline(polygons, run, false, half, stroke.Cap, stroke.Join, stroke.MiterLimit);
                }
            }
            else
            {
                BuildPolyline(polygons, contour.Points, contour.Closed, half, stroke.Cap, stroke.Join, stroke.MiterLimit);
            }
        }

        FillPolygons(polygons, FillRule.NonZero, clip, stroke.Paint.Color, opacity, output);
    }

    /// <summary>Rasterises a set of closed polygons under one fill rule.</summary>
    public static void FillPolygons(
        IReadOnlyList<Vec2[]> polygons,
        FillRule rule,
        Bounds clip,
        uint color,
        float opacity,
        SpanBuffer output)
    {
        if (polygons.Count == 0 || opacity <= 0.002f)
        {
            return;
        }

        List<Edge> edges = new(polygons.Count * 8);
        Bounds bounds = Bounds.Empty;

        foreach (Vec2[] points in polygons)
        {
            if (points.Length < 3)
            {
                continue;
            }

            for (int i = 0; i < points.Length; i++)
            {
                Vec2 a = points[i];
                Vec2 b = points[(i + 1) % points.Length];
                bounds.Add(a);
                if (MathF.Abs(a.Y - b.Y) < 1e-5f)
                {
                    continue;
                }

                edges.Add(new Edge(a, b));
            }
        }

        if (edges.Count == 0)
        {
            return;
        }

        Bounds visible = bounds.Intersect(clip);
        if (visible.IsEmpty)
        {
            return;
        }

        int firstRow = Math.Max((int)MathF.Floor(visible.Top), (int)MathF.Floor(clip.Top));
        int lastRow = Math.Min((int)MathF.Ceiling(visible.Bottom), (int)MathF.Ceiling(clip.Bottom));
        int clipLeft = Math.Max(0, (int)MathF.Floor(clip.Left));
        int clipRight = Math.Min((int)MathF.Ceiling(clip.Right), clipLeft + 16384);
        int coverWidth = clipRight - clipLeft + 2;
        if (coverWidth <= 1 || lastRow <= firstRow)
        {
            return;
        }

        float[] cover = new float[coverWidth];
        List<Intersection> crossings = new(edges.Count);
        uint baseColor = color & 0x00FFFFFFu;
        float baseAlpha = Colour.A(color) / 255f;

        for (int row = firstRow; row < lastRow; row++)
        {
            Array.Clear(cover, 0, cover.Length);

            for (int sample = 0; sample < SubSamples; sample++)
            {
                float y = row + ((sample + 0.5f) / SubSamples);
                crossings.Clear();

                foreach (Edge edge in edges)
                {
                    if (y < edge.YMin || y >= edge.YMax)
                    {
                        continue;
                    }

                    crossings.Add(new Intersection(edge.XAt(y), edge.Direction));
                }

                if (crossings.Count < 2)
                {
                    continue;
                }

                crossings.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Direction.CompareTo(b.Direction));

                int winding = 0;
                float spanStart = 0f;
                bool inside = false;

                foreach (Intersection crossing in crossings)
                {
                    bool wasInside = inside;
                    winding += crossing.Direction;
                    inside = rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;

                    if (!wasInside && inside)
                    {
                        spanStart = crossing.X;
                    }
                    else if (wasInside && !inside)
                    {
                        Accumulate(cover, clipLeft, clipRight, spanStart, crossing.X);
                    }
                }
            }

            EmitRow(output, row, cover, clipLeft, clipRight, baseColor, baseAlpha * opacity);
        }
    }

    private static void Accumulate(float[] cover, int clipLeft, int clipRight, float x0, float x1)
    {
        x0 = MathF.Max(x0, clipLeft);
        x1 = MathF.Min(x1, clipRight);
        if (x1 <= x0)
        {
            return;
        }

        float local0 = x0 - clipLeft;
        float local1 = x1 - clipLeft;
        int i0 = (int)MathF.Floor(local0);
        int i1 = (int)MathF.Floor(local1);

        if (i0 == i1)
        {
            if (i0 >= 0 && i0 < cover.Length)
            {
                cover[i0] += x1 - x0;
            }

            return;
        }

        if (i0 >= 0 && i0 < cover.Length)
        {
            cover[i0] += (i0 + 1) - local0;
        }

        int start = Math.Max(i0 + 1, 0);
        int end = Math.Min(i1 - 1, cover.Length - 1);
        for (int i = start; i <= end; i++)
        {
            cover[i] += 1f;
        }

        if (i1 >= 0 && i1 < cover.Length)
        {
            cover[i1] += local1 - i1;
        }
    }

    private static void EmitRow(
        SpanBuffer output,
        int row,
        float[] cover,
        int clipLeft,
        int clipRight,
        uint baseColor,
        float alphaScale)
    {
        int runStart = -1;
        int runAlpha = 0;
        int limit = Math.Min(cover.Length, clipRight - clipLeft + 1);

        for (int i = 0; i <= limit; i++)
        {
            int alpha = 0;
            if (i < limit)
            {
                float coverage = MathF.Min(cover[i], SubSamples) / SubSamples;
                alpha = (int)((coverage * alphaScale * 255f) + 0.5f);
                alpha = Math.Clamp(alpha, 0, 255);
            }

            if (alpha != runAlpha)
            {
                if (runAlpha > 0 && runStart >= 0)
                {
                    output.Add(
                        row,
                        clipLeft + runStart,
                        i - runStart,
                        baseColor | ((uint)runAlpha << 24));
                }

                runStart = i;
                runAlpha = alpha;
            }
        }
    }

    /// <summary>Builds the polygons of a stroked polyline.</summary>
    private static void BuildPolyline(
        List<Vec2[]> output,
        Vec2[] source,
        bool closed,
        float half,
        LineCap cap,
        LineJoin join,
        float miterLimit)
    {
        Vec2[] points = RemoveDuplicates(source);
        if (points.Length < 2)
        {
            if (points.Length == 1)
            {
                AddDisc(output, points[0], half, 16);
            }

            return;
        }

        for (int i = 0; i + 1 < points.Length; i++)
        {
            AddSegment(output, points[i], points[i + 1], half);
        }

        if (closed)
        {
            AddSegment(output, points[^1], points[0], half);
        }

        for (int i = 0; i < points.Length; i++)
        {
            bool hasPrevious = closed || i > 0;
            bool hasNext = closed || i + 1 < points.Length;
            if (!hasPrevious || !hasNext)
            {
                continue;
            }

            Vec2 previous = points[(i - 1 + points.Length) % points.Length];
            Vec2 next = points[(i + 1) % points.Length];
            AddJoin(output, points[i], previous, next, half, join, miterLimit);
        }

        if (!closed)
        {
            AddCap(output, points[0], points[1], half, cap, atStart: true);
            AddCap(output, points[^1], points[^2], half, cap, atStart: false);
        }
    }

    private static Vec2[] RemoveDuplicates(Vec2[] points)
    {
        List<Vec2> result = new(points.Length);
        foreach (Vec2 point in points)
        {
            if (result.Count == 0 || Vec2.Distance(result[^1], point) > 1e-4f)
            {
                result.Add(point);
            }
        }

        if (result.Count > 1 && Vec2.Distance(result[0], result[^1]) < 1e-4f)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result.ToArray();
    }

    private static void AddSegment(List<Vec2[]> output, Vec2 a, Vec2 b, float half)
    {
        Vec2 direction = b - a;
        if (direction.Length < 1e-5f)
        {
            return;
        }

        Vec2 normal = new Vec2(-direction.Y, direction.X).Normalized * half;
        output.Add(Orient([a + normal, b + normal, b - normal, a - normal]));
    }

    private static void AddJoin(
        List<Vec2[]> output,
        Vec2 vertex,
        Vec2 previous,
        Vec2 next,
        float half,
        LineJoin join,
        float miterLimit)
    {
        Vec2 directionIn = (vertex - previous).Normalized;
        Vec2 directionOut = (next - vertex).Normalized;
        if (directionIn.Length < 1e-5f || directionOut.Length < 1e-5f)
        {
            return;
        }

        Vec2 normalIn = new Vec2(-directionIn.Y, directionIn.X) * half;
        Vec2 normalOut = new Vec2(-directionOut.Y, directionOut.X) * half;
        float turn = Vec2.Cross(directionIn, directionOut);

        if (join == LineJoin.Round || MathF.Abs(turn) < 0.02f)
        {
            if (MathF.Abs(turn) > 0.02f)
            {
                AddDisc(output, vertex, half, ArcSteps(half));
            }

            return;
        }

        if (join == LineJoin.Bevel)
        {
            output.Add(Orient([vertex, vertex + normalIn, vertex + normalOut]));
            output.Add(Orient([vertex, vertex - normalIn, vertex - normalOut]));
            return;
        }

        // Miter: extend to the point where the two outer edges meet. Both sides
        // are emitted; the inner wedge lies inside the stroke body either way.
        Vec2 miterIn = (normalIn + normalOut);
        float miterLength = miterIn.Length;
        if (miterLength > 1e-4f && Vec2.Dot(miterIn / miterLength, normalIn.Normalized) > 0.08f)
        {
            float distance = half / Vec2.Dot(miterIn / miterLength, normalIn.Normalized);
            if (distance <= half * MathF.Max(miterLimit, 1f))
            {
                Vec2 tip = vertex + (miterIn / miterLength) * distance;
                output.Add(Orient([vertex, vertex + normalIn, tip]));
                output.Add(Orient([vertex, tip, vertex + normalOut]));
                output.Add(Orient([vertex, vertex - normalIn, vertex - (miterIn / miterLength) * distance]));
                output.Add(Orient([vertex, vertex - (miterIn / miterLength) * distance, vertex - normalOut]));
                return;
            }
        }

        output.Add(Orient([vertex, vertex + normalIn, vertex + normalOut]));
        output.Add(Orient([vertex, vertex - normalIn, vertex - normalOut]));
    }

    private static void AddCap(List<Vec2[]> output, Vec2 end, Vec2 towards, float half, LineCap cap, bool atStart)
    {
        if (cap == LineCap.Butt)
        {
            return;
        }

        Vec2 direction = (end - towards).Normalized;
        if (direction.Length < 1e-5f)
        {
            return;
        }

        if (cap == LineCap.Round)
        {
            AddDisc(output, end, half, ArcSteps(half));
            return;
        }

        Vec2 normal = new Vec2(-direction.Y, direction.X) * half;
        Vec2 tip = end + (direction * half);
        output.Add(Orient([end + normal, tip + normal, tip - normal, end - normal]));
    }

    private static void AddDisc(List<Vec2[]> output, Vec2 center, float radius, int segments)
    {
        if (radius < 0.2f)
        {
            return;
        }

        Vec2[] points = new Vec2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * MathF.PI * 2f / segments;
            points[i] = new Vec2(center.X + (MathF.Cos(angle) * radius), center.Y + (MathF.Sin(angle) * radius));
        }

        output.Add(Orient(points));
    }

    private static int ArcSteps(float radius) => Math.Clamp((int)(radius * 1.4f) + 8, 8, 48);

    private static Vec2[] Orient(Vec2[] polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];
            area += (a.X * b.Y) - (b.X * a.Y);
        }

        if (area >= 0f)
        {
            return polygon;
        }

        Array.Reverse(polygon);
        return polygon;
    }

    private static List<Vec2[]> BuildDashes(
        Vec2[] points,
        bool closed,
        float[] pattern,
        float offset,
        float scale)
    {
        List<Vec2[]> runs = [];
        if (pattern.Length == 0)
        {
            runs.Add(points);
            return runs;
        }

        float[] dashes = new float[pattern.Length];
        float total = 0f;
        for (int i = 0; i < pattern.Length; i++)
        {
            dashes[i] = MathF.Max(pattern[i] * scale, 0.1f);
            total += dashes[i];
        }

        if (total < 0.1f)
        {
            runs.Add(points);
            return runs;
        }

        int dashIndex = 0;
        float remaining = dashes[0];
        bool drawing = true;
        float skip = ((offset * scale) % total + total) % total;
        while (skip > 0f)
        {
            if (skip >= remaining)
            {
                skip -= remaining;
                dashIndex = (dashIndex + 1) % dashes.Length;
                remaining = dashes[dashIndex];
                drawing = !drawing;
            }
            else
            {
                remaining -= skip;
                skip = 0f;
            }
        }

        List<Vec2> current = [];
        int segments = (closed ? points.Length : points.Length - 1) + 1;

        for (int i = 0; i < segments; i++)
        {
            Vec2 a = points[i % points.Length];
            Vec2 b = points[(i + 1) % points.Length];
            if (!closed && i + 1 >= points.Length)
            {
                break;
            }

            float length = Vec2.Distance(a, b);
            if (length < 1e-5f)
            {
                continue;
            }

            float travelled = 0f;
            while (travelled < length - 1e-5f)
            {
                float step = MathF.Min(remaining, length - travelled);
                float t0 = travelled / length;
                float t1 = (travelled + step) / length;

                if (drawing)
                {
                    if (current.Count == 0)
                    {
                        current.Add(Vec2.Lerp(a, b, t0));
                    }

                    current.Add(Vec2.Lerp(a, b, t1));
                }

                travelled += step;
                remaining -= step;

                if (remaining <= 1e-5f)
                {
                    if (drawing && current.Count >= 2)
                    {
                        runs.Add(current.ToArray());
                    }

                    current.Clear();
                    dashIndex = (dashIndex + 1) % dashes.Length;
                    remaining = dashes[dashIndex];
                    drawing = !drawing;
                }
            }
        }

        if (drawing && current.Count >= 2)
        {
            runs.Add(current.ToArray());
        }

        return runs;
    }

    private readonly struct Intersection
    {
        public Intersection(float x, int direction)
        {
            X = x;
            Direction = direction;
        }

        public float X { get; }

        public int Direction { get; }
    }

    private readonly struct Edge
    {
        private readonly Vec2 _a;
        private readonly Vec2 _b;

        public Edge(Vec2 a, Vec2 b)
        {
            _a = a;
            _b = b;
            YMin = MathF.Min(a.Y, b.Y);
            YMax = MathF.Max(a.Y, b.Y);
            Direction = a.Y < b.Y ? 1 : -1;
        }

        public float YMin { get; }

        public float YMax { get; }

        public int Direction { get; }

        public float XAt(float y)
        {
            float t = (y - _a.Y) / (_b.Y - _a.Y);
            return _a.X + ((_b.X - _a.X) * t);
        }
    }
}
