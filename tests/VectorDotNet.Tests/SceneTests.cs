using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SceneObjectTests
{
    [Fact]
    public void GroupBoundsAndDocumentTransformsFollowTheTree()
    {
        GroupObject root = new() { Name = "Root", Transform = Mat.Translate(100, 50) };
        GroupObject group = new() { Name = "Group", Transform = Mat.Translate(10, 20) };
        PathObject path = new()
        {
            Name = "Path",
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 20, Height = 10 },
            Transform = Mat.Translate(5, 5),
        };
        path.Rebuild();
        group.Add(path);
        root.Add(group);

        Assert.Same(group, path.Parent);
        Assert.Same(root, group.Parent);
        Assert.True(group.IsContainer);
        Assert.False(path.IsContainer);
        Assert.Equal(new Bounds(-5, 0, 20, 10), group.LocalBounds);

        Bounds document = path.DocumentBounds;
        V.Nearly(105f, document.Left);
        V.Nearly(70f, document.Top);
    }

    [Fact]
    public void ClonePreservesOrRenewsIdentity()
    {
        GroupObject root = new() { Name = "Root" };
        PathObject path = new() { Name = "Path" };
        path.Transform = Mat.Translate(3, 4);
        root.Add(path);

        GroupObject same = (GroupObject)root.Clone(keepId: true);
        GroupObject fresh = (GroupObject)root.Clone(keepId: false);

        Assert.Equal(root.Id, same.Id);
        Assert.Equal(path.Id, same.Children[0].Id);
        Assert.NotEqual(root.Id, fresh.Id);
        Assert.NotEqual(path.Id, fresh.Children[0].Id);
        Assert.Equal(new Vec2(3, 4), ((Matrix3x2)fresh.Children[0].Transform).Apply(Vec2.Zero));
    }

    [Fact]
    public void VisibilityAndLockPropagateThroughAncestors()
    {
        GroupObject layer = new LayerObject();
        GroupObject group = new();
        PathObject path = new();
        layer.Add(group);
        group.Add(path);

        Assert.True(path.IsEffectivelyVisible);
        Assert.False(path.IsEffectivelyLocked);

        group.Visible = false;
        Assert.False(path.IsEffectivelyVisible);

        group.Visible = true;
        layer.Locked = true;
        Assert.True(path.IsEffectivelyLocked);
        Assert.True(layer.IsLayer);
    }

    [Fact]
    public void SelfAndDescendantsIsDepthFirst()
    {
        GroupObject root = new() { Name = "root" };
        GroupObject a = new() { Name = "a" };
        GroupObject b = new() { Name = "b" };
        PathObject leaf = new() { Name = "leaf" };
        root.Add(a);
        a.Add(b);
        b.Add(leaf);

        Assert.Equal(["root", "a", "b", "leaf"], root.SelfAndDescendants().Select(item => item.Name));
    }

    [Fact]
    public void PathObjectCachesBoundsUntilInvalidated()
    {
        PathObject path = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
        };
        path.Rebuild();

        Bounds first = path.LocalBounds;
        path.Path.SubPaths[0].Nodes.Clear();
        Assert.Equal(first, path.LocalBounds);

        path.InvalidateGeometry();
        Assert.True(path.LocalBounds.IsEmpty);
    }

    [Fact]
    public void ShapeCanBeBakedAndRestoredByParameters()
    {
        PathObject path = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Ellipse, Width = 20, Height = 10 },
        };
        path.Rebuild();

        Assert.True(path.IsShape);
        Assert.Equal("Ellipse", path.DisplayName);

        path.BakeShape();
        Assert.False(path.IsShape);
        Assert.Equal("Path", path.DisplayName);

        path.Shape = new ShapeSpec { Kind = ShapeKind.Star, Width = 10, Height = 10, Points = 5 };
        path.Rebuild();
        Assert.Equal(10, path.Path.SubPaths[0].Nodes.Count);
    }

    [Fact]
    public void PathCloneCopiesFacesAndGeometry()
    {
        PathObject path = new()
        {
            Name = "Named",
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
            Fill = Paint.Solid(0xFF112233),
            Stroke = new StrokeStyle { Paint = Paint.Solid(0xFF445566), Width = 3 },
            Opacity = 0.5f,
        };
        path.Rebuild();

        PathObject clone = (PathObject)path.Clone(keepId: false);
        clone.Fill!.Color = 0xFFFFFFFF;

        Assert.Equal(0xFF112233u, path.Fill!.Color);
        Assert.Equal(0xFF445566u, clone.Stroke!.Paint.Color);
        Assert.Equal(0.5f, clone.Opacity);
        Assert.Equal(path.Path.SubPaths[0].Nodes.Count, clone.Path.SubPaths[0].Nodes.Count);
    }

    [Fact]
    public void TextObjectMeasuresLinesAndClones()
    {
        TextObject text = new() { Text = "Hello\nWorld!", FontSize = 20f, LineHeight = 1.5f };

        Assert.True(text.MeasuredWidth > 50f);
        Assert.Equal(60f, text.LocalBounds.Height);
        Assert.Equal("Hello", text.DisplayName);

        TextObject clone = (TextObject)text.Clone(keepId: false);
        clone.Text = "Other";

        Assert.Equal("Hello\nWorld!", text.Text);
        Assert.Equal(20f, clone.FontSize);

        text.Text = "";
        Assert.Equal("Text", text.DisplayName);
    }
}

public sealed class DocumentTests
{
    [Fact]
    public void NewDocumentHasOneLayerAndArtboard()
    {
        Document document = new(640, 480);

        Assert.Single(document.Artboards);
        Assert.Single(document.Layers);
        Assert.NotNull(document.ActiveLayer);
        Assert.Equal(640f, document.Active.Width);
        Assert.Equal(480f, document.Active.Height);
        Assert.Equal("Untitled-1", document.Name);
    }

    [Fact]
    public void FindLocatesObjectsAnywhereInTheTree()
    {
        Document document = new();
        GroupObject group = new() { Name = "Group" };
        PathObject path = new() { Name = "Path" };
        group.Add(path);
        document.Root.Add(group);

        Assert.Same(path, document.Find(path.Id));
        Assert.Null(document.Find(Guid.NewGuid()));
    }

    [Fact]
    public void CloneDeepCopiesLayersArtboardsGuidesAndActiveLayer()
    {
        Document document = new(100, 100);
        document.Name = "Doc";
        document.Active.Background = 0xFF112233;
        document.Guides.Add(new Guide { Horizontal = true, Position = 20 });
        document.ActiveLayer!.Add(new PathObject { Name = "Path" });
        document.ActiveLayer.Locked = true;

        Document clone = document.Clone(keepIds: false);

        Assert.Equal("Doc", clone.Name);
        Assert.Single(clone.Artboards);
        Assert.Equal(0xFF112233u, clone.Active.Background);
        Assert.Single(clone.Guides);
        Assert.Same(clone.Layers.First(), clone.ActiveLayer);
        Assert.NotSame(document.ActiveLayer, clone.ActiveLayer);
        Assert.NotEqual(document.Root.Id, clone.Root.Id);
        Assert.True(clone.ActiveLayer!.Locked);
    }

    [Fact]
    public void SelectionBoundsUnionsDocumentBounds()
    {
        Document document = new();
        PathObject a = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
        };
        a.Rebuild();
        a.Transform = Mat.Translate(5, 5);
        PathObject b = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
        };
        b.Rebuild();
        b.Transform = Mat.Translate(40, 30);
        document.ActiveLayer!.Add(a);
        document.ActiveLayer.Add(b);

        Bounds bounds = Document.SelectionBounds([a, b]);

        V.Nearly(0f, bounds.Left);
        V.Nearly(45f, bounds.Right);
        V.Nearly(35f, bounds.Bottom);
    }

    [Fact]
    public void ArtboardCloneIsIndependent()
    {
        Artboard artboard = new() { Name = "Page", X = 5, Y = 6, Width = 10, Height = 20, Background = 0xFF010203 };
        Artboard clone = artboard.Clone();
        clone.Name = "Other";

        Assert.Equal("Page", artboard.Name);
        Assert.Equal(5f, clone.X);
        Assert.Equal(0xFF010203u, clone.Background);
    }
}
