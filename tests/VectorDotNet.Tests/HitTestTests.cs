using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class HitTestTests
{
    [Fact]
    public void FilledAndStrokedPathsAreHit()
    {
        GroupObject layer = new LayerObject();
        PathObject filled = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 100, Height = 100 },
            Fill = Paint.Solid(0xFFFF0000),
        };
        filled.Rebuild();
        filled.Transform = Mat.Translate(50, 50);
        layer.Add(filled);

        Assert.True(HitTest.Contains(filled, new Vec2(50, 50), 0f));
        Assert.False(HitTest.Contains(filled, new Vec2(120, 120), 0f));

        PathObject outlined = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 100, Height = 100 },
            Fill = Paint.None(),
            Stroke = new StrokeStyle { Paint = Paint.Solid(0xFF00FF00), Width = 4f },
        };
        outlined.Rebuild();
        outlined.Transform = Mat.Translate(200, 50);
        layer.Add(outlined);

        Assert.True(HitTest.Contains(outlined, new Vec2(200, 0), 1f));
        Assert.False(HitTest.Contains(outlined, new Vec2(200, 50), 0.5f));
    }

    [Fact]
    public void AShapeWithNoFacesIsStillSelectableByItsBox()
    {
        PathObject empty = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
            Fill = Paint.None(),
        };
        empty.Rebuild();

        Assert.True(HitTest.Contains(empty, Vec2.Zero, 0f));
        Assert.False(HitTest.Contains(empty, new Vec2(20, 20), 0f));
    }

    [Fact]
    public void TopmostObjectIgnoresHiddenLockedAndPicksPaintOrder()
    {
        Document document = new();
        PathObject bottom = new()
        {
            Name = "bottom",
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 50, Height = 50 },
            Fill = Paint.Solid(0xFFFF0000),
        };
        bottom.Rebuild();
        PathObject top = new()
        {
            Name = "top",
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 50, Height = 50 },
            Fill = Paint.Solid(0xFF00FF00),
        };
        top.Rebuild();
        document.ActiveLayer!.Add(bottom);
        document.ActiveLayer.Add(top);

        Assert.Same(top, HitTest.TopmostObject(document, Vec2.Zero, 0f));
        Assert.Same(top, HitTest.TopmostPath(document, Vec2.Zero, 0f));

        top.Visible = false;
        Assert.Same(bottom, HitTest.TopmostObject(document, Vec2.Zero, 0f));

        top.Visible = true;
        top.Locked = true;
        Assert.Same(bottom, HitTest.TopmostObject(document, Vec2.Zero, 0f));
    }

    [Fact]
    public void TextObjectsAreHitByTheirLocalBounds()
    {
        Document document = new();
        TextObject text = new()
        {
            Text = "Hello",
            FontSize = 20,
            Transform = Mat.Translate(100, 100),
        };
        document.ActiveLayer!.Add(text);

        Assert.Same(text, HitTest.TopmostObject(document, new Vec2(105, 105), 0f));
        Assert.Null(HitTest.TopmostObject(document, new Vec2(500, 500), 0f));
        Assert.Null(HitTest.TopmostPath(document, new Vec2(105, 105), 0f));
    }

    [Fact]
    public void ResolveSelectionWalksOutOfNonLayerGroups()
    {
        Document document = new();
        GroupObject group = new() { Name = "Group" };
        PathObject path = new();
        group.Add(path);
        document.ActiveLayer!.Add(group);

        Assert.Same(group, HitTest.ResolveSelection(path));

        group.Parent = null;
        path.Parent = null;
        Assert.Same(path, HitTest.ResolveSelection(path));
    }

    [Fact]
    public void RectAndLassoQueriesReturnTouchedPaths()
    {
        Document document = new();
        PathObject a = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
            Fill = Paint.Solid(0xFFFF0000),
        };
        a.Rebuild();
        a.Transform = Mat.Translate(5, 5);
        PathObject b = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
            Fill = Paint.Solid(0xFF00FF00),
        };
        b.Rebuild();
        b.Transform = Mat.Translate(100, 100);
        document.ActiveLayer!.Add(a);
        document.ActiveLayer.Add(b);

        List<PathObject> rect = HitTest.InRect(document, new Bounds(0, 0, 20, 20));
        Assert.Single(rect);
        Assert.Same(a, rect[0]);

        List<PathObject> lasso = HitTest.InLasso(
            document,
            [new Vec2(-5, -5), new Vec2(25, -5), new Vec2(25, 25), new Vec2(-5, 25)]);
        Assert.Single(lasso);
        Assert.Same(a, lasso[0]);

        Assert.Empty(HitTest.InLasso(document, [Vec2.Zero, new Vec2(1, 1)]));
    }

    [Fact]
    public void PointInPolygonAndDistanceHelpersAreExact()
    {
        Vec2[] polygon = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

        Assert.True(HitTest.PointInPolygon(polygon, new Vec2(5, 5)));
        Assert.False(HitTest.PointInPolygon(polygon, new Vec2(20, 5)));
        Assert.Equal(5f, HitTest.DistanceToSegment(new Vec2(5, 5), new Vec2(0, 0), new Vec2(10, 0)), 4);
        Assert.Equal(0f, HitTest.DistanceToSegment(new Vec2(0, 5), new Vec2(0, 0), new Vec2(0, 10)), 4);
        Assert.Equal(5f, HitTest.DistanceToSegment(new Vec2(5, 5), new Vec2(0, 0), new Vec2(0, 10)), 4);
    }

    [Fact]
    public void FillRulesDistinguishOverlappingContours()
    {
        VPath path = new() { FillRule = FillRule.NonZero };
        VSubPath outer = path.Add(new VSubPath(closed: true));
        outer.Add(PathNode.Corner(new Vec2(0, 0)));
        outer.Add(PathNode.Corner(new Vec2(10, 0)));
        outer.Add(PathNode.Corner(new Vec2(10, 10)));
        outer.Add(PathNode.Corner(new Vec2(0, 10)));

        Assert.True(HitTest.PointInPath(path, new Vec2(5, 5)));

        VSubPath inner = path.Add(new VSubPath(closed: true));
        inner.Add(PathNode.Corner(new Vec2(4, 4)));
        inner.Add(PathNode.Corner(new Vec2(6, 4)));
        inner.Add(PathNode.Corner(new Vec2(6, 6)));
        inner.Add(PathNode.Corner(new Vec2(4, 6)));

        Assert.True(HitTest.PointInPath(path, new Vec2(5, 5)));

        path.FillRule = FillRule.EvenOdd;
        Assert.False(HitTest.PointInPath(path, new Vec2(5, 5)));
        Assert.True(HitTest.PointInPath(path, new Vec2(1, 1)));
    }
}

public sealed class RasterizerTests
{
    private static Bounds Clip(float width = 32f, float height = 32f) => new(0, 0, width, height);

    [Fact]
    public void SpanBufferRejectsInvisibleRuns()
    {
        SpanBuffer buffer = new();
        buffer.Add(0, 0, 0f, 0xFFFF0000);
        buffer.Add(0, 0, 10f, 0x00FF0000);
        buffer.Add(0, 0, 10f, 0xFFFF0000);

        Assert.Single(buffer.Spans);
        buffer.Clear();
        Assert.Empty(buffer.Spans);
    }

    [Fact]
    public void FilledRectangleCoversEveryPixelOnce()
    {
        SpanBuffer buffer = new();
        Rasterizer.FillPath(
            VPath.Rectangle(new Bounds(2, 3, 10, 8)),
            Matrix3x2.Identity,
            Clip(),
            0xFFFF0000,
            1f,
            buffer);

        Assert.NotEmpty(buffer.Spans);
        float area = buffer.Spans.Sum(span => span.Width);
        Assert.InRange(area, 79f, 81f);
        Assert.All(buffer.Spans, span => Assert.InRange(span.Y, 2, 10));
    }

    [Fact]
    public void ClippingAndOpacityLimitTheOutput()
    {
        SpanBuffer outside = new();
        Rasterizer.FillPath(
            VPath.Rectangle(new Bounds(40, 40, 10, 10)),
            Matrix3x2.Identity,
            Clip(),
            0xFFFF0000,
            1f,
            outside);
        Assert.Empty(outside.Spans);

        SpanBuffer transparent = new();
        Rasterizer.FillPath(
            VPath.Rectangle(new Bounds(0, 0, 10, 10)),
            Matrix3x2.Identity,
            Clip(),
            0xFFFF0000,
            0f,
            transparent);
        Assert.Empty(transparent.Spans);

        SpanBuffer half = new();
        Rasterizer.FillPath(
            VPath.Rectangle(new Bounds(0, 0, 10, 10)),
            Matrix3x2.Identity,
            Clip(),
            0xFFFF0000,
            0.5f,
            half);
        Assert.NotEmpty(half.Spans);
        Assert.All(half.Spans, span => Assert.InRange(Colour.A(span.Color), 120, 130));
    }

    [Fact]
    public void NonZeroAndEvenOddRulesFillOverlapsDifferently()
    {
        VPath path = new() { FillRule = FillRule.NonZero };
        VSubPath contour = path.Add(new VSubPath(closed: true));
        contour.Add(PathNode.Corner(new Vec2(0, 0)));
        contour.Add(PathNode.Corner(new Vec2(10, 0)));
        contour.Add(PathNode.Corner(new Vec2(10, 10)));
        contour.Add(PathNode.Corner(new Vec2(0, 10)));

        SpanBuffer nonzero = new();
        Rasterizer.FillPath(path, Matrix3x2.Identity, Clip(), 0xFFFFFFFF, 1f, nonzero);

        SpanBuffer evenOdd = new();
        Rasterizer.FillPath(path, Matrix3x2.Identity, Clip(), 0xFFFFFFFF, 1f, evenOdd, FillRule.EvenOdd);

        Assert.Equal(
            nonzero.Spans.Sum(span => span.Width),
            evenOdd.Spans.Sum(span => span.Width),
            3);
    }

    [Fact]
    public void StrokedLinesAndDashesEmitSpans()
    {
        VPath line = VPath.Line(new Vec2(2, 10), new Vec2(28, 10));

        SpanBuffer solid = new();
        Rasterizer.StrokePath(
            line,
            Matrix3x2.Identity,
            Clip(),
            new StrokeStyle { Paint = Paint.Solid(0xFF000000), Width = 4, Cap = LineCap.Round, Join = LineJoin.Round },
            1f,
            solid);

        SpanBuffer dashed = new();
        Rasterizer.StrokePath(
            line,
            Matrix3x2.Identity,
            Clip(),
            new StrokeStyle { Paint = Paint.Solid(0xFF000000), Width = 4, Dash = [2, 2] },
            1f,
            dashed);

        Assert.NotEmpty(solid.Spans);
        Assert.NotEmpty(dashed.Spans);
        Assert.True(dashed.Spans.Count > solid.Spans.Count);
    }

    [Fact]
    public void ZeroWidthStrokeAndEmptyPathAreSkipped()
    {
        SpanBuffer buffer = new();
        Rasterizer.StrokePath(
            VPath.Line(Vec2.Zero, new Vec2(10, 10)),
            Matrix3x2.Identity,
            Clip(),
            new StrokeStyle { Width = 0 },
            1f,
            buffer);

        Rasterizer.FillPath(new VPath(), Matrix3x2.Identity, Clip(), 0xFFFFFFFF, 1f, buffer);

        Assert.Empty(buffer.Spans);
    }
}

public sealed class SceneRendererTests
{
    [Fact]
    public void RenderPaintsTheArtboardAndVisibleObjects()
    {
        Document document = new(100, 60);
        document.Active.Background = 0xFFFFFFFF;
        PathObject rect = V.AddRectToDocument(document, 10, 10, 20, 20, 0xFFFF0000);
        SpanBuffer buffer = new();

        SceneRenderer.Render(
            document,
            Matrix3x2.Identity,
            new Bounds(0, 0, 120, 80),
            buffer);

        Assert.NotEmpty(buffer.Spans);
        Assert.Contains(buffer.Spans, span => span.Color == 0xFFFFFFFF);
        Assert.Contains(buffer.Spans, span => span.Color == 0xFFFF0000);
    }

    [Fact]
    public void HiddenZeroOpacityAndOutOfClipObjectsAreSkipped()
    {
        Document document = new(50, 50);
        document.Active.Background = 0xFF000000;
        PathObject hidden = V.AddRectToDocument(document, 0, 0, 10, 10, 0xFFFF0000);
        hidden.Visible = false;
        PathObject transparent = V.AddRectToDocument(document, 0, 0, 10, 10, 0xFF00FF00);
        transparent.Opacity = 0f;
        V.AddRectToDocument(document, 500, 500, 10, 10, 0xFF0000FF);
        SpanBuffer buffer = new();

        SceneRenderer.Render(document, Matrix3x2.Identity, new Bounds(0, 0, 60, 60), buffer);

        Assert.DoesNotContain(buffer.Spans, span => span.Color == 0xFFFF0000);
        Assert.DoesNotContain(buffer.Spans, span => span.Color == 0xFF00FF00);
        Assert.DoesNotContain(buffer.Spans, span => span.Color == 0xFF0000FF);
    }

    [Fact]
    public void ArtboardsCanBeSkippedAndViewTransformApplies()
    {
        Document document = new(10, 10);
        document.Active.Background = 0xFFFFFFFF;
        SpanBuffer noPages = new();
        SceneRenderer.Render(document, Matrix3x2.Identity, new Bounds(0, 0, 20, 20), noPages, drawArtboards: false);
        Assert.Empty(noPages.Spans);

        PathObject rect = V.AddRectToDocument(document, 0, 0, 5, 5, 0xFFFF0000);
        SpanBuffer scaled = new();
        SceneRenderer.Render(document, Mat.Scale(2, 2), new Bounds(0, 0, 40, 40), scaled);
        Assert.Contains(scaled.Spans, span => span.Color == 0xFFFF0000);
    }

    [Fact]
    public void TextCollectionHonoursAncestorOpacity()
    {
        Document document = new();
        GroupObject group = new() { Opacity = 0.5f };
        TextObject visible = new() { Text = "Visible" };
        TextObject hidden = new() { Text = "Hidden", Visible = false };
        group.Add(visible);
        group.Add(hidden);
        document.ActiveLayer!.Add(group);
        List<TextDrawItem> items = [];

        SceneRenderer.CollectText(document, items);

        Assert.Single(items);
        Assert.Same(visible, items[0].Text);
        Assert.Equal(0.5f, items[0].Opacity);
    }

    [Fact]
    public void DeviceMatrixIncludesAncestorsAndView()
    {
        Document document = new();
        GroupObject group = new() { Transform = Mat.Translate(10, 0) };
        PathObject path = new() { Transform = Mat.Translate(5, 0) };
        group.Add(path);
        document.ActiveLayer!.Add(group);

        Matrix3x2 matrix = SceneRenderer.DeviceMatrix(path, Mat.Translate(100, 0));

        Assert.Equal(new Vec2(115, 0), matrix.Apply(Vec2.Zero));
    }
}
