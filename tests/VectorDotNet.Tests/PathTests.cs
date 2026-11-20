using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class VPathTests
{
    [Fact]
    public void RectangleBuildsFourClosedCorners()
    {
        VPath path = VPath.Rectangle(new Bounds(10, 20, 30, 40));

        Assert.Single(path.SubPaths);
        VSubPath sub = path.SubPaths[0];
        Assert.True(sub.Closed);
        Assert.Equal(4, sub.Nodes.Count);
        Assert.Equal(new Vec2(10, 20), sub.Nodes[0].Point);
        Assert.Equal(new Vec2(40, 60), sub.Nodes[2].Point);

        Bounds bounds = path.GetBounds();
        V.Nearly(10f, bounds.Left);
        V.Nearly(20f, bounds.Top);
        V.Nearly(30f, bounds.Width);
        V.Nearly(40f, bounds.Height);
    }

    [Fact]
    public void RoundedRectangleClampsTheRadiusAndKeepsEightAnchors()
    {
        VSubPath rounded = VPath.RoundedRectangle(new Bounds(0, 0, 100, 50), 999f).SubPaths[0];
        Assert.Equal(8, rounded.Nodes.Count);
        Assert.Contains(rounded.Nodes, node => node.HasIn || node.HasOut);

        VSubPath square = VPath.RoundedRectangle(new Bounds(0, 0, 100, 50), 0f).SubPaths[0];
        Assert.Equal(4, square.Nodes.Count);
    }

    [Fact]
    public void EllipseUsesFourSmoothNodes()
    {
        VPath path = VPath.Ellipse(new Bounds(0, 0, 100, 60));
        VSubPath sub = path.SubPaths[0];

        Assert.Equal(4, sub.Nodes.Count);
        Assert.All(sub.Nodes, node => Assert.True(node.HasIn && node.HasOut));
        Assert.Equal(new Vec2(50, 0), sub.Nodes[0].Point);
        Assert.Equal(new Vec2(100, 30), sub.Nodes[1].Point);
    }

    [Fact]
    public void PolygonAndStarClampCountsAndProduceExpectedNodes()
    {
        Assert.Equal(5, VPath.Polygon(Vec2.Zero, 50, 50, 5, 0f).SubPaths[0].Nodes.Count);
        Assert.Equal(3, VPath.Polygon(Vec2.Zero, 50, 50, 1, 0f).SubPaths[0].Nodes.Count);
        Assert.Equal(64, VPath.Polygon(Vec2.Zero, 50, 50, 999, 0f).SubPaths[0].Nodes.Count);
        Assert.Equal(10, VPath.Star(Vec2.Zero, 50, 50, 5, 0.5f, 0f).SubPaths[0].Nodes.Count);
        Assert.Equal(8, VPath.Star(Vec2.Zero, 50, 50, 4, 9f, 0f).SubPaths[0].Nodes.Count);
    }

    [Fact]
    public void SpiralAndLineBuildOpenContours()
    {
        VPath spiral = VPath.Spiral(Vec2.Zero, 50f, 2f, 5f);
        Assert.Single(spiral.SubPaths);
        Assert.False(spiral.SubPaths[0].Closed);
        Assert.True(spiral.SubPaths[0].Nodes.Count >= 24);

        VPath line = VPath.Line(new Vec2(1, 2), new Vec2(8, 9));
        Assert.Equal(2, line.SubPaths[0].Nodes.Count);
        Assert.False(line.SubPaths[0].Closed);
    }

    [Fact]
    public void FlattenSubdividesCurvesUntilTheyAreFlat()
    {
        VPath curve = new();
        VSubPath sub = curve.Add(new VSubPath());
        sub.Add(PathNode.Smooth(new Vec2(0, 0), new Vec2(0, 0), new Vec2(30, 30)));
        sub.Add(PathNode.Smooth(new Vec2(100, 0), new Vec2(70, 30), new Vec2(100, 0)));

        List<FlatContour> flat = curve.Flatten(Matrix3x2.Identity, 0.1f);
        Assert.Single(flat);
        Assert.True(flat[0].Points.Length > 2);
        Assert.False(flat[0].Closed);
    }

    [Fact]
    public void TransformAndCloneKeepGeometryIndependent()
    {
        VPath path = VPath.Rectangle(new Bounds(0, 0, 10, 10));
        VPath moved = path.Transformed(Mat.Translate(5, 7));

        Assert.Equal(new Vec2(5, 7), moved.SubPaths[0].Nodes[0].Point);
        Assert.Equal(new Vec2(0, 0), path.SubPaths[0].Nodes[0].Point);

        VPath clone = path.Clone();
        clone.SubPaths[0].Nodes.Clear();
        Assert.Equal(4, path.SubPaths[0].Nodes.Count);
        Assert.True(clone.IsEmpty);
    }

    [Fact]
    public void EmptyAndFillRuleAreObservable()
    {
        VPath path = new() { FillRule = FillRule.EvenOdd };

        Assert.True(path.IsEmpty);
        Assert.Equal(FillRule.EvenOdd, path.Clone().FillRule);

        path.Add(new VSubPath { Closed = true }).Add(PathNode.Corner(Vec2.Zero));
        Assert.False(path.IsEmpty);
    }

    [Fact]
    public void SmoothAlongBuildsMirroredHandes()
    {
        PathNode node = PathNode.SmoothAlong(new Vec2(10, 10), new Vec2(1, 0), 4f, 6f);

        Assert.True(node.HasIn);
        Assert.True(node.HasOut);
        Assert.Equal(new Vec2(6, 10), node.In);
        Assert.Equal(new Vec2(16, 10), node.Out);
    }
}

public sealed class ShapeSpecTests
{
    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.RoundedRectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Polygon)]
    [InlineData(ShapeKind.Star)]
    [InlineData(ShapeKind.Line)]
    [InlineData(ShapeKind.Spiral)]
    public void EveryShapeBuildsGeometry(ShapeKind kind)
    {
        ShapeSpec spec = new()
        {
            Kind = kind,
            Width = 100f,
            Height = 60f,
            Points = 6,
            InnerRatio = 0.4f,
            CornerRadius = 8f,
            Turns = 2f,
        };

        VPath path = spec.Build();

        Assert.False(path.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(spec.DisplayName));
    }

    [Fact]
    public void ShapeKindsUseTheirExpectedNodeCounts()
    {
        Assert.Equal(4, new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 }.Build().SubPaths[0].Nodes.Count);
        Assert.Equal(8, new ShapeSpec { Kind = ShapeKind.RoundedRectangle, Width = 10, Height = 10, CornerRadius = 2 }.Build().SubPaths[0].Nodes.Count);
        Assert.Equal(4, new ShapeSpec { Kind = ShapeKind.Ellipse, Width = 10, Height = 10 }.Build().SubPaths[0].Nodes.Count);
        Assert.Equal(5, new ShapeSpec { Kind = ShapeKind.Polygon, Width = 10, Height = 10, Points = 5 }.Build().SubPaths[0].Nodes.Count);
        Assert.Equal(12, new ShapeSpec { Kind = ShapeKind.Star, Width = 10, Height = 10, Points = 6 }.Build().SubPaths[0].Nodes.Count);
        Assert.Equal(2, new ShapeSpec { Kind = ShapeKind.Line, Width = 10, Height = 0 }.Build().SubPaths[0].Nodes.Count);
        Assert.True(new ShapeSpec { Kind = ShapeKind.Spiral, Width = 10, Height = 10, Turns = 1 }.Build().SubPaths[0].Nodes.Count > 10);
    }

    [Fact]
    public void CloningCopiesParameters()
    {
        ShapeSpec spec = new()
        {
            Kind = ShapeKind.Star,
            Width = 11,
            Height = 12,
            Points = 7,
            InnerRatio = 0.3f,
            Rotation = 15,
            Turns = 4,
        };

        ShapeSpec clone = spec.Clone();
        clone.Width = 99;

        Assert.Equal(11f, spec.Width);
        Assert.Equal(ShapeKind.Star, clone.Kind);
        Assert.Equal(7, clone.Points);
        Assert.Equal(0.3f, clone.InnerRatio);
    }
}

public sealed class PaintTests
{
    [Fact]
    public void PaintVisibilityTracksKindAndAlpha()
    {
        Assert.True(Paint.Solid(0xFFFFFFFF).IsVisible);
        Assert.False(Paint.Solid(0x00FFFFFF).IsVisible);
        Assert.False(Paint.None().IsVisible);
        Assert.False(new Paint { Kind = PaintKind.None, Color = 0xFFFFFFFF }.IsVisible);
    }

    [Fact]
    public void StrokeVisibilityAndCloneAreIndependent()
    {
        StrokeStyle stroke = new()
        {
            Paint = Paint.Solid(0xFF112233),
            Width = 4f,
            Dash = [2f, 3f],
        };

        Assert.True(stroke.IsVisible);
        StrokeStyle clone = stroke.Clone();
        clone.Dash[0] = 99f;

        Assert.Equal(2f, stroke.Dash[0]);
        Assert.Equal(4f, clone.Width);
        Assert.Equal(0xFF112233u, clone.Paint.Color);

        stroke.Width = 0f;
        Assert.False(stroke.IsVisible);
    }
}
