using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>Pointer, pen, node-editing and creation behaviour for the editor.</summary>
public sealed partial class EditorSession
{
    private HistoryEntry? _pending;
    private Vec2 _lastScreen;
    private bool _additive;
    private readonly List<Vec2> _pencilPoints = [];
    private readonly List<(bool Horizontal, float Position)> _smartGuides = [];

    /// <summary>Alignment guides produced by the current drag, in document space.</summary>
    public IReadOnlyList<(bool Horizontal, float Position)> SmartGuideLines => _smartGuides;

    /// <summary>The pencil stroke being collected, for the live overlay.</summary>
    public IReadOnlyList<Vec2> PencilPoints => _pencilPoints;

    private void BeginInteraction()
    {
        _pending ??= Capture();
    }

    private void CommitInteraction()
    {
        if (_pending is null)
        {
            return;
        }

        _undo.Push(_pending);
        _redo.Clear();
        _pending = null;
    }

    /// <summary>Begins a pointer gesture at a canvas-local position.</summary>
    public void PointerDown(Vec2 screen, bool shift, bool alt, bool middle)
    {
        _lastScreen = screen;
        Vec2 document = ScreenToDocument(screen);
        _dragStartDocument = document;
        _dragLastDocument = document;
        _dragMoved = false;
        _smartGuides.Clear();

        if (middle)
        {
            _drag = DragKind.Pan;
            return;
        }

        bool overTopRuler = ShowRulers && screen.Y >= 0f && screen.Y <= RulerSize;
        bool overLeftRuler = ShowRulers && screen.X >= 0f && screen.X <= RulerSize;
        if (overTopRuler || overLeftRuler)
        {
            if (overTopRuler && overLeftRuler)
            {
                return;
            }

            Guide guide = overTopRuler
                ? new Guide { Horizontal = true, Position = document.Y }
                : new Guide { Horizontal = false, Position = document.X };
            Document.Guides.Add(guide);
            SetStatus("Guide added. View ▸ Clear Guides removes them.");
            return;
        }

        if (Tool == ToolKind.Hand)
        {
            _drag = DragKind.Pan;
            return;
        }

        if (screen.X < RulerSize || screen.Y < RulerSize)
        {
            return;
        }

        switch (Tool)
        {
            case ToolKind.Select:
                PressSelect(screen, document, shift);
                break;
            case ToolKind.Direct:
                PressDirect(screen, document, shift);
                break;
            case ToolKind.Pen:
                PressPen(document, alt);
                break;
            case ToolKind.Pencil:
                _drag = DragKind.Pencil;
                _pencilPoints.Clear();
                _pencilPoints.Add(document);
                _pencilDistance = 0f;
                break;
            case ToolKind.Lasso:
                _drag = DragKind.Lasso;
                _lasso.Clear();
                _lasso.Add(document);
                _additive = shift;
                Touch();
                break;
            case ToolKind.Zoom:
                ZoomAt(screen, alt ? Zoom / 1.4f : Zoom * 1.4f);
                break;
            case ToolKind.Rectangle:
            case ToolKind.RoundedRectangle:
            case ToolKind.Ellipse:
            case ToolKind.Polygon:
            case ToolKind.Star:
            case ToolKind.Line:
            case ToolKind.Spiral:
                BeginCreate(document, shift, alt);
                break;
            case ToolKind.Text:
                CreateText(document);
                break;
        }
    }

    /// <summary>Updates the active gesture.</summary>
    public void PointerMove(Vec2 screen)
    {
        CursorDocument = ScreenToDocument(screen);
        CursorOnCanvas = screen.X >= RulerSize && screen.Y >= RulerSize;

        Vec2 document = CursorDocument;
        Vec2 previous = _dragLastDocument;
        _dragLastDocument = document;

        if (_drag == DragKind.None)
        {
            if (IsPenActive)
            {
                _penPreview = document;
                Touch();
            }

            return;
        }

        if (!_dragMoved && Vec2.Distance(screen, _lastScreen) > 1.5f)
        {
            _dragMoved = true;
            CommitInteraction();
        }

        switch (_drag)
        {
            case DragKind.Pan:
                PanBy(screen.X - _lastScreen.X, screen.Y - _lastScreen.Y);
                break;

            case DragKind.Move:
                if (!_dragMoved)
                {
                    break;
                }

                ApplyMove(document, InputShiftHeld);
                break;

            case DragKind.Scale:
                ApplyScale(document, InputShiftHeld, InputAltHeld);
                break;

            case DragKind.Create:
                UpdateCreate(document, InputShiftHeld, InputAltHeld);
                break;

            case DragKind.Marquee:
            case DragKind.Lasso:
                Touch();
                break;

            case DragKind.Pencil:
                AppendPencil(document);
                break;

            case DragKind.PenHandle:
                DragPenHandle(document, InputAltHeld);
                break;

            case DragKind.Node:
                if (_nodeDrag is { } node && _editTarget is not null)
                {
                    MoveNode(node, document - previous);
                }

                break;

            case DragKind.Handle:
                if (_handleDrag is { } handle && _editTarget is not null)
                {
                    DragNodeHandle(handle, document, InputAltHeld);
                }

                break;
        }

        _ = previous;
        _lastScreen = screen;
    }

    /// <summary>Finishes the active gesture.</summary>
    public void PointerUp(Vec2 screen)
    {
        Vec2 document = ScreenToDocument(screen);
        _dragLastDocument = document;

        switch (_drag)
        {
            case DragKind.Create:
                FinishCreate();
                break;

            case DragKind.Move:
            case DragKind.Scale:
            case DragKind.Node:
            case DragKind.Handle:
                if (_dragMoved)
                {
                    Notify();
                }

                break;

            case DragKind.Marquee:
                FinishMarquee();
                break;

            case DragKind.Lasso:
                FinishLasso();
                break;

            case DragKind.Pencil:
                FinishPencil();
                break;
        }

        _drag = DragKind.None;
        _scaleHandle = -1;
        _nodeDrag = null;
        _handleDrag = null;
        _smartGuides.Clear();
        _pending = null;
        Touch();
    }

    /// <summary>Clears hover state when the pointer leaves the canvas.</summary>
    public void PointerLeave()
    {
        CursorOnCanvas = false;
        Touch();
    }

    /// <summary>Scrolls or zooms the view.</summary>
    public void Scroll(float horizontal, float vertical, Vec2 screen, bool control, bool shift)
    {
        if (control)
        {
            ZoomByWheel(vertical, screen);
            return;
        }

        if (shift)
        {
            PanBy(vertical, 0f);
        }
        else
        {
            PanBy(horizontal, vertical);
        }
    }

    // The host exposes modifier state for wheel and pointer gestures.
    private static bool InputShiftHeld => Donkeh.Windowing.InputState.Shift;

    private static bool InputAltHeld => Donkeh.Windowing.InputState.Alt;

    // ---------------------------------------------------------------- select

    private void PressSelect(Vec2 screen, Vec2 document, bool shift)
    {
        if (Selection.Count > 0 && TryHitScaleHandle(screen, out int handle))
        {
            BeginInteraction();
            _scaleHandle = handle;
            _dragStartBounds = SelectionBounds;
            StoreStartTransforms();
            _drag = DragKind.Scale;
            return;
        }

        SceneObject? hit = HitTest.TopmostObject(Document, document, 5f / Zoom);
        if (hit is null)
        {
            if (!shift)
            {
                Selection.Clear();
                _editTarget = null;
                _selectedNode = null;
            }

            _additive = shift;
            _drag = DragKind.Marquee;
            Notify();
            return;
        }

        SceneObject target = HitTest.ResolveSelection(hit);
        if (shift)
        {
            Toggle(target);
            return;
        }

        if (!Selection.Contains(target))
        {
            Selection.Clear();
            Selection.Add(target);
        }

        _editTarget = null;
        _selectedNode = null;
        BeginInteraction();
        StoreStartTransforms();
        _drag = DragKind.Move;
        Notify();
    }

    private void ApplyMove(Vec2 document, bool constrain)
    {
        Vec2 delta = document - _dragStartDocument;
        if (constrain)
        {
            if (MathF.Abs(delta.X) > MathF.Abs(delta.Y))
            {
                delta = new Vec2(delta.X, 0f);
            }
            else
            {
                delta = new Vec2(0f, delta.Y);
            }
        }

        delta = ApplySnapping(delta);
        Matrix3x2 move = Mat.Translate(delta);
        foreach (SceneObject item in Selection)
        {
            ApplyFromStart(item, move);
        }

        _toolHint = $"Move {delta.X:0.#}, {delta.Y:0.#}";
        Touch();
    }

    /// <summary>Snaps a move delta to the grid, object edges or artboard edges.</summary>
    private Vec2 ApplySnapping(Vec2 delta)
    {
        if (Selection.Count == 0)
        {
            return delta;
        }

        Bounds start = _dragStartBounds;
        _smartGuides.Clear();

        if (SnapToGrid && GridSize > 0.5f)
        {
            float left = MathF.Round((start.Left + delta.X) / GridSize) * GridSize;
            float top = MathF.Round((start.Top + delta.Y) / GridSize) * GridSize;
            delta = new Vec2(left - start.Left, top - start.Top);
        }

        if (!SnapToObjects)
        {
            return delta;
        }

        float threshold = 6f / Zoom;
        List<float> verticalTargets = [];
        List<float> horizontalTargets = [];
        CollectSnapTargets(verticalTargets, horizontalTargets);

        float leftEdge = start.Left + delta.X;
        float topEdge = start.Top + delta.Y;
        float[] movingX = [leftEdge, start.Center.X + delta.X, start.Right + delta.X];
        float[] movingY = [topEdge, start.Center.Y + delta.Y, start.Bottom + delta.Y];

        float bestDx = 0f;
        float bestXd = threshold;
        float guideX = 0f;
        foreach (float target in verticalTargets)
        {
            foreach (float probe in movingX)
            {
                float diff = target - probe;
                if (MathF.Abs(diff) < bestXd)
                {
                    bestXd = MathF.Abs(diff);
                    bestDx = diff;
                    guideX = target;
                }
            }
        }

        float bestDy = 0f;
        float bestYd = threshold;
        float guideY = 0f;
        foreach (float target in horizontalTargets)
        {
            foreach (float probe in movingY)
            {
                float diff = target - probe;
                if (MathF.Abs(diff) < bestYd)
                {
                    bestYd = MathF.Abs(diff);
                    bestDy = diff;
                    guideY = target;
                }
            }
        }

        if (bestXd < threshold)
        {
            delta = new Vec2(delta.X + bestDx, delta.Y);
            _smartGuides.Add((false, guideX));
        }

        if (bestYd < threshold)
        {
            delta = new Vec2(delta.X, delta.Y + bestDy);
            _smartGuides.Add((true, guideY));
        }

        return delta;
    }

    /// <summary>Collects every useful vertical and horizontal snap line.</summary>
    private void CollectSnapTargets(List<float> vertical, List<float> horizontal)
    {
        Artboard artboard = Document.Active;
        vertical.AddRange([artboard.Bounds.Left, artboard.Bounds.Center.X, artboard.Bounds.Right]);
        horizontal.AddRange([artboard.Bounds.Top, artboard.Bounds.Center.Y, artboard.Bounds.Bottom]);

        foreach (Guide guide in Document.Guides)
        {
            if (guide.Horizontal)
            {
                horizontal.Add(guide.Position);
            }
            else
            {
                vertical.Add(guide.Position);
            }
        }

        foreach (SceneObject item in Document.Root.SelfAndDescendants())
        {
            if (item is GroupObject || Selection.Contains(item) || IsInsideSelection(item))
            {
                continue;
            }

            Bounds bounds = item.DocumentBounds;
            if (bounds.IsEmpty)
            {
                continue;
            }

            vertical.AddRange([bounds.Left, bounds.Center.X, bounds.Right]);
            horizontal.AddRange([bounds.Top, bounds.Center.Y, bounds.Bottom]);
        }
    }

    /// <summary>Snaps a free document point to nearby object, guide or artboard lines.</summary>
    private Vec2 SnapPoint(Vec2 point)
    {
        _smartGuides.Clear();
        if (!SnapToObjects)
        {
            return point;
        }

        List<float> vertical = [];
        List<float> horizontal = [];
        CollectSnapTargets(vertical, horizontal);
        float threshold = 6f / Zoom;
        Vec2 result = point;

        float bestX = threshold;
        float bestValue = point.X;
        float guideX = 0f;
        foreach (float target in vertical)
        {
            float distance = MathF.Abs(target - point.X);
            if (distance < bestX)
            {
                bestX = distance;
                bestValue = target;
                guideX = target;
            }
        }

        if (bestX < threshold)
        {
            result = new Vec2(bestValue, result.Y);
            _smartGuides.Add((false, guideX));
        }

        float bestY = threshold;
        bestValue = point.Y;
        float guideY = 0f;
        foreach (float target in horizontal)
        {
            float distance = MathF.Abs(target - point.Y);
            if (distance < bestY)
            {
                bestY = distance;
                bestValue = target;
                guideY = target;
            }
        }

        if (bestY < threshold)
        {
            result = new Vec2(result.X, bestValue);
            _smartGuides.Add((true, guideY));
        }

        return result;
    }

    private bool IsInsideSelection(SceneObject item)
    {
        foreach (SceneObject selected in Selection)
        {
            if (selected is GroupObject group && group.SelfAndDescendants().Contains(item))
            {
                return true;
            }
        }

        return false;
    }

    private void StoreStartTransforms()
    {
        _startTransforms.Clear();
        foreach (SceneObject item in Selection)
        {
            _startTransforms[item.Id] = item.Transform;
        }
    }

    private void ApplyFromStart(SceneObject item, Matrix3x2 documentMatrix)
    {
        if (!_startTransforms.TryGetValue(item.Id, out Matrix3x2 start))
        {
            return;
        }

        Matrix3x2 parent = Matrix3x2.Identity;
        GroupObject? node = item.Parent;
        while (node is not null)
        {
            parent *= node.Transform;
            node = node.Parent;
        }

        if (Matrix3x2.Invert(parent, out Matrix3x2 inverse))
        {
            item.Transform = start * parent * documentMatrix * inverse;
        }
        else
        {
            item.Transform = start * documentMatrix;
        }
    }

    private void ApplyScale(Vec2 document, bool uniform, bool fromCenter)
    {
        document = SnapPoint(document);
        Bounds bounds = _dragStartBounds;
        if (bounds.Width < 0.01f && bounds.Height < 0.01f)
        {
            return;
        }

        (Vec2 anchor, Vec2 start) = ScaleAnchor(bounds, _scaleHandle, fromCenter);
        float sx = MathF.Abs(start.X - anchor.X) < 0.001f ? 1f : (document.X - anchor.X) / (start.X - anchor.X);
        float sy = MathF.Abs(start.Y - anchor.Y) < 0.001f ? 1f : (document.Y - anchor.Y) / (start.Y - anchor.Y);

        if (_scaleHandle is 1 or 5)
        {
            sx = 1f;
        }

        if (_scaleHandle is 3 or 7)
        {
            sy = 1f;
        }

        if (uniform)
        {
            float factor = MathF.Abs(sx) > MathF.Abs(sy) ? sx : sy;
            if (_scaleHandle is not (1 or 5))
            {
                sx = factor;
            }

            if (_scaleHandle is not (3 or 7))
            {
                sy = factor;
            }
        }

        sx = MathF.Abs(sx) < 0.002f ? 0.002f * MathF.Sign(sx == 0f ? 1f : sx) : sx;
        sy = MathF.Abs(sy) < 0.002f ? 0.002f * MathF.Sign(sy == 0f ? 1f : sy) : sy;

        Matrix3x2 scale = Mat.ScaleAbout(anchor, sx, sy);
        foreach (SceneObject item in Selection)
        {
            ApplyFromStart(item, scale);
        }

        _toolHint = $"Scale {sx * 100f:0}% × {sy * 100f:0}%";
        Touch();
    }

    private static (Vec2 Anchor, Vec2 Start) ScaleAnchor(Bounds bounds, int handle, bool fromCenter)
    {
        Vec2 center = bounds.Center;
        Vec2 topLeft = new(bounds.Left, bounds.Top);
        Vec2 topCenter = new(bounds.Center.X, bounds.Top);
        Vec2 topRight = new(bounds.Right, bounds.Top);
        Vec2 rightCenter = new(bounds.Right, bounds.Center.Y);
        Vec2 bottomRight = new(bounds.Right, bounds.Bottom);
        Vec2 bottomCenter = new(bounds.Center.X, bounds.Bottom);
        Vec2 bottomLeft = new(bounds.Left, bounds.Bottom);
        Vec2 leftCenter = new(bounds.Left, bounds.Center.Y);

        if (fromCenter)
        {
            Vec2 moving = handle switch
            {
                0 => topLeft,
                1 => topCenter,
                2 => topRight,
                3 => rightCenter,
                4 => bottomRight,
                5 => bottomCenter,
                6 => bottomLeft,
                _ => leftCenter,
            };
            return (center, moving);
        }

        return handle switch
        {
            0 => (bottomRight, topLeft),
            1 => (bottomCenter, topCenter),
            2 => (bottomLeft, topRight),
            3 => (leftCenter, rightCenter),
            4 => (topLeft, bottomRight),
            5 => (topCenter, bottomCenter),
            6 => (topRight, bottomLeft),
            _ => (rightCenter, leftCenter),
        };
    }

    /// <summary>Screen-space positions of the eight scale handles.</summary>
    public Vec2[] ScaleHandlesInScreen()
    {
        Bounds bounds = SelectionBounds;
        Vec2 topLeft = DocumentToScreen(new Vec2(bounds.Left, bounds.Top));
        Vec2 bottomRight = DocumentToScreen(new Vec2(bounds.Right, bounds.Bottom));
        float midX = (topLeft.X + bottomRight.X) * 0.5f;
        float midY = (topLeft.Y + bottomRight.Y) * 0.5f;
        return
        [
            topLeft,
            new Vec2(midX, topLeft.Y),
            new Vec2(bottomRight.X, topLeft.Y),
            new Vec2(bottomRight.X, midY),
            bottomRight,
            new Vec2(midX, bottomRight.Y),
            new Vec2(topLeft.X, bottomRight.Y),
            new Vec2(topLeft.X, midY),
        ];
    }

    private bool TryHitScaleHandle(Vec2 screen, out int handle)
    {
        Vec2[] handles = ScaleHandlesInScreen();
        for (int i = 0; i < handles.Length; i++)
        {
            if (Vec2.Distance(screen, handles[i]) <= 7f)
            {
                handle = i;
                return true;
            }
        }

        handle = -1;
        return false;
    }

    private void FinishMarquee()
    {
        Bounds rect = Bounds.FromPoints(_dragStartDocument, _dragLastDocument).Normalized();
        if (rect.Width < 1f || rect.Height < 1f)
        {
            return;
        }

        if (!_additive)
        {
            Selection.Clear();
        }

        foreach (PathObject path in HitTest.InRect(Document, rect))
        {
            SceneObject? target = HitTest.ResolveSelection(path);
            if (target is not null && !Selection.Contains(target))
            {
                Selection.Add(target);
            }
        }

        _toolHint = $"{Selection.Count} selected";
        Notify();
    }

    private void FinishLasso()
    {
        if (_lasso.Count < 3)
        {
            _lasso.Clear();
            return;
        }

        if (!_additive)
        {
            Selection.Clear();
        }

        foreach (PathObject path in HitTest.InLasso(Document, _lasso))
        {
            SceneObject? target = HitTest.ResolveSelection(path);
            if (target is not null && !Selection.Contains(target))
            {
                Selection.Add(target);
            }
        }

        _toolHint = $"{Selection.Count} selected";
        _lasso.Clear();
        Notify();
    }

    // ---------------------------------------------------------------- create

    private static ShapeKind ShapeKindOf(ToolKind tool) => tool switch
    {
        ToolKind.Rectangle => ShapeKind.Rectangle,
        ToolKind.RoundedRectangle => ShapeKind.RoundedRectangle,
        ToolKind.Ellipse => ShapeKind.Ellipse,
        ToolKind.Polygon => ShapeKind.Polygon,
        ToolKind.Star => ShapeKind.Star,
        ToolKind.Line => ShapeKind.Line,
        _ => ShapeKind.Spiral,
    };

    private void BeginCreate(Vec2 document, bool shift, bool alt)
    {
        BeginInteraction();
        _createFromCenter = alt;
        _createAnchor = document;
        _drag = DragKind.Create;

        ShapeSpec spec = new() { Kind = ShapeKindOf(Tool), Width = 1f, Height = 1f };
        if (spec.Kind == ShapeKind.RoundedRectangle)
        {
            spec.CornerRadius = DefaultCornerRadius;
        }

        if (spec.Kind is ShapeKind.Polygon or ShapeKind.Star)
        {
            spec.Points = DefaultPoints;
            spec.InnerRatio = DefaultInnerRatio;
        }

        PathObject created = new()
        {
            Name = spec.DisplayName,
            Shape = spec,
            Fill = spec.Kind == ShapeKind.Line ? null : Paint.Solid(FillColor),
            Stroke = spec.Kind == ShapeKind.Line
                ? new StrokeStyle { Paint = Paint.Solid(StrokeColor), Width = MathF.Max(StrokeWidth, 1f), Join = LineJoin.Round, Cap = LineCap.Round }
                : null,
            Transform = Mat.Translate(document),
        };
        created.Rebuild();
        _created = created;
        ActiveLayer.Add(created);
        Selection.Clear();
        Selection.Add(created);
        Touch();
    }

    private void UpdateCreate(Vec2 document, bool shift, bool alt)
    {
        if (_created?.Shape is not { } spec)
        {
            return;
        }

        document = SnapPoint(document);
        Vec2 delta = document - _createAnchor;
        float width;
        float height;
        Vec2 center;
        float rotation = 0f;

        if (spec.Kind == ShapeKind.Line)
        {
            Vec2 direction = delta;
            if (shift)
            {
                float angle = MathF.Round(MathF.Atan2(delta.Y, delta.X) / (MathF.PI / 4f)) * (MathF.PI / 4f);
                float length = delta.Length;
                direction = new Vec2(MathF.Cos(angle) * length, MathF.Sin(angle) * length);
            }

            spec.Width = MathF.Max(direction.Length, 0.5f);
            spec.Height = 0f;
            rotation = MathF.Atan2(direction.Y, direction.X);
            center = _createAnchor + (direction * 0.5f);
            _created.Transform = Mat.Translate(center) * Mat.Rotate(rotation);
        }
        else
        {
            if (alt || _createFromCenter)
            {
                width = MathF.Abs(delta.X) * 2f;
                height = MathF.Abs(delta.Y) * 2f;
                center = _createAnchor;
            }
            else
            {
                width = MathF.Abs(delta.X);
                height = MathF.Abs(delta.Y);
                center = new Vec2(
                    _createAnchor.X + (delta.X * 0.5f),
                    _createAnchor.Y + (delta.Y * 0.5f));
            }

            if (shift && spec.Kind is not (ShapeKind.Spiral or ShapeKind.Polygon or ShapeKind.Star))
            {
                float side = MathF.Max(width, height);
                width = side;
                height = side;
                if (!alt && !_createFromCenter)
                {
                    center = new Vec2(
                        _createAnchor.X + (MathF.Sign(delta.X == 0f ? 1f : delta.X) * side * 0.5f),
                        _createAnchor.Y + (MathF.Sign(delta.Y == 0f ? 1f : delta.Y) * side * 0.5f));
                }
            }

            spec.Width = MathF.Max(width, 0.5f);
            spec.Height = MathF.Max(height, 0.5f);
            _created.Transform = Mat.Translate(center);
        }

        _created.Rebuild();
        _ = rotation;
        _toolHint = $"{spec.DisplayName} {spec.Width:0.#} × {spec.Height:0.#}";
        Touch();
    }

    private void FinishCreate()
    {
        if (_created is null)
        {
            return;
        }

        Bounds bounds = _created.DocumentBounds;
        bool tiny = bounds.Width < 2f && bounds.Height < 2f;
        if (tiny)
        {
            _created.Parent?.Remove(_created);
            Selection.Clear();
            _created = null;
            _pending = null;
            SetStatus("Drag to draw a shape.");
            return;
        }

        CommitInteraction();
        _toolHint = $"{_created.DisplayName} created";
        _created = null;
        Notify();
    }

    // ------------------------------------------------------------------ pen

    private void PressPen(Vec2 document, bool alt)
    {
        if (_penNodes.Count >= 2 && Vec2.Distance(document, _penNodes[0].Point) <= 9f / Zoom)
        {
            FinishPen(true);
            return;
        }

        if (_penNodes.Count >= 2 && Vec2.Distance(document, _penNodes[^1].Point) <= 7f / Zoom)
        {
            FinishPen(false);
            return;
        }

        _penNodes.Add(PathNode.Corner(document));
        _penPreview = document;
        _drag = DragKind.PenHandle;
        Touch();
    }

    private void DragPenHandle(Vec2 document, bool breakSymmetry)
    {
        if (_penNodes.Count == 0)
        {
            return;
        }

        PathNode node = _penNodes[^1];
        float distance = Vec2.Distance(document, node.Point);
        if (distance < 0.5f / Zoom)
        {
            return;
        }

        node.Out = document;
        node.HasOut = true;
        if (!breakSymmetry)
        {
            node.In = node.Point - (document - node.Point);
            node.HasIn = true;
        }

        _penNodes[^1] = node;
        _penPreview = document;
        Touch();
    }

    /// <summary>Completes the pen path, either closed or open.</summary>
    public void FinishPen(bool closed)
    {
        if (_penNodes.Count < 2)
        {
            CancelPen();
            return;
        }

        PushHistory("Pen Path");
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed));
        sub.Nodes.AddRange(_penNodes);

        PathObject created = new()
        {
            Name = "Path",
            Path = path,
            Fill = closed ? Paint.Solid(FillColor) : null,
            Stroke = new StrokeStyle
            {
                Paint = Paint.Solid(StrokeColor),
                Width = MathF.Max(StrokeWidth, 1f),
                Join = LineJoin.Round,
                Cap = LineCap.Round,
            },
        };

        ActiveLayer.Add(created);
        Selection.Clear();
        Selection.Add(created);
        _penNodes.Clear();
        _penPreview = Vec2.Zero;
        _drag = DragKind.None;
        _toolHint = closed ? "Closed path created" : "Path created";
        Notify();
    }

    private void CancelPen()
    {
        if (_penNodes.Count == 0)
        {
            return;
        }

        _penNodes.Clear();
        _penPreview = Vec2.Zero;
        _drag = DragKind.None;
        _toolHint = "Pen cancelled";
        Notify();
    }

    // ---------------------------------------------------------------- pencil

    private void AppendPencil(Vec2 document)
    {
        if (_pencilPoints.Count == 0)
        {
            _pencilPoints.Add(document);
            return;
        }

        float step = Vec2.Distance(_pencilPoints[^1], document);
        if (step < 1.4f / Zoom)
        {
            return;
        }

        _pencilDistance += step;
        _pencilPoints.Add(document);
        Touch();
    }

    private void FinishPencil()
    {
        if (_pencilPoints.Count < 2)
        {
            _pencilPoints.Clear();
            return;
        }

        PushHistory("Pencil Path");
        bool closed = _pencilPoints.Count > 6
            && Vec2.Distance(_pencilPoints[0], _pencilPoints[^1]) < 14f / Zoom;

        PathObject created = new()
        {
            Name = "Freehand",
            Path = BuildSmoothPath(_pencilPoints, closed),
            Fill = closed ? Paint.Solid(FillColor) : null,
            Stroke = new StrokeStyle
            {
                Paint = Paint.Solid(StrokeColor),
                Width = MathF.Max(StrokeWidth, 1f),
                Join = LineJoin.Round,
                Cap = LineCap.Round,
            },
        };

        ActiveLayer.Add(created);
        Selection.Clear();
        Selection.Add(created);
        _pencilPoints.Clear();
        _toolHint = "Freehand path created";
        Notify();
    }

    /// <summary>Fits a smooth cubic path through sampled points.</summary>
    public static VPath BuildSmoothPath(IReadOnlyList<Vec2> points, bool closed)
    {
        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed));
        int count = points.Count;
        if (count < 2)
        {
            return path;
        }

        for (int i = 0; i < count; i++)
        {
            Vec2 previous = points[closed ? (i - 1 + count) % count : Math.Max(i - 1, 0)];
            Vec2 next = points[closed ? (i + 1) % count : Math.Min(i + 1, count - 1)];
            Vec2 tangent = (next - previous) / 6f;
            sub.Add(PathNode.Smooth(points[i], points[i] - tangent, points[i] + tangent));
        }

        return path;
    }

    // ------------------------------------------------------------ node edit

    private void PressDirect(Vec2 screen, Vec2 document, bool shift)
    {
        if (_editTarget is not null && TryPickNode(screen, document))
        {
            return;
        }

        PathObject? path = HitTest.TopmostPath(Document, document, 5f / Zoom);
        if (path is null)
        {
            DeselectAll();
            return;
        }

        if (!Selection.Contains(path))
        {
            Selection.Clear();
            Selection.Add(path);
        }

        _editTarget = path;
        _selectedNode = null;
        BeginInteraction();
        TryPickNode(screen, document);
        _toolHint = "Node editing — drag anchors and handles, Delete removes a node.";
        Notify();
    }

    private bool TryPickNode(Vec2 screen, Vec2 document)
    {
        if (_editTarget is null)
        {
            return false;
        }

        Matrix3x2 toDocument = _editTarget.DocumentTransform;
        if (!Matrix3x2.Invert(toDocument, out Matrix3x2 inverse))
        {
            return false;
        }

        Vec2 local = inverse.Apply(document);

        if (_selectedNode is { } selected
            && selected.Sub < _editTarget.Path.SubPaths.Count
            && selected.Node < _editTarget.Path.SubPaths[selected.Sub].Nodes.Count)
        {
            PathNode current = _editTarget.Path.SubPaths[selected.Sub].Nodes[selected.Node];
            if (current.HasOut && Vec2.Distance(screen, DocumentToScreen(toDocument.Apply(current.Out))) <= 7f)
            {
                _handleDrag = (selected.Sub, selected.Node, true);
                _drag = DragKind.Handle;
                BeginInteraction();
                return true;
            }

            if (current.HasIn && Vec2.Distance(screen, DocumentToScreen(toDocument.Apply(current.In))) <= 7f)
            {
                _handleDrag = (selected.Sub, selected.Node, false);
                _drag = DragKind.Handle;
                BeginInteraction();
                return true;
            }
        }

        float bestDistance = 8f;
        (int Sub, int Node)? best = null;
        for (int sub = 0; sub < _editTarget.Path.SubPaths.Count; sub++)
        {
            VSubPath contour = _editTarget.Path.SubPaths[sub];
            for (int node = 0; node < contour.Nodes.Count; node++)
            {
                Vec2 screenPoint = DocumentToScreen(toDocument.Apply(contour.Nodes[node].Point));
                float distance = Vec2.Distance(screen, screenPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (sub, node);
                }
            }
        }

        if (best is { } hit)
        {
            _selectedNode = hit;
            _nodeDrag = hit;
            _drag = DragKind.Node;
            BeginInteraction();
            Notify();
            return true;
        }

        _ = local;
        return false;
    }

    private void MoveNode((int Sub, int Node) node, Vec2 documentDelta)
    {
        if (_editTarget is null)
        {
            return;
        }

        if (_editTarget.Shape is not null)
        {
            // Editing geometry turns a live shape into a free path, but the
            // original parameters remain available through undo.
            _editTarget.BakeShape();
        }

        Matrix3x2 toDocument = _editTarget.DocumentTransform;
        if (!Matrix3x2.Invert(toDocument, out Matrix3x2 inverse))
        {
            return;
        }

        Vec2 localDelta = inverse.ApplyVector(documentDelta);
        if (node.Sub >= _editTarget.Path.SubPaths.Count)
        {
            return;
        }

        VSubPath contour = _editTarget.Path.SubPaths[node.Sub];
        if (node.Node >= contour.Nodes.Count)
        {
            return;
        }

        PathNode current = contour.Nodes[node.Node];
        current.Point += localDelta;
        if (current.HasIn)
        {
            current.In += localDelta;
        }

        if (current.HasOut)
        {
            current.Out += localDelta;
        }

        contour.Nodes[node.Node] = current;
        _editTarget.InvalidateGeometry();
        _toolHint = $"Node {node.Node + 1} moved";
        Touch();
    }

    private void DragNodeHandle((int Sub, int Node, bool Out) handle, Vec2 document, bool breakSymmetry)
    {
        if (_editTarget is null || handle.Sub >= _editTarget.Path.SubPaths.Count)
        {
            return;
        }

        if (_editTarget.Shape is not null)
        {
            _editTarget.BakeShape();
        }

        Matrix3x2 toDocument = _editTarget.DocumentTransform;
        if (!Matrix3x2.Invert(toDocument, out Matrix3x2 inverse))
        {
            return;
        }

        Vec2 local = inverse.Apply(document);
        VSubPath contour = _editTarget.Path.SubPaths[handle.Sub];
        if (handle.Node >= contour.Nodes.Count)
        {
            return;
        }

        PathNode node = contour.Nodes[handle.Node];
        if (handle.Out)
        {
            node.Out = local;
            node.HasOut = true;
            if (!breakSymmetry)
            {
                node.In = node.Point - (local - node.Point);
                node.HasIn = true;
            }
        }
        else
        {
            node.In = local;
            node.HasIn = true;
            if (!breakSymmetry)
            {
                node.Out = node.Point - (local - node.Point);
                node.HasOut = true;
            }
        }

        contour.Nodes[handle.Node] = node;
        _editTarget.InvalidateGeometry();
        Touch();
    }

    /// <summary>Leaves node-editing mode.</summary>
    public void EndNodeEdit()
    {
        _editTarget = null;
        _selectedNode = null;
        _nodeDrag = null;
        _handleDrag = null;
    }

    /// <summary>Deletes the selected path node.</summary>
    public void DeleteSelectedNode()
    {
        if (_editTarget is null || _selectedNode is not { } selected)
        {
            return;
        }

        if (selected.Sub >= _editTarget.Path.SubPaths.Count)
        {
            return;
        }

        PushHistory("Delete Node");
        VSubPath contour = _editTarget.Path.SubPaths[selected.Sub];
        if (selected.Node < contour.Nodes.Count)
        {
            contour.Nodes.RemoveAt(selected.Node);
        }

        if (contour.Nodes.Count < 2)
        {
            _editTarget.Path.SubPaths.RemoveAt(selected.Sub);
        }

        _selectedNode = null;
        _editTarget.InvalidateGeometry();
        if (_editTarget.Path.SubPaths.Count == 0)
        {
            SceneObject target = _editTarget;
            EndNodeEdit();
            target.Parent?.Remove(target);
            Selection.Remove(target);
        }

        _toolHint = "Node deleted";
        Notify();
    }

    // ------------------------------------------------------------------ text

    private void CreateText(Vec2 document)
    {
        PushHistory("Text");
        TextObject text = new()
        {
            Text = "Text",
            FontFamily = DefaultFontFamily,
            FontSize = 48f,
            Fill = Paint.Solid(FillColor),
            Transform = Mat.Translate(document),
        };

        ActiveLayer.Add(text);
        Selection.Clear();
        Selection.Add(text);
        _toolHint = "Text placed. Edit the content in the properties panel.";
        Notify();
    }

    /// <summary>Updates the content of selected text objects.</summary>
    public void SetTextContent(string text)
    {
        List<TextObject> items = Selection.OfType<TextObject>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        PushHistory("Edit Text");
        foreach (TextObject item in items)
        {
            item.Text = text;
        }

        Notify();
    }

    /// <summary>Updates the font size of selected text objects.</summary>
    public void SetTextSize(float size)
    {
        List<TextObject> items = Selection.OfType<TextObject>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        PushHistory("Text Size");
        foreach (TextObject item in items)
        {
            item.FontSize = Math.Clamp(size, 4f, 512f);
        }

        Notify();
    }

    /// <summary>Updates the font family of selected text objects.</summary>
    public void SetTextFont(string family)
    {
        List<TextObject> items = Selection.OfType<TextObject>().ToList();
        if (items.Count == 0 || string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        PushHistory("Font");
        foreach (TextObject item in items)
        {
            item.FontFamily = family;
        }

        Notify();
    }

    /// <summary>Toggles weight on selected text objects.</summary>
    public void ToggleTextBold()
    {
        List<TextObject> items = Selection.OfType<TextObject>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        PushHistory("Bold");
        bool value = !items[0].Bold;
        foreach (TextObject item in items)
        {
            item.Bold = value;
        }

        Notify();
    }

    /// <summary>Converts selected live shapes into free paths.</summary>
    public void ConvertShapesToPaths()
    {
        List<PathObject> shapes = Selection.OfType<PathObject>().Where(path => path.Shape is not null).ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        PushHistory("Convert to Path");
        foreach (PathObject shape in shapes)
        {
            shape.BakeShape();
        }

        _toolHint = "Converted to paths";
        Notify();
    }

    /// <summary>Reverses the direction of selected free paths.</summary>
    public void ReverseSelectedPaths()
    {
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        if (paths.Count == 0)
        {
            return;
        }

        PushHistory("Reverse Path");
        foreach (PathObject path in paths)
        {
            path.BakeShape();
            foreach (VSubPath sub in path.Path.SubPaths)
            {
                sub.Nodes.Reverse();
                for (int i = 0; i < sub.Nodes.Count; i++)
                {
                    PathNode node = sub.Nodes[i];
                    (node.In, node.Out) = (node.Out, node.In);
                    (node.HasIn, node.HasOut) = (node.HasOut, node.HasIn);
                    sub.Nodes[i] = node;
                }
            }

            path.InvalidateGeometry();
        }

        _toolHint = "Path direction reversed";
        Notify();
    }

    /// <summary>Simplifies selected paths by resampling flattened geometry.</summary>
    public void SimplifySelectedPaths(float tolerance = 1.5f)
    {
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        if (paths.Count == 0)
        {
            return;
        }

        PushHistory("Simplify");
        foreach (PathObject path in paths)
        {
            VPath simplified = new() { FillRule = path.Path.FillRule };
            foreach (FlatContour contour in path.Path.Flatten(Matrix3x2.Identity, tolerance))
            {
                List<Vec2> points = DouglasPeucker(contour.Points, tolerance);
                if (points.Count < 2)
                {
                    continue;
                }

                VSubPath sub = simplified.Add(new VSubPath(contour.Closed));
                foreach (Vec2 point in points)
                {
                    sub.Add(PathNode.Corner(point));
                }
            }

            path.BakeShape();
            path.Path = simplified;
            path.InvalidateGeometry();
        }

        _toolHint = "Paths simplified";
        Notify();
    }

    private static List<Vec2> DouglasPeucker(Vec2[] points, float tolerance)
    {
        if (points.Length < 3)
        {
            return [.. points];
        }

        bool[] keep = new bool[points.Length];
        keep[0] = true;
        keep[^1] = true;
        SimplifySegment(points, 0, points.Length - 1, tolerance, keep);
        List<Vec2> result = new(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    private static void SimplifySegment(Vec2[] points, int first, int last, float tolerance, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        float maxDistance = 0f;
        int index = -1;
        for (int i = first + 1; i < last; i++)
        {
            float distance = HitTest.DistanceToSegment(points[i], points[first], points[last]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                index = i;
            }
        }

        if (index >= 0 && maxDistance > tolerance)
        {
            keep[index] = true;
            SimplifySegment(points, first, index, tolerance, keep);
            SimplifySegment(points, index, last, tolerance, keep);
        }
    }

    /// <summary>Removes every guide.</summary>
    public void ClearGuides()
    {
        if (Document.Guides.Count == 0)
        {
            return;
        }

        PushHistory("Clear Guides");
        Document.Guides.Clear();
        _toolHint = "Guides cleared";
        Notify();
    }
}
