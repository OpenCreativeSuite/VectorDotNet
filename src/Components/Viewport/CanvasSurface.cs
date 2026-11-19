using System.Numerics;
using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;

namespace VectorDotNet.Components;

/// <summary>
/// The document surface. One canvas paints the cached vector spans, text,
/// guides, selection chrome and rulers every frame, and forwards pointer input
/// in canvas-local pixels.
/// </summary>
internal sealed class CanvasSurface : Component
{
    private const uint Pasteboard = 0xFF101114;
    private const uint Accent = 0xFF5B93F8;
    private const uint AccentSoft = 0x225B93F8;
    private const uint ChipDark = 0xFF101215;
    private const uint ChipLight = 0xFFF4F7FC;
    private const uint RulerFill = 0xFF1A1D22;
    private const uint RulerLine = 0xFF3A414C;
    private const uint RulerText = 0xFF8B94A1;

    private static readonly TextStyle RulerStyle = new(
        "Segoe UI",
        9.5f,
        400,
        false,
        RulerText,
        0f,
        11f,
        TextAlign.Left,
        TextDecoration.None,
        WhiteSpace.NoWrap,
        Direction.LeftToRight);

    private static readonly TextStyle LabelStyle = new(
        "Segoe UI",
        11f,
        500,
        false,
        0xFF9AA3AF,
        0f,
        13f,
        TextAlign.Left,
        TextDecoration.None,
        WhiteSpace.NoWrap,
        Direction.LeftToRight);

    private static readonly TextStyle HintStyle = new(
        "Segoe UI",
        12f,
        400,
        false,
        0xFF9AA3AF,
        0f,
        15f,
        TextAlign.Left,
        TextDecoration.None,
        WhiteSpace.NoWrap,
        Direction.LeftToRight);

    private sealed class RenderCache
    {
        public int Version = -1;
        public float Width;
        public float Height;
        public readonly SpanBuffer Spans = new();
        public readonly List<TextDrawItem> Text = [];
    }

    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        Ref<ElementNode> canvasRef = UseRef<ElementNode>();
        Ref<RenderCache> cacheRef = UseRef<RenderCache>();

        string cursor = session.Tool switch
        {
            ToolKind.Select => "is-select",
            ToolKind.Direct => "is-direct",
            ToolKind.Pen => "is-pen",
            ToolKind.Pencil => "is-draw",
            ToolKind.Text => "is-text",
            ToolKind.Lasso => "is-lasso",
            ToolKind.Zoom => "is-zoom",
            ToolKind.Hand => "is-hand",
            _ => "is-draw",
        };

        Node node = Canvas(new CanvasProps
        {
            ClassName = "surface " + cursor,
            TabIndex = 0,
            Attributes = Ui.Size(1280, 800),
            OnDraw = context => Draw(context, session, canvasRef.Value, cacheRef),
            OnPointerDown = (x, y) => session.PointerDown(
                new Vec2(x, y),
                InputState.Shift,
                InputState.Alt,
                InputState.Middle),
            OnPointerMove = (x, y) =>
            {
                if (MiddlePan.Handle(session))
                {
                    return;
                }

                session.PointerMove(new Vec2(x, y));
            },
            OnPointerUp = (x, y) =>
            {
                MiddlePan.Reset();
                session.PointerUp(new Vec2(x, y));
            },
            OnScrollEvent = scroll =>
            {
                Vec2 screen = canvasRef.Value is { } canvas
                    ? new Vec2(InputState.PointerX - canvas.PositionX, InputState.PointerY - canvas.PositionY)
                    : new Vec2(session.ViewWidth * 0.5f, session.ViewHeight * 0.5f);
                bool control = scroll.Modifiers.HasFlag(KeyModifiers.Control) || InputState.Control;
                bool shift = scroll.Modifiers.HasFlag(KeyModifiers.Shift) || InputState.Shift;
                session.Scroll(scroll.DeltaX, scroll.DeltaY, screen, control, shift);
            },
            OnContextMenu = menu =>
            {
                menu.PreventDefault();
                session.DeselectAll();
            },
        });

        if (node is ElementNode element)
        {
            element.BindRef(canvasRef);
        }

        return node;
    }

    private static void Draw(
        CanvasContext context,
        EditorSession session,
        ElementNode? canvas,
        Ref<RenderCache> cacheRef)
    {
        if (canvas is null || canvas.Width < 2f || canvas.Height < 2f)
        {
            return;
        }

        session.SetViewSize(canvas.Width, canvas.Height);
        Bounds clip = session.CanvasClip;
        context.Clear(Pasteboard);

        Matrix3x2 view = Matrix3x2.CreateScale(session.Zoom) * Matrix3x2.CreateTranslation(session.PanX, session.PanY);

        RenderCache cache = cacheRef.Value ??= new RenderCache();
        if (cache.Version != session.Revision || MathF.Abs(cache.Width - canvas.Width) > 0.5f || MathF.Abs(cache.Height - canvas.Height) > 0.5f)
        {
            cache.Spans.Clear();
            SceneRenderer.Render(session.Document, view, clip, cache.Spans, true);
            SceneRenderer.CollectText(session.Document, cache.Text);
            cache.Version = session.Revision;
            cache.Width = canvas.Width;
            cache.Height = canvas.Height;
        }

        context.PushClip(ToRect(clip));

        DrawArtboardShadows(context, session, view);
        DrawSpans(context, cache.Spans);
        DrawText(context, cache.Text, view);
        DrawGrid(context, session, view);
        DrawGuides(context, session, view);
        DrawArtboardFrame(context, session, view);
        DrawSmartGuides(context, session);
        DrawSelection(context, session, view);
        DrawNodeEditing(context, session, view);
        DrawPen(context, session, view);
        DrawPencil(context, session, view);
        DrawMarquee(context, session);
        DrawLasso(context, session);
        DrawEmptyHint(context, session);

        context.PopClip();

        DrawArtboardLabels(context, session, view);

        if (session.ShowRulers)
        {
            DrawRulers(context, session);
        }
    }

    private static void DrawSpans(CanvasContext context, SpanBuffer buffer)
    {
        List<Span> spans = buffer.Spans;
        for (int i = 0; i < spans.Count; i++)
        {
            Span span = spans[i];
            context.FillRect(span.X, span.Y, span.Width, 1f, span.Color);
        }
    }

    private static void DrawText(CanvasContext context, List<TextDrawItem> items, Matrix3x2 view)
    {
        foreach (TextDrawItem item in items)
        {
            TextObject text = item.Text;
            if (text.Fill is not { IsVisible: true } fill)
            {
                continue;
            }

            uint color = Colour.Fade(fill.Color, item.Opacity);
            if ((color >> 24) == 0)
            {
                continue;
            }

            TextStyle style = new(
                text.FontFamily,
                text.FontSize,
                text.Bold ? 700 : 400,
                text.Italic,
                color,
                0f,
                text.FontSize * text.LineHeight,
                TextAlign.Left,
                TextDecoration.None,
                WhiteSpace.NoWrap,
                Direction.LeftToRight);

            context.PushTransform(text.DocumentTransform * view);
            context.DrawText(
                new Rect(0f, 0f, text.MeasuredWidth + (text.FontSize * 2f), text.LocalBounds.Height + (text.FontSize * 0.5f)),
                text.Text,
                style);
            context.PopTransform();
        }
    }

    private static void DrawArtboardShadows(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        foreach (Artboard artboard in session.Document.Artboards)
        {
            Bounds rect = view.TransformBounds(artboard.Bounds);
            if (!rect.Intersects(session.CanvasClip))
            {
                continue;
            }

            for (int layer = 5; layer >= 1; layer--)
            {
                float grow = layer * 2f;
                context.FillRect(
                    rect.X - grow,
                    rect.Y + 3f + (grow * 0.6f),
                    rect.Width + (grow * 2f),
                    rect.Height + grow,
                    0x0C000000);
            }
        }
    }

    private static void DrawArtboardFrame(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        foreach (Artboard artboard in session.Document.Artboards)
        {
            Bounds rect = view.TransformBounds(artboard.Bounds);
            bool active = artboard == session.Document.Active;
            uint color = active ? 0x665B93F8u : 0x33000000u;
            context.FillRect(rect.Left, rect.Top, rect.Width, 1f, color);
            context.FillRect(rect.Left, rect.Bottom - 1f, rect.Width, 1f, color);
            context.FillRect(rect.Left, rect.Top, 1f, rect.Height, color);
            context.FillRect(rect.Right - 1f, rect.Top, 1f, rect.Height, color);
        }
    }

    private static void DrawArtboardLabels(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        Bounds clip = session.CanvasClip;
        foreach (Artboard artboard in session.Document.Artboards)
        {
            Bounds rect = view.TransformBounds(artboard.Bounds);
            float y = rect.Top - 16f;
            if (y < clip.Top + 2f)
            {
                y = rect.Top + 6f;
            }

            bool active = artboard == session.Document.Active;
            TextStyle style = active
                ? new TextStyle("Segoe UI", 11f, 600, false, 0xFF7FB0FF, 0f, 13f, TextAlign.Left, TextDecoration.None, WhiteSpace.NoWrap, Direction.LeftToRight)
                : LabelStyle;

            context.DrawText(new Rect(rect.Left + 1f, y, MathF.Max(160f, rect.Width), 14f), artboard.Name, style);

            string size = $"{artboard.Width:0} × {artboard.Height:0}";
            context.DrawText(
                new Rect(rect.Left + 1f + (style.FontSize * 7.2f), y, 160f, 14f),
                size,
                new TextStyle("Segoe UI", 10f, 400, false, 0xFF6C7683, 0f, 13f, TextAlign.Left, TextDecoration.None, WhiteSpace.NoWrap, Direction.LeftToRight));
        }
    }

    private static void DrawGrid(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        if (!session.ShowGrid || session.GridSize <= 0.5f)
        {
            return;
        }

        Bounds page = view.TransformBounds(session.Document.Active.Bounds);
        Bounds visible = page.Intersect(session.CanvasClip);
        if (visible.IsEmpty)
        {
            return;
        }

        float step = session.GridSize * session.Zoom;
        while (step < 6f)
        {
            step *= 5f;
        }

        context.PushClip(ToRect(visible));
        int index = 0;
        for (float x = page.Left; x <= page.Right + 0.5f; x += step)
        {
            if (x >= visible.Left && x <= visible.Right)
            {
                context.FillRect(x, visible.Top, 1f, visible.Height, index % 5 == 0 ? 0x24000000u : 0x12000000u);
            }

            index++;
        }

        index = 0;
        for (float y = page.Top; y <= page.Bottom + 0.5f; y += step)
        {
            if (y >= visible.Top && y <= visible.Bottom)
            {
                context.FillRect(visible.Left, y, visible.Width, 1f, index % 5 == 0 ? 0x24000000u : 0x12000000u);
            }

            index++;
        }

        context.PopClip();
    }

    private static void DrawGuides(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        if (!session.ShowGuides)
        {
            return;
        }

        Bounds clip = session.CanvasClip;
        foreach (Guide guide in session.Document.Guides)
        {
            if (guide.Horizontal)
            {
                float y = (guide.Position * session.Zoom) + session.PanY;
                context.FillRect(clip.Left, y, clip.Width, 1f, 0xAA37C6FF);
            }
            else
            {
                float x = (guide.Position * session.Zoom) + session.PanX;
                context.FillRect(x, clip.Top, 1f, clip.Height, 0xAA37C6FF);
            }
        }
    }

    private static void DrawSmartGuides(CanvasContext context, EditorSession session)
    {
        if (!session.SmartGuides || session.SmartGuideLines.Count == 0)
        {
            return;
        }

        Bounds clip = session.CanvasClip;
        foreach ((bool horizontal, float position) in session.SmartGuideLines)
        {
            if (horizontal)
            {
                float y = (position * session.Zoom) + session.PanY;
                DashLine(context, clip.Left, y, clip.Right, y, 0xFFF06BC0);
            }
            else
            {
                float x = (position * session.Zoom) + session.PanX;
                DashLine(context, x, clip.Top, x, clip.Bottom, 0xFFF06BC0);
            }
        }
    }

    private static void DrawSelection(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        if (session.Selection.Count == 0 || session.Tool != ToolKind.Select)
        {
            return;
        }

        if (session.Selection.Count > 1)
        {
            foreach (SceneObject item in session.Selection)
            {
                Bounds bounds = view.TransformBounds(item.DocumentBounds);
                Outline(context, bounds, 0x805B93F8);
            }
        }

        Bounds selection = view.TransformBounds(session.SelectionBounds);
        Outline(context, selection, Accent);

        Vec2[] handles = session.ScaleHandlesInScreen();
        foreach (Vec2 handle in handles)
        {
            context.FillRect(handle.X - 4.5f, handle.Y - 4.5f, 9f, 9f, ChipDark);
            context.FillRect(handle.X - 3f, handle.Y - 3f, 6f, 6f, ChipLight);
        }
    }

    private static void Outline(CanvasContext context, Bounds rect, uint color)
    {
        context.FillRect(rect.Left, rect.Top, rect.Width, 1f, color);
        context.FillRect(rect.Left, rect.Bottom - 1f, rect.Width, 1f, color);
        context.FillRect(rect.Left, rect.Top, 1f, rect.Height, color);
        context.FillRect(rect.Right - 1f, rect.Top, 1f, rect.Height, color);
    }

    private static void DrawNodeEditing(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        if (session.EditTarget is not { } target || session.Tool != ToolKind.Direct)
        {
            return;
        }

        Matrix3x2 matrix = target.DocumentTransform * view;
        (int Sub, int Node)? selected = session.SelectedNode;

        if (selected is { } chosen
            && chosen.Sub < target.Path.SubPaths.Count
            && chosen.Node < target.Path.SubPaths[chosen.Sub].Nodes.Count)
        {
            PathNode node = target.Path.SubPaths[chosen.Sub].Nodes[chosen.Node];
            Vec2 anchor = matrix.Apply(node.Point);
            if (node.HasIn)
            {
                Vec2 incoming = matrix.Apply(node.In);
                Line(context, anchor, incoming, 0xCC5B93F8);
                context.FillRect(incoming.X - 3f, incoming.Y - 3f, 6f, 6f, ChipLight);
            }

            if (node.HasOut)
            {
                Vec2 outgoing = matrix.Apply(node.Out);
                Line(context, anchor, outgoing, 0xCC5B93F8);
                context.FillRect(outgoing.X - 3f, outgoing.Y - 3f, 6f, 6f, ChipLight);
            }
        }

        for (int sub = 0; sub < target.Path.SubPaths.Count; sub++)
        {
            List<PathNode> nodes = target.Path.SubPaths[sub].Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Vec2 point = matrix.Apply(nodes[i].Point);
                bool isSelected = selected is { } sel && sel.Sub == sub && sel.Node == i;
                float size = isSelected ? 8f : 6f;
                context.FillRect(point.X - (size * 0.5f) - 1f, point.Y - (size * 0.5f) - 1f, size + 2f, size + 2f, ChipDark);
                context.FillRect(point.X - (size * 0.5f), point.Y - (size * 0.5f), size, size, isSelected ? Accent : ChipLight);
            }
        }
    }

    private static void DrawPen(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        if (!session.IsPenActive)
        {
            return;
        }

        VPath preview = new();
        VSubPath contour = preview.Add(new VSubPath());
        contour.Nodes.AddRange(session.PenNodes);
        contour.Add(PathNode.Corner(session.PenPreview));

        foreach (FlatContour flat in preview.Flatten(view, 0.4f))
        {
            Vec2[] points = flat.Points;
            for (int i = 0; i + 1 < points.Length; i++)
            {
                DashLine(context, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, 0xDD5B93F8);
            }
        }

        for (int i = 0; i < session.PenNodes.Count; i++)
        {
            Vec2 point = view.Apply(session.PenNodes[i].Point);
            float size = i == 0 ? 9f : 7f;
            context.FillRect(point.X - (size * 0.5f) - 1f, point.Y - (size * 0.5f) - 1f, size + 2f, size + 2f, ChipDark);
            context.FillRect(point.X - (size * 0.5f), point.Y - (size * 0.5f), size, size, i == 0 ? 0xFFF5A524 : ChipLight);
        }
    }

    private static void DrawPencil(CanvasContext context, EditorSession session, Matrix3x2 view)
    {
        IReadOnlyList<Vec2> points = session.PencilPoints;
        if (points.Count < 2)
        {
            return;
        }

        Vec2 previous = view.Apply(points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            Vec2 current = view.Apply(points[i]);
            Line(context, previous, current, 0xDD5B93F8);
            previous = current;
        }
    }

    private static void DrawMarquee(CanvasContext context, EditorSession session)
    {
        if (session.Marquee is not { } marquee)
        {
            return;
        }

        Vec2 topLeft = session.DocumentToScreen(new Vec2(marquee.Left, marquee.Top));
        Vec2 bottomRight = session.DocumentToScreen(new Vec2(marquee.Right, marquee.Bottom));
        float width = bottomRight.X - topLeft.X;
        float height = bottomRight.Y - topLeft.Y;
        if (width < 0.5f || height < 0.5f)
        {
            return;
        }

        context.FillRect(topLeft.X, topLeft.Y, width, height, AccentSoft);
        DashLine(context, topLeft.X, topLeft.Y, bottomRight.X, topLeft.Y, Accent);
        DashLine(context, bottomRight.X, topLeft.Y, bottomRight.X, bottomRight.Y, Accent);
        DashLine(context, bottomRight.X, bottomRight.Y, topLeft.X, bottomRight.Y, Accent);
        DashLine(context, topLeft.X, bottomRight.Y, topLeft.X, topLeft.Y, Accent);
    }

    private static void DrawLasso(CanvasContext context, EditorSession session)
    {
        IReadOnlyList<Vec2> points = session.LassoPoints;
        if (points.Count < 2)
        {
            return;
        }

        Vec2 theFirst = session.DocumentToScreen(points[0]);
        Vec2 previous = theFirst;
        for (int i = 1; i < points.Count; i++)
        {
            Vec2 current = session.DocumentToScreen(points[i]);
            Line(context, previous, current, Accent);
            previous = current;
        }

        if (points.Count > 3)
        {
            DashLine(context, previous.X, previous.Y, theFirst.X, theFirst.Y, 0x885B93F8);
        }
    }

    private static void DrawEmptyHint(CanvasContext context, EditorSession session)
    {
        if (session.Document.Root.SelfAndDescendants().Any(item => item is PathObject or TextObject))
        {
            return;
        }

        Bounds page = session.Document.Active.Bounds;
        Vec2 anchor = session.DocumentToScreen(new Vec2(page.Left + 26f, page.Bottom + 30f));
        context.DrawText(
            new Rect(anchor.X, anchor.Y, 480f, 18f),
            "Draw with the tools on the left  ·  press M for a rectangle, P for the pen",
            HintStyle);
    }

    private static void DrawRulers(CanvasContext context, EditorSession session)
    {
        float size = session.RulerSize;
        if (size <= 0f)
        {
            return;
        }

        float width = session.ViewWidth;
        float height = session.ViewHeight;
        Bounds clip = session.CanvasClip;

        context.FillRect(0f, 0f, width, size, RulerFill);
        context.FillRect(0f, 0f, size, height, RulerFill);
        context.FillRect(0f, size - 1f, width, 1f, 0xFF0E1013);
        context.FillRect(size - 1f, 0f, 1f, height, 0xFF0E1013);

        float[] steps = [1f, 2f, 5f, 10f, 20f, 25f, 50f, 100f, 200f, 250f, 500f, 1000f, 2000f, 5000f];
        float chosen = steps[^1];
        foreach (float step in steps)
        {
            if (step * session.Zoom >= 64f)
            {
                chosen = step;
                break;
            }
        }

        float left = (clip.Left - session.PanX) / session.Zoom;
        float right = (clip.Right - session.PanX) / session.Zoom;
        float top = (clip.Top - session.PanY) / session.Zoom;
        float bottom = (clip.Bottom - session.PanY) / session.Zoom;

        float startX = MathF.Floor(left / chosen) * chosen;
        for (float value = startX; value <= right + chosen; value += chosen)
        {
            float x = (value * session.Zoom) + session.PanX;
            if (x < size || x > width)
            {
                continue;
            }

            context.FillRect(x, size - 6f, 1f, 6f, RulerLine);
            context.DrawText(
                new Rect(x + 4f, 4f, 70f, 12f),
                value.ToString("0"),
                RulerStyle);

            float minor = chosen / 5f;
            for (int i = 1; i < 5; i++)
            {
                float mx = ((value + (minor * i)) * session.Zoom) + session.PanX;
                if (mx > size && mx < width)
                {
                    context.FillRect(mx, size - 3f, 1f, 3f, 0xFF2C323B);
                }
            }
        }

        float startY = MathF.Floor(top / chosen) * chosen;
        for (float value = startY; value <= bottom + chosen; value += chosen)
        {
            float y = (value * session.Zoom) + session.PanY;
            if (y < size || y > height)
            {
                continue;
            }

            context.FillRect(size - 6f, y, 6f, 1f, RulerLine);
            context.DrawText(
                new Rect(3f, y + 3f, size - 4f, 11f),
                value.ToString("0"),
                RulerStyle);
        }

        context.FillRect(0f, 0f, size - 1f, size - 1f, 0xFF15181C);
        context.DrawText(new Rect(5f, 5f, 16f, 11f), "px", RulerStyle);
    }

    private static Rect ToRect(Bounds bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static void Line(CanvasContext context, Vec2 from, Vec2 to, uint color)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        int steps = Math.Max(1, (int)MathF.Ceiling(length / 2f));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            context.FillRect(from.X + (dx * t), from.Y + (dy * t), 1.2f, 1.2f, color);
        }
    }

    private static void DashLine(CanvasContext context, float x0, float y0, float x1, float y1, uint color)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.5f)
        {
            return;
        }

        float ux = dx / length;
        float uy = dy / length;
        int steps = (int)MathF.Ceiling(length / 7f);
        for (int i = 0; i < steps; i++)
        {
            float start = i * 7f;
            float size = MathF.Min(4f, length - start);
            if (size <= 0f)
            {
                break;
            }

            context.FillRect(
                x0 + (ux * start),
                y0 + (uy * start),
                ux == 0f ? 1.2f : size,
                uy == 0f ? 1.2f : size,
                color);
        }
    }
}
