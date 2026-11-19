using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>How overlapping contours decide which points are filled.</summary>
public enum FillRule
{
    /// <summary>A point is filled when its winding number is not zero.</summary>
    NonZero,

    /// <summary>A point is filled when an odd number of contours cover it.</summary>
    EvenOdd,
}

/// <summary>
/// One anchor on a contour. Handles are absolute positions, matching the way
/// vector editors present them, and are only used when the corresponding flag
/// is set so corner nodes stay corners when their neighbours move.
/// </summary>
public struct PathNode
{
    /// <summary>The anchor position.</summary>
    public Vec2 Point;

    /// <summary>The control point of the curve arriving at this anchor.</summary>
    public Vec2 In;

    /// <summary>The control point of the curve leaving this anchor.</summary>
    public Vec2 Out;

    /// <summary>Whether <see cref="In"/> is meaningful.</summary>
    public bool HasIn;

    /// <summary>Whether <see cref="Out"/> is meaningful.</summary>
    public bool HasOut;

    /// <summary>A corner node with no handles.</summary>
    public static PathNode Corner(Vec2 point) => new() { Point = point };

    /// <summary>A smooth node with mirrored handles.</summary>
    public static PathNode Smooth(Vec2 point, Vec2 incoming, Vec2 outgoing) => new()
    {
        Point = point,
        In = incoming,
        Out = outgoing,
        HasIn = true,
        HasOut = true,
    };

    /// <summary>A smooth node whose handles run along one tangent.</summary>
    public static PathNode SmoothAlong(Vec2 point, Vec2 tangent, float inLength, float outLength)
    {
        Vec2 direction = tangent.Normalized;
        return Smooth(point, point - (direction * inLength), point + (direction * outLength));
    }
}

/// <summary>A single open or closed contour.</summary>
public sealed class VSubPath
{
    /// <summary>Creates an empty contour.</summary>
    public VSubPath(bool closed = false)
    {
        Closed = closed;
    }

    /// <summary>The anchors in drawing order.</summary>
    public List<PathNode> Nodes { get; } = [];

    /// <summary>Whether the last anchor joins back to the first.</summary>
    public bool Closed { get; set; }

    /// <summary>Whether the contour has anything to draw.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Appends an anchor.</summary>
    public void Add(PathNode node) => Nodes.Add(node);

    /// <summary>Deep copy.</summary>
    public VSubPath Clone()
    {
        VSubPath copy = new(Closed);
        copy.Nodes.AddRange(Nodes);
        return copy;
    }
}

/// <summary>A vector path made of cubic contours, the document's fundamental geometry.</summary>
public sealed class VPath
{
    /// <summary>The contours in drawing order.</summary>
    public List<VSubPath> SubPaths { get; } = [];

    /// <summary>The fill rule applied when the path is rasterised.</summary>
    public FillRule FillRule { get; set; } = FillRule.NonZero;

    /// <summary>Whether the path has no geometry.</summary>
    public bool IsEmpty => SubPaths.TrueForAll(sub => sub.Nodes.Count == 0);

    /// <summary>Adds a contour and returns it.</summary>
    public VSubPath Add(VSubPath subPath)
    {
        SubPaths.Add(subPath);
        return subPath;
    }

    /// <summary>Deep copy.</summary>
    public VPath Clone()
    {
        VPath copy = new() { FillRule = FillRule };
        foreach (VSubPath sub in SubPaths)
        {
            copy.SubPaths.Add(sub.Clone());
        }

        return copy;
    }

    /// <summary>Returns a copy transformed by a matrix.</summary>
    public VPath Transformed(Matrix3x2 matrix)
    {
        VPath copy = Clone();
        foreach (VSubPath sub in copy.SubPaths)
        {
            for (int i = 0; i < sub.Nodes.Count; i++)
            {
                PathNode node = sub.Nodes[i];
                node.Point = matrix.Apply(node.Point);
                node.In = matrix.Apply(node.In);
                node.Out = matrix.Apply(node.Out);
                sub.Nodes[i] = node;
            }
        }

        return copy;
    }

    /// <summary>Bounds of the flattened geometry.</summary>
    public Bounds GetBounds()
    {
        Bounds bounds = Bounds.Empty;
        foreach (FlatContour contour in Flatten(Matrix3x2.Identity, 0.08f))
        {
            foreach (Vec2 point in contour.Points)
            {
                bounds.Add(point);
            }
        }

        return bounds;
    }

    /// <summary>
    /// Converts the contours to polylines. Curves are subdivided until every
    /// segment deviates by less than <paramref name="tolerance"/> in the target space.
    /// </summary>
    public List<FlatContour> Flatten(Matrix3x2 transform, float tolerance)
    {
        List<FlatContour> result = new(SubPaths.Count);
        foreach (VSubPath sub in SubPaths)
        {
            if (sub.Nodes.Count == 0)
            {
                continue;
            }

            List<Vec2> points = new(sub.Nodes.Count * 4);
            PathNode first = sub.Nodes[0];
            points.Add(transform.Apply(first.Point));

            for (int i = 0; i < sub.Nodes.Count; i++)
            {
                if (i == sub.Nodes.Count - 1 && !sub.Closed)
                {
                    break;
                }

                PathNode from = sub.Nodes[i];
                PathNode to = sub.Nodes[(i + 1) % sub.Nodes.Count];
                Vec2 p0 = transform.Apply(from.Point);
                Vec2 p3 = transform.Apply(to.Point);

                if (!from.HasOut && !to.HasIn)
                {
                    points.Add(p3);
                    continue;
                }

                Vec2 p1 = from.HasOut ? transform.Apply(from.Out) : p0;
                Vec2 p2 = to.HasIn ? transform.Apply(to.In) : p3;
                FlattenCubic(points, p0, p1, p2, p3, tolerance, 0);
            }

            // A closed contour's final point repeats the first one; the closing
            // edge is implicit, which keeps point counts honest during editing.
            if (sub.Closed && points.Count > 1 && Vec2.Distance(points[^1], points[0]) < 1e-4f)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count >= 2)
            {
                result.Add(new FlatContour(points.ToArray(), sub.Closed));
            }
        }

        return result;
    }

    /// <summary>Subdivides one cubic segment until it is flat enough.</summary>
    private static void FlattenCubic(
        List<Vec2> points,
        Vec2 p0,
        Vec2 p1,
        Vec2 p2,
        Vec2 p3,
        float tolerance,
        int depth)
    {
        if (depth >= 18
            || (DistanceToLine(p1, p0, p3) <= tolerance && DistanceToLine(p2, p0, p3) <= tolerance))
        {
            points.Add(p3);
            return;
        }

        Vec2 p01 = (p0 + p1) * 0.5f;
        Vec2 p12 = (p1 + p2) * 0.5f;
        Vec2 p23 = (p2 + p3) * 0.5f;
        Vec2 p012 = (p01 + p12) * 0.5f;
        Vec2 p123 = (p12 + p23) * 0.5f;
        Vec2 mid = (p012 + p123) * 0.5f;

        FlattenCubic(points, p0, p01, p012, mid, tolerance, depth + 1);
        FlattenCubic(points, mid, p123, p23, p3, tolerance, depth + 1);
    }

    /// <summary>Distance from a point to the infinite line through two points.</summary>
    private static float DistanceToLine(Vec2 point, Vec2 a, Vec2 b)
    {
        Vec2 direction = b - a;
        float length = direction.Length;
        if (length < 1e-6f)
        {
            return Vec2.Distance(point, a);
        }

        return MathF.Abs(Vec2.Cross(direction / length, point - a));
    }

    /// <summary>Builds a rectangle path.</summary>
    public static VPath Rectangle(Bounds bounds)
    {
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed: true));
        sub.Add(PathNode.Corner(new Vec2(bounds.Left, bounds.Top)));
        sub.Add(PathNode.Corner(new Vec2(bounds.Right, bounds.Top)));
        sub.Add(PathNode.Corner(new Vec2(bounds.Right, bounds.Bottom)));
        sub.Add(PathNode.Corner(new Vec2(bounds.Left, bounds.Bottom)));
        return path;
    }

    /// <summary>Builds a rounded rectangle path.</summary>
    public static VPath RoundedRectangle(Bounds bounds, float radius)
    {
        float limit = MathF.Min(bounds.Width, bounds.Height) * 0.5f;
        float r = Math.Clamp(radius, 0f, limit);
        if (r <= 0.01f)
        {
            return Rectangle(bounds);
        }

        const float Kappa = 0.5522847498f;
        float k = r * Kappa;
        float left = bounds.Left;
        float top = bounds.Top;
        float right = bounds.Right;
        float bottom = bounds.Bottom;

        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed: true));

        // Anchors sit where each corner arc meets its straight edge. Only the
        // arc endpoints carry handles; the straight edges stay lines.
        sub.Add(ArcNode(new Vec2(left + r, top), new Vec2(left + r - k, top), null));
        sub.Add(ArcNode(new Vec2(right - r, top), null, new Vec2(right - r + k, top)));
        sub.Add(ArcNode(new Vec2(right, top + r), new Vec2(right, top + r - k), null));
        sub.Add(ArcNode(new Vec2(right, bottom - r), null, new Vec2(right, bottom - r + k)));
        sub.Add(ArcNode(new Vec2(right - r, bottom), new Vec2(right - r + k, bottom), null));
        sub.Add(ArcNode(new Vec2(left + r, bottom), null, new Vec2(left + r - k, bottom)));
        sub.Add(ArcNode(new Vec2(left, bottom - r), new Vec2(left, bottom - r + k), null));
        sub.Add(ArcNode(new Vec2(left, top + r), null, new Vec2(left, top + r - k)));

        return path;
    }

    private static PathNode ArcNode(Vec2 point, Vec2? incoming, Vec2? outgoing) => new()
    {
        Point = point,
        In = incoming ?? Vec2.Zero,
        Out = outgoing ?? Vec2.Zero,
        HasIn = incoming.HasValue,
        HasOut = outgoing.HasValue,
    };

    /// <summary>Builds an ellipse path.</summary>
    public static VPath Ellipse(Bounds bounds)
    {
        const float Kappa = 0.5522847498f;
        float rx = bounds.Width * 0.5f;
        float ry = bounds.Height * 0.5f;
        float cx = bounds.X + rx;
        float cy = bounds.Y + ry;
        float kx = rx * Kappa;
        float ky = ry * Kappa;

        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed: true));
        sub.Add(PathNode.Smooth(new Vec2(cx, cy - ry), new Vec2(cx - kx, cy - ry), new Vec2(cx + kx, cy - ry)));
        sub.Add(PathNode.Smooth(new Vec2(cx + rx, cy), new Vec2(cx + rx, cy - ky), new Vec2(cx + rx, cy + ky)));
        sub.Add(PathNode.Smooth(new Vec2(cx, cy + ry), new Vec2(cx + kx, cy + ry), new Vec2(cx - kx, cy + ry)));
        sub.Add(PathNode.Smooth(new Vec2(cx - rx, cy), new Vec2(cx - rx, cy + ky), new Vec2(cx - rx, cy - ky)));
        return path;
    }

    /// <summary>Builds a regular polygon centred on the origin.</summary>
    public static VPath Polygon(Vec2 center, float radiusX, float radiusY, int sides, float rotationDegrees)
    {
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed: true));
        float start = ((rotationDegrees - 90f) * MathF.PI) / 180f;
        int count = Math.Clamp(sides, 3, 64);

        for (int i = 0; i < count; i++)
        {
            float angle = start + (i * MathF.PI * 2f / count);
            sub.Add(PathNode.Corner(new Vec2(
                center.X + (MathF.Cos(angle) * radiusX),
                center.Y + (MathF.Sin(angle) * radiusY))));
        }

        return path;
    }

    /// <summary>Builds a star centred on the origin.</summary>
    public static VPath Star(Vec2 center, float radiusX, float radiusY, int points, float innerRatio, float rotationDegrees)
    {
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed: true));
        int count = Math.Clamp(points, 3, 64);
        float inner = Math.Clamp(innerRatio, 0.05f, 0.95f);
        float start = ((rotationDegrees - 90f) * MathF.PI) / 180f;

        for (int i = 0; i < count * 2; i++)
        {
            float angle = start + (i * MathF.PI / count);
            float radius = i % 2 == 0 ? 1f : inner;
            sub.Add(PathNode.Corner(new Vec2(
                center.X + (MathF.Cos(angle) * radiusX * radius),
                center.Y + (MathF.Sin(angle) * radiusY * radius))));
        }

        return path;
    }

    /// <summary>Builds a spiral as a finely sampled open polyline.</summary>
    public static VPath Spiral(Vec2 center, float radius, float turns, float startRadius)
    {
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath());
        float total = Math.Clamp(turns, 0.25f, 20f) * MathF.PI * 2f;
        int steps = Math.Clamp((int)(total / 0.12f), 24, 1200);

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float angle = total * t;
            float r = startRadius + ((radius - startRadius) * t);
            sub.Add(PathNode.Corner(new Vec2(
                center.X + (MathF.Cos(angle) * r),
                center.Y + (MathF.Sin(angle) * r))));
        }

        return path;
    }

    /// <summary>Builds a straight line.</summary>
    public static VPath Line(Vec2 from, Vec2 to)
    {
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath());
        sub.Add(PathNode.Corner(from));
        sub.Add(PathNode.Corner(to));
        return path;
    }
}

/// <summary>A flattened contour ready for rasterising or hit testing.</summary>
public sealed class FlatContour
{
    /// <summary>Creates a contour.</summary>
    public FlatContour(Vec2[] points, bool closed)
    {
        Points = points;
        Closed = closed;
    }

    /// <summary>The polyline vertices, without a repeated closing point.</summary>
    public Vec2[] Points { get; }

    /// <summary>Whether the final vertex joins the first.</summary>
    public bool Closed { get; }
}
