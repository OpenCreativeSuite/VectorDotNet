using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>Point and region queries against the document.</summary>
public static class HitTest
{
    /// <summary>The topmost path under a document-space point, or null.</summary>
    public static PathObject? TopmostPath(Document document, Vec2 point, float tolerance)
    {
        return FindPath(document.Root, point, tolerance);
    }

    /// <summary>The topmost object of any kind under a point.</summary>
    public static SceneObject? TopmostObject(Document document, Vec2 point, float tolerance)
    {
        return FindObject(document.Root, point, tolerance);
    }

    private static SceneObject? FindObject(GroupObject group, Vec2 point, float tolerance)
    {
        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            SceneObject child = group.Children[i];
            if (!child.Visible || child.Locked)
            {
                continue;
            }

            if (child is GroupObject nested)
            {
                SceneObject? found = FindObject(nested, point, tolerance);
                if (found is not null)
                {
                    return found;
                }
            }
            else if (child is PathObject path && Contains(path, point, tolerance))
            {
                return path;
            }
            else if (child is TextObject text && ContainsText(text, point))
            {
                return text;
            }
        }

        return null;
    }

    private static bool ContainsText(TextObject text, Vec2 documentPoint)
    {
        Matrix3x2 toDocument = text.DocumentTransform;
        if (!Matrix3x2.Invert(toDocument, out Matrix3x2 inverse))
        {
            return false;
        }

        return text.LocalBounds.Contains(inverse.Apply(documentPoint));
    }

    private static PathObject? FindPath(GroupObject group, Vec2 point, float tolerance)
    {
        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            SceneObject child = group.Children[i];
            if (!child.Visible || child.Locked)
            {
                continue;
            }

            if (child is GroupObject nested)
            {
                PathObject? found = FindPath(nested, point, tolerance);
                if (found is not null)
                {
                    return found;
                }
            }
            else if (child is PathObject path && Contains(path, point, tolerance))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>The outermost object the user expects to select for a hit.</summary>
    public static SceneObject ResolveSelection(SceneObject hit)
    {
        SceneObject node = hit;
        while (node.Parent is { IsLayer: false } parent)
        {
            node = parent;
        }

        return node;
    }

    /// <summary>Whether a point lies on a path's fill or stroke.</summary>
    public static bool Contains(PathObject path, Vec2 documentPoint, float tolerance)
    {
        Matrix3x2 toDocument = path.DocumentTransform;
        if (!Matrix3x2.Invert(toDocument, out Matrix3x2 inverse))
        {
            return false;
        }

        Vec2 local = inverse.Apply(documentPoint);
        float scale = MathF.Max(toDocument.ScaleFactor(), 0.01f);
        float localTolerance = tolerance / scale;

        if (path.Fill is { IsVisible: true } && PointInPath(path.Path, local))
        {
            return true;
        }

        if (path.Stroke is { IsVisible: true } stroke)
        {
            float reach = (stroke.Width * 0.5f) + localTolerance;
            foreach (FlatContour contour in path.Path.Flatten(Matrix3x2.Identity, 0.35f))
            {
                if (DistanceToContour(contour, local) <= reach)
                {
                    return true;
                }
            }

            return false;
        }

        if (path.Fill is not { IsVisible: true })
        {
            // An object with neither face is still grab-able by its box.
            return path.LocalBounds.Contains(local);
        }

        return false;
    }

    /// <summary>Filled-region test using the path's own fill rule.</summary>
    public static bool PointInPath(VPath path, Vec2 point)
    {
        int winding = 0;
        foreach (FlatContour contour in path.Flatten(Matrix3x2.Identity, 0.4f))
        {
            Vec2[] points = contour.Points;
            for (int i = 0; i < points.Length; i++)
            {
                Vec2 a = points[i];
                Vec2 b = points[(i + 1) % points.Length];
                if (a.Y <= point.Y && b.Y > point.Y)
                {
                    if (Vec2.Cross(b - a, point - a) > 0f)
                    {
                        winding++;
                    }
                }
                else if (b.Y <= point.Y && a.Y > point.Y)
                {
                    if (Vec2.Cross(b - a, point - a) < 0f)
                    {
                        winding--;
                    }
                }
            }
        }

        return path.FillRule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0;
    }

    /// <summary>Every leaf whose bounds intersect a document-space rectangle.</summary>
    public static List<PathObject> InRect(Document document, Bounds rect)
    {
        List<PathObject> hits = [];
        CollectRect(document.Root, rect, hits);
        return hits;
    }

    private static void CollectRect(GroupObject group, Bounds rect, List<PathObject> hits)
    {
        foreach (SceneObject child in group.Children)
        {
            if (!child.Visible || child.Locked)
            {
                continue;
            }

            if (child is GroupObject nested)
            {
                if (nested.BoundsInParent.Intersects(rect))
                {
                    CollectRect(nested, rect, hits);
                }
            }
            else if (child is PathObject path && path.DocumentBounds.Intersects(rect))
            {
                hits.Add(path);
            }
        }
    }

    /// <summary>Every leaf touched by a lasso polygon.</summary>
    public static List<PathObject> InLasso(Document document, IReadOnlyList<Vec2> polygon)
    {
        List<PathObject> hits = [];
        if (polygon.Count < 3)
        {
            return hits;
        }

        Bounds cage = Bounds.Empty;
        foreach (Vec2 point in polygon)
        {
            cage.Add(point);
        }

        CollectLasso(document.Root, cage, polygon, hits);
        return hits;
    }

    private static void CollectLasso(GroupObject group, Bounds cage, IReadOnlyList<Vec2> polygon, List<PathObject> hits)
    {
        foreach (SceneObject child in group.Children)
        {
            if (!child.Visible || child.Locked)
            {
                continue;
            }

            if (child is GroupObject nested)
            {
                if (nested.BoundsInParent.Intersects(cage))
                {
                    CollectLasso(nested, cage, polygon, hits);
                }
            }
            else if (child is PathObject path && path.DocumentBounds.Intersects(cage) && TouchesPolygon(path, polygon))
            {
                hits.Add(path);
            }
        }
    }

    private static bool TouchesPolygon(PathObject path, IReadOnlyList<Vec2> polygon)
    {
        Matrix3x2 toDocument = path.DocumentTransform;
        foreach (FlatContour contour in path.Path.Flatten(toDocument, 1.2f))
        {
            foreach (Vec2 point in contour.Points)
            {
                if (PointInPolygon(polygon, point))
                {
                    return true;
                }
            }
        }

        return PointInPolygon(polygon, path.DocumentBounds.Center);
    }

    /// <summary>Even-odd point-in-polygon test.</summary>
    public static bool PointInPolygon(IReadOnlyList<Vec2> polygon, Vec2 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float DistanceToContour(FlatContour contour, Vec2 point)
    {
        Vec2[] points = contour.Points;
        float best = float.MaxValue;
        int count = contour.Closed ? points.Length : points.Length - 1;
        for (int i = 0; i < count; i++)
        {
            best = MathF.Min(best, DistanceToSegment(point, points[i], points[(i + 1) % points.Length]));
        }

        return best;
    }

    /// <summary>Shortest distance from a point to a segment.</summary>
    public static float DistanceToSegment(Vec2 point, Vec2 a, Vec2 b)
    {
        Vec2 direction = b - a;
        float lengthSquared = Vec2.Dot(direction, direction);
        if (lengthSquared < 1e-8f)
        {
            return Vec2.Distance(point, a);
        }

        float t = Math.Clamp(Vec2.Dot(point - a, direction) / lengthSquared, 0f, 1f);
        return Vec2.Distance(point, a + (direction * t));
    }
}
