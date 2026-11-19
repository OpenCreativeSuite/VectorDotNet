using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>Turns a document plus a view transform into render spans.</summary>
public static class SceneRenderer
{
    /// <summary>Paints the artboard backgrounds and every visible object.</summary>
    public static void Render(
        Document document,
        Matrix3x2 view,
        Bounds clip,
        SpanBuffer output,
        bool drawArtboards = true)
    {
        if (drawArtboards)
        {
            foreach (Artboard artboard in document.Artboards)
            {
                VPath page = VPath.Rectangle(artboard.Bounds);
                Rasterizer.FillPath(page, view, clip, artboard.Background, 1f, output);
            }
        }

        foreach (SceneObject layer in document.Root.Children)
        {
            RenderObject(layer, Matrix3x2.Identity, view, clip, output, 1f);
        }
    }

    private static void RenderObject(
        SceneObject node,
        Matrix3x2 parent,
        Matrix3x2 view,
        Bounds clip,
        SpanBuffer output,
        float opacity)
    {
        if (!node.Visible || node.Opacity <= 0.002f)
        {
            return;
        }

        float alpha = opacity * node.Opacity;
        if (alpha <= 0.002f)
        {
            return;
        }

        Matrix3x2 toDocument = node.Transform * parent;

        if (node is GroupObject group)
        {
            foreach (SceneObject child in group.Children)
            {
                RenderObject(child, toDocument, view, clip, output, alpha);
            }

            return;
        }

        if (node is not PathObject path)
        {
            return;
        }

        Matrix3x2 device = toDocument * view;

        // Cull against the canvas before flattening anything expensive.
        Bounds deviceBounds = device.TransformBounds(path.LocalBounds);
        if (!deviceBounds.Intersect(clip).IsEmpty)
        {
            if (path.Fill is { IsVisible: true } fill)
            {
                Rasterizer.FillPath(path.Path, device, clip, fill.Color, alpha, output);
            }
        }

        if (path.Stroke is { IsVisible: true } stroke)
        {
            Bounds strokeBounds = deviceBounds.Inflate((stroke.Width * device.ScaleFactor()) + 2f);
            if (strokeBounds.Intersects(clip))
            {
                Rasterizer.StrokePath(path.Path, device, clip, stroke, alpha, output);
            }
        }
    }

    /// <summary>The local-to-device matrix of an object under the current view.</summary>
    public static Matrix3x2 DeviceMatrix(SceneObject node, Matrix3x2 view) => node.DocumentTransform * view;

    /// <summary>Collects visible text objects in paint order for the canvas overlay.</summary>
    public static void CollectText(Document document, List<TextDrawItem> output)
    {
        output.Clear();
        foreach (SceneObject layer in document.Root.Children)
        {
            CollectText(layer, output, 1f);
        }
    }

    private static void CollectText(SceneObject node, List<TextDrawItem> output, float opacity)
    {
        if (!node.Visible || node.Opacity <= 0.002f)
        {
            return;
        }

        float alpha = opacity * node.Opacity;
        if (node is GroupObject group)
        {
            foreach (SceneObject child in group.Children)
            {
                CollectText(child, output, alpha);
            }
        }
        else if (node is TextObject text && alpha > 0.002f)
        {
            output.Add(new TextDrawItem(text, alpha));
        }
    }
}

/// <summary>A text object with its resolved ancestor opacity.</summary>
public readonly record struct TextDrawItem(TextObject Text, float Opacity);
