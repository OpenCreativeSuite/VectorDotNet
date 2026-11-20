using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SessionInputTests
{
    [Fact]
    public void SelectClickAndDragMovesTheObject()
    {
        EditorSession session = V.InputSession();
        PathObject path = V.AddRect(session, 100, 100, 100, 50);

        V.Click(session, new Vec2(150, 125));
        Assert.Single(session.Selection);
        Assert.Same(path, session.Selection[0]);

        V.Drag(session, new Vec2(150, 125), new Vec2(170, 135));

        V.Nearly(120f, path.DocumentBounds.Left);
        V.Nearly(110f, path.DocumentBounds.Top);
    }

    [Fact]
    public void ShiftClickTogglesTheSelection()
    {
        EditorSession session = V.InputSession();
        PathObject a = V.AddRect(session, 0, 0, 50, 50);
        PathObject b = V.AddRect(session, 100, 0, 50, 50);

        V.Click(session, new Vec2(25, 25));
        V.Click(session, new Vec2(125, 25), shift: true);

        Assert.Equal(2, session.Selection.Count);
        Assert.Contains(a, session.Selection);
        Assert.Contains(b, session.Selection);

        V.Click(session, new Vec2(125, 25), shift: true);
        Assert.Single(session.Selection);
    }

    [Fact]
    public void DraggingEmptyCanvasMarqueesTheObjectsInside()
    {
        EditorSession session = V.InputSession();
        PathObject inside = V.AddRect(session, 100, 100, 50, 50);
        PathObject outside = V.AddRect(session, 400, 400, 50, 50);

        V.Drag(session, new Vec2(50, 50), new Vec2(200, 200));

        Assert.Single(session.Selection);
        Assert.Same(inside, session.Selection[0]);
        Assert.DoesNotContain(outside, session.Selection);
    }

    [Fact]
    public void LassoSelectsObjectsTouchedByTheLoop()
    {
        EditorSession session = V.InputSession();
        PathObject target = V.AddRect(session, 100, 100, 50, 50);
        session.SetTool(ToolKind.Lasso);
        Assert.Single(HitTest.InLasso(
            session.Document,
            [new Vec2(50, 50), new Vec2(220, 50), new Vec2(220, 220), new Vec2(50, 220)]));

        session.PointerDown(session.DocumentToScreen(new Vec2(50, 50)), false, false, false);
        session.PointerMove(session.DocumentToScreen(new Vec2(220, 50)));
        session.PointerMove(session.DocumentToScreen(new Vec2(220, 220)));
        session.PointerMove(session.DocumentToScreen(new Vec2(50, 220)));
        session.PointerUp(session.DocumentToScreen(new Vec2(50, 50)));

        Assert.Single(session.Selection);
        Assert.Same(target, session.Selection[0]);
        Assert.Empty(session.LassoPoints);
    }

    [Fact]
    public void ScaleHandleDragResizesTheSelection()
    {
        EditorSession session = V.InputSession();
        PathObject path = V.AddRect(session, 100, 100, 100, 50);
        session.Select(path);
        Bounds before = session.SelectionBounds;
        Vec2[] handles = session.ScaleHandlesInScreen();
        Vec2 bottomRight = handles[4];

        session.PointerDown(bottomRight, false, false, false);
        session.PointerMove(bottomRight + new Vec2(50, 30));
        session.PointerUp(bottomRight + new Vec2(50, 30));

        Assert.True(session.SelectionBounds.Width > before.Width);
        Assert.True(session.SelectionBounds.Height > before.Height);
    }

    [Fact]
    public void MarqueeOverlayIsExposedWhileDragging()
    {
        EditorSession session = V.InputSession();
        Vec2 start = session.DocumentToScreen(new Vec2(20, 20));
        Vec2 end = session.DocumentToScreen(new Vec2(120, 80));

        session.PointerDown(start, false, false, false);
        session.PointerMove(end);

        Assert.Equal(DragKind.Marquee, session.Drag);
        Assert.NotNull(session.Marquee);
        Assert.Equal(100f, session.Marquee!.Value.Width, 1);
        Assert.Equal(60f, session.Marquee.Value.Height, 1);

        session.PointerUp(end);
        Assert.Null(session.Marquee);
    }

    [Theory]
    [InlineData(ToolKind.Rectangle, ShapeKind.Rectangle)]
    [InlineData(ToolKind.RoundedRectangle, ShapeKind.RoundedRectangle)]
    [InlineData(ToolKind.Ellipse, ShapeKind.Ellipse)]
    [InlineData(ToolKind.Polygon, ShapeKind.Polygon)]
    [InlineData(ToolKind.Star, ShapeKind.Star)]
    [InlineData(ToolKind.Line, ShapeKind.Line)]
    [InlineData(ToolKind.Spiral, ShapeKind.Spiral)]
    public void EveryShapeToolCreatesALiveShape(ToolKind tool, ShapeKind kind)
    {
        EditorSession session = V.InputSession();
        session.SetTool(tool);

        V.Drag(session, new Vec2(100, 100), new Vec2(220, 180));

        Assert.Single(session.Selection);
        PathObject created = Assert.IsType<PathObject>(session.Selection[0]);
        Assert.True(created.IsShape);
        Assert.Equal(kind, created.Shape!.Kind);
        Assert.True(created.Shape.Width > 0f);
    }

    [Fact]
    public void ShiftConstrainsShapesToSquaresAndAltDrawsFromTheCentre()
    {
        EditorSession squareSession = V.InputSession();
        squareSession.SetTool(ToolKind.Rectangle);
        InputControl.Set("Shift", true);
        try
        {
            V.Drag(squareSession, new Vec2(100, 100), new Vec2(200, 150), shift: true);
        }
        finally
        {
            InputControl.Set("Shift", false);
        }

        PathObject square = (PathObject)squareSession.Selection[0];
        Assert.Equal(square.Shape!.Width, square.Shape.Height, 3);

        EditorSession centreSession = V.InputSession();
        centreSession.SetTool(ToolKind.Ellipse);
        V.Drag(centreSession, new Vec2(200, 200), new Vec2(250, 230), alt: true);
        PathObject ellipse = (PathObject)centreSession.Selection[0];
        Assert.Equal(100f, ellipse.Shape!.Width, 3);
        Assert.Equal(60f, ellipse.Shape.Height, 3);
        V.Nearly(200f, ellipse.DocumentBounds.Center.X);
        V.Nearly(200f, ellipse.DocumentBounds.Center.Y);
    }

    [Fact]
    public void TinyShapesAreDiscarded()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Rectangle);

        V.Click(session, new Vec2(100, 100));

        Assert.Empty(session.ActiveLayer.Children);
        Assert.Empty(session.Selection);
        Assert.Contains("Drag to draw", session.Status);
    }

    [Fact]
    public void ShapeDefaultsFlowIntoNewObjects()
    {
        EditorSession session = V.InputSession();
        session.DefaultCornerRadius = 24f;
        session.DefaultPoints = 7;
        session.DefaultInnerRatio = 0.3f;
        session.FillColor = 0xFF112233;
        session.StrokeWidth = 4f;
        session.StrokeColor = 0xFF445566;

        session.SetTool(ToolKind.RoundedRectangle);
        V.Drag(session, new Vec2(10, 10), new Vec2(100, 100));
        PathObject rounded = (PathObject)session.Selection[0];
        Assert.Equal(24f, rounded.Shape!.CornerRadius);
        Assert.Equal(0xFF112233u, rounded.Fill!.Color);

        session.SetTool(ToolKind.Star);
        V.Drag(session, new Vec2(200, 200), new Vec2(300, 300));
        PathObject star = (PathObject)session.Selection[0];
        Assert.Equal(7, star.Shape!.Points);
        Assert.Equal(0.3f, star.Shape.InnerRatio);

        session.SetTool(ToolKind.Line);
        V.Drag(session, new Vec2(10, 300), new Vec2(110, 320));
        PathObject line = (PathObject)session.Selection[0];
        Assert.Null(line.Fill);
        Assert.NotNull(line.Stroke);
        Assert.Equal(session.StrokeColor, line.Stroke!.Paint.Color);
        Assert.Equal(4f, line.Stroke.Width);
    }

    [Fact]
    public void TextToolPlacesEditableText()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Text);
        session.DefaultFontFamily = "Arial";

        V.Click(session, new Vec2(100, 120));

        TextObject text = Assert.IsType<TextObject>(session.Selection[0]);
        Assert.Equal("Text", text.Text);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal(48f, text.FontSize);
        Assert.Equal(0xFF4C8DFFu, text.Fill!.Color);
    }

    [Fact]
    public void PenClickDragAndFinishBuildsAClosedCurve()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Pen);
        Vec2 a = session.DocumentToScreen(new Vec2(100, 100));
        Vec2 b = session.DocumentToScreen(new Vec2(200, 100));

        session.PointerDown(a, false, false, false);
        session.PointerMove(a + new Vec2(30, 30));
        session.PointerUp(a + new Vec2(30, 30));
        Assert.True(session.IsPenActive);
        Assert.True(session.PenNodes[^1].HasOut);

        session.PointerDown(b, false, false, false);
        session.PointerUp(b);
        session.FinishPen(closed: true);

        Assert.False(session.IsPenActive);
        PathObject path = Assert.IsType<PathObject>(session.Selection[0]);
        Assert.True(path.Path.SubPaths[0].Closed);
        Assert.True(path.Fill is not null);
        Assert.True(path.Stroke is not null);
        Assert.Single(session.ActiveLayer.Children);
    }

    [Fact]
    public void PenClickingTheFirstNodeClosesThePath()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Pen);
        Vec2 a = session.DocumentToScreen(new Vec2(100, 100));
        Vec2 b = session.DocumentToScreen(new Vec2(200, 100));
        Vec2 c = session.DocumentToScreen(new Vec2(150, 180));

        session.PointerDown(a, false, false, false);
        session.PointerUp(a);
        session.PointerDown(b, false, false, false);
        session.PointerUp(b);
        session.PointerDown(c, false, false, false);
        session.PointerUp(c);
        session.PointerDown(a, false, false, false);

        Assert.False(session.IsPenActive);
        Assert.True(((PathObject)session.Selection[0]).Path.SubPaths[0].Closed);
    }

    [Fact]
    public void PencilCollectsPointsAndCreatesASmoothPath()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Pencil);
        Vec2 start = session.DocumentToScreen(new Vec2(100, 100));

        session.PointerDown(start, false, false, false);
        for (int i = 1; i <= 8; i++)
        {
            session.PointerMove(start + new Vec2(i * 12, (i % 2) * 10));
        }

        session.PointerUp(start + new Vec2(96, 0));

        Assert.Equal(DragKind.None, session.Drag);
        Assert.Single(session.ActiveLayer.Children);
        PathObject path = Assert.IsType<PathObject>(session.Selection[0]);
        Assert.Equal("Freehand", path.Name);
        Assert.False(path.IsShape);
        Assert.True(path.Path.SubPaths[0].Nodes.Count >= 2);
        Assert.NotNull(path.Stroke);
    }

    [Fact]
    public void DirectSelectionPicksAndMovesNodes()
    {
        EditorSession session = V.InputSession();
        PathObject line = new()
        {
            Name = "Line",
            Path = VPath.Line(new Vec2(100, 100), new Vec2(300, 100)),
            Stroke = new StrokeStyle { Paint = Paint.Solid(0xFF000000), Width = 4f },
        };
        session.ActiveLayer.Add(line);
        session.SetTool(ToolKind.Direct);

        V.Click(session, new Vec2(100, 100));
        Assert.Same(line, session.EditTarget);
        Assert.NotNull(session.SelectedNode);

        V.Drag(session, new Vec2(100, 100), new Vec2(120, 130));
        PathNode moved = line.Path.SubPaths[0].Nodes[0];
        V.Nearly(120f, moved.Point.X);
        V.Nearly(130f, moved.Point.Y);
        Assert.False(line.IsShape);
    }

    [Fact]
    public void DeletingASelectedNodeRemovesItAndCanRemoveTheObject()
    {
        EditorSession session = V.InputSession();
        VPath linePath = new();
        VSubPath contour = linePath.Add(new VSubPath());
        contour.Add(PathNode.Corner(new Vec2(100, 100)));
        contour.Add(PathNode.Corner(new Vec2(200, 100)));
        contour.Add(PathNode.Corner(new Vec2(300, 100)));
        PathObject path = new()
        {
            Name = "Path",
            Path = linePath,
            Stroke = new StrokeStyle { Paint = Paint.Solid(0xFF000000), Width = 4f },
        };
        session.ActiveLayer.Add(path);
        session.SetTool(ToolKind.Direct);
        V.Click(session, new Vec2(100, 100));

        session.DeleteSelectedNode();
        Assert.Equal(2, path.Path.SubPaths[0].Nodes.Count);

        V.Click(session, new Vec2(300, 100));
        session.DeleteSelectedNode();
        Assert.Empty(path.Path.SubPaths);
        Assert.Empty(session.ActiveLayer.Children);
        Assert.Null(session.EditTarget);
    }

    [Fact]
    public void ZoomToolAndAltClickStepTheZoom()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Zoom);
        float zoom = session.Zoom;
        Vec2 point = session.DocumentToScreen(new Vec2(100, 100));

        session.PointerDown(point, false, false, false);
        Assert.True(session.Zoom > zoom);

        float zoomed = session.Zoom;
        session.PointerDown(point, true, true, false);
        Assert.True(session.Zoom < zoomed);
    }

    [Fact]
    public void HandAndMiddleButtonPanTheView()
    {
        EditorSession hand = V.InputSession();
        hand.SetTool(ToolKind.Hand);
        float panX = hand.PanX;
        Vec2 start = hand.DocumentToScreen(new Vec2(100, 100));
        hand.PointerDown(start, false, false, false);
        hand.PointerMove(start + new Vec2(20, 10));
        hand.PointerUp(start + new Vec2(20, 10));
        V.Nearly(panX + 20f, hand.PanX);

        EditorSession middle = V.InputSession();
        float middleX = middle.PanX;
        Vec2 origin = new(300, 300);
        middle.PointerDown(origin, false, false, middle: true);
        Assert.Equal(DragKind.Pan, middle.Drag);
        middle.PointerMove(origin + new Vec2(-15, 5));
        middle.PointerUp(origin + new Vec2(-15, 5));
        V.Nearly(middleX - 15f, middle.PanX);
    }

    [Fact]
    public void RulersCreateGuidesAndGuidesCanBeCleared()
    {
        EditorSession session = new();
        session.SetViewSize(800, 600);
        session.ActualPixels();

        session.PointerDown(new Vec2(200, 10), false, false, false);
        session.PointerDown(new Vec2(10, 200), false, false, false);

        Assert.Equal(2, session.Document.Guides.Count);
        Assert.Contains(session.Document.Guides, guide => guide.Horizontal);
        Assert.Contains(session.Document.Guides, guide => !guide.Horizontal);
        Assert.Contains("Guide added", session.Status);

        session.ClearGuides();
        Assert.Empty(session.Document.Guides);
    }

    [Fact]
    public void GridSnappingRoundsTheMoveDelta()
    {
        EditorSession session = V.InputSession();
        session.SnapToGrid = true;
        session.GridSize = 20f;
        PathObject path = V.AddRect(session, 0, 0, 100, 100);
        session.Select(path);

        Vec2 start = session.DocumentToScreen(new Vec2(50, 50));
        session.PointerDown(start, false, false, false);
        Assert.Equal(DragKind.Move, session.Drag);
        session.PointerMove(start + new Vec2(13, 7));
        session.PointerUp(start + new Vec2(13, 7));

        V.Nearly(20f, path.DocumentBounds.Left);
        V.Nearly(0f, path.DocumentBounds.Top);
    }

    [Fact]
    public void PointerLeaveClearsTheHoverFlag()
    {
        EditorSession session = V.InputSession();
        session.PointerMove(session.DocumentToScreen(new Vec2(100, 100)));
        Assert.True(session.CursorOnCanvas);

        session.PointerLeave();
        Assert.False(session.CursorOnCanvas);
    }

    [Fact]
    public void LockedAndHiddenObjectsCannotBeSelected()
    {
        EditorSession session = V.InputSession();
        PathObject path = V.AddRect(session, 100, 100, 50, 50);
        path.Locked = true;

        V.Click(session, new Vec2(125, 125));
        Assert.Empty(session.Selection);

        path.Locked = false;
        path.Visible = false;
        V.Click(session, new Vec2(125, 125));
        Assert.Empty(session.Selection);
    }
}
