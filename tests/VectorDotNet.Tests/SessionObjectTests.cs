using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SessionObjectTests
{
    [Fact]
    public void CopyPasteDuplicateCutAndDeleteWorkInPlace()
    {
        EditorSession session = V.Session();
        PathObject original = V.AddRect(session, 10, 20, 30, 40);
        session.Select(original);

        session.Copy();
        session.Paste();

        Assert.Equal(2, session.ActiveLayer.Children.Count);
        Assert.Single(session.Selection);
        Assert.NotSame(original, session.Selection[0]);
        V.Nearly(34f, session.Selection[0].DocumentBounds.Left);

        session.DuplicateSelection();
        Assert.Equal(3, session.ActiveLayer.Children.Count);
        V.Nearly(52f, session.Selection[0].DocumentBounds.Left);

        session.Cut();
        Assert.Equal(2, session.ActiveLayer.Children.Count);
        Assert.Empty(session.Selection);

        session.Select(original);
        session.DeleteSelection();
        Assert.Single(session.ActiveLayer.Children);
    }

    [Fact]
    public void CopyingNothingReportsEmptyAndDeleteIsANoOp()
    {
        EditorSession session = V.Session();

        session.Copy();
        Assert.Equal("Nothing to copy", session.Status);

        session.DeleteSelection();
        Assert.Empty(session.Selection);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void GroupingKeepsPaintOrderAndUngroupingRestoresTransforms()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);
        session.GroupSelection();

        Assert.Single(session.Selection);
        GroupObject group = Assert.IsType<GroupObject>(session.Selection[0]);
        Assert.Equal(2, group.Children.Count);
        Assert.Same(group, a.Parent);
        Assert.Same(group, b.Parent);
        Assert.Single(session.ActiveLayer.Children);

        group.Transform = Mat.Translate(5, 5);
        session.UngroupSelection();

        Assert.Equal(2, session.Selection.Count);
        Assert.Equal(2, session.ActiveLayer.Children.Count);
        V.Nearly(5f, a.DocumentBounds.Left);
        V.Nearly(25f, b.DocumentBounds.Left);
    }

    [Fact]
    public void GroupingAcrossLayersIsRefused()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        session.AddLayer();
        PathObject b = V.AddRect(session, 0, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);

        session.GroupSelection();

        Assert.Contains("same layer", session.Status);
        Assert.Equal(2, session.Selection.Count);
    }

    [Fact]
    public void ArrangeMovesObjectsThroughThePaintOrder()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        PathObject c = V.AddRect(session, 40, 0, 10, 10);
        session.Select(a);

        session.Arrange(2);
        Assert.Equal([b, c, a], session.ActiveLayer.Children);
        session.Arrange(-2);
        Assert.Equal([a, b, c], session.ActiveLayer.Children);
        session.Arrange(1);
        Assert.Equal([b, a, c], session.ActiveLayer.Children);
        session.Arrange(-1);
        Assert.Equal([a, b, c], session.ActiveLayer.Children);
    }

    [Fact]
    public void AlignmentWorksAgainstSelectionAndArtboard()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 10, 10, 10, 10);
        PathObject b = V.AddRect(session, 50, 30, 10, 10);
        session.Select(a);
        session.Toggle(b);

        session.Align(EditorSession.AlignMode.Left);
        V.Nearly(10f, a.DocumentBounds.Left);
        V.Nearly(10f, b.DocumentBounds.Left);

        session.Align(EditorSession.AlignMode.Right, toArtboard: true);
        V.Nearly(session.Document.Active.Bounds.Right, a.DocumentBounds.Right);
        V.Nearly(session.Document.Active.Bounds.Right, b.DocumentBounds.Right);

        session.Align(EditorSession.AlignMode.Top, toArtboard: true);
        V.Nearly(0f, a.DocumentBounds.Top);
        V.Nearly(0f, b.DocumentBounds.Top);

        session.Align(EditorSession.AlignMode.Bottom, toArtboard: true);
        V.Nearly(session.Document.Active.Bounds.Bottom, a.DocumentBounds.Bottom);
        V.Nearly(session.Document.Active.Bounds.Bottom, b.DocumentBounds.Bottom);

        session.Align(EditorSession.AlignMode.CenterHorizontal, toArtboard: true);
        V.Nearly(session.Document.Active.Bounds.Center.X, a.DocumentBounds.Center.X);
        V.Nearly(session.Document.Active.Bounds.Center.X, b.DocumentBounds.Center.X);
    }

    [Fact]
    public void SingleObjectAlignsToItsArtboard()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 100, 100, 50, 20);
        session.Select(path);

        session.Align(EditorSession.AlignMode.Left);
        V.Nearly(0f, path.DocumentBounds.Left);

        session.Align(EditorSession.AlignMode.Top);
        V.Nearly(0f, path.DocumentBounds.Top);

        session.Align(EditorSession.AlignMode.CenterVertical);
        V.Nearly(session.Document.Active.Bounds.Center.Y, path.DocumentBounds.Center.Y);
    }

    [Fact]
    public void DistributionNeedsThreeObjectsAndEqualisesGaps()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 30, 0, 20, 10);
        PathObject c = V.AddRect(session, 80, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);
        session.Toggle(c);

        session.Align(EditorSession.AlignMode.DistributeHorizontal, toArtboard: true);

        float gap1 = b.DocumentBounds.Left - a.DocumentBounds.Right;
        float gap2 = c.DocumentBounds.Left - b.DocumentBounds.Right;
        V.Nearly(gap1, gap2, 0.1f);

        EditorSession two = V.Session();
        PathObject x = V.AddRect(two, 0, 0, 10, 10);
        PathObject y = V.AddRect(two, 40, 0, 10, 10);
        two.Select(x);
        two.Toggle(y);
        two.Align(EditorSession.AlignMode.DistributeVertical);
        V.Nearly(0f, x.DocumentBounds.Left);
        V.Nearly(40f, y.DocumentBounds.Left);
    }

    [Fact]
    public void TransformFieldsMoveAndResizeTheSelection()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 10, 20, 100, 50);
        session.Select(path);

        session.SetSelectionPosition(30, 40);
        V.Nearly(30f, path.DocumentBounds.Left);
        V.Nearly(40f, path.DocumentBounds.Top);

        session.SetSelectionSize(200, 100);
        V.Nearly(200f, path.DocumentBounds.Width);
        V.Nearly(100f, path.DocumentBounds.Height);

        session.SetSelectionSize(0, 0);
        Assert.True(path.DocumentBounds.Width > 0f);
        Assert.True(path.DocumentBounds.Height > 0f);
    }

    [Fact]
    public void AppearanceCommandsUpdateEverySelectedFace()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10, fill: null);
        PathObject b = V.AddRect(session, 20, 0, 10, 10, fill: null);
        session.Select(a);
        session.Toggle(b);

        session.SetSelectionFill(0xFFFF1122);
        Assert.Equal(0xFFFF1122u, a.Fill!.Color);
        Assert.Equal(0xFFFF1122u, b.Fill!.Color);
        Assert.Equal(0xFFFF1122u, session.FillColor);
        Assert.Equal(0xFFFF1122u, session.RecentColours[0]);

        session.SetSelectionStroke(0xFF00FF33);
        Assert.Equal(0xFF00FF33u, a.Stroke!.Paint.Color);
        session.SetSelectionStrokeWidth(6f);
        Assert.Equal(6f, a.Stroke!.Width);
        Assert.Equal(6f, session.StrokeWidth);

        session.ClearSelectionStroke();
        Assert.Null(a.Stroke);
        session.ClearSelectionFill();
        Assert.Null(a.Fill);
    }

    [Fact]
    public void OpacityPreviewCanBeCommittedOrDiscarded()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);

        session.PreviewSelectionOpacity(0.4f);
        Assert.Equal(0.4f, path.Opacity);

        session.CommitSelectionOpacity(0.4f);
        Assert.Equal(0.4f, path.Opacity);
        Assert.True(session.CanUndo);

        session.SetSelectionOpacity(2f);
        Assert.Equal(1f, path.Opacity);
        session.SetSelectionOpacity(-1f);
        Assert.Equal(0f, path.Opacity);

        session.Undo();
        Assert.True(path.Opacity is >= 0f and <= 1f);
    }

    [Fact]
    public void CornerRadiusOnlyAffectsLiveRoundedRectangles()
    {
        EditorSession session = V.Session();
        PathObject rounded = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.RoundedRectangle, Width = 100, Height = 50, CornerRadius = 10 },
            Fill = Paint.Solid(0xFFFFFFFF),
            Transform = Mat.Translate(50, 25),
        };
        rounded.Rebuild();
        session.ActiveLayer.Add(rounded);
        PathObject plain = V.AddRect(session, 0, 0, 10, 10);
        session.Select(rounded);
        session.Toggle(plain);

        session.SetSelectionCornerRadius(20f);

        Assert.Equal(20f, rounded.Shape!.CornerRadius);
        Assert.Equal(8, rounded.Path.SubPaths[0].Nodes.Count);
        Assert.Equal(4, plain.Path.SubPaths[0].Nodes.Count);
    }

    [Fact]
    public void TextPropertiesEditEverySelectedTextObject()
    {
        EditorSession session = V.Session();
        TextObject a = V.AddText(session, 0, 0, "One", 20f);
        TextObject b = V.AddText(session, 0, 50, "Two", 20f);
        session.Select(a);
        session.Toggle(b);

        session.SetTextContent("Changed");
        Assert.Equal("Changed", a.Text);
        Assert.Equal("Changed", b.Text);

        session.SetTextSize(64f);
        Assert.Equal(64f, a.FontSize);
        session.NudgeTextSize(-4f);
        Assert.Equal(60f, a.FontSize);
        session.NudgeTextSize(999f);
        Assert.Equal(512f, a.FontSize);

        session.SetTextFont("Arial");
        Assert.Equal("Arial", a.FontFamily);
        session.ToggleTextBold();
        Assert.True(a.Bold);
        session.ToggleTextBold();
        Assert.False(a.Bold);
        session.ToggleTextItalic();
        Assert.True(a.Italic);
    }

    [Fact]
    public void ConvertReverseAndSimplifyPaths()
    {
        EditorSession session = V.Session();
        PathObject shape = V.AddRect(session, 0, 0, 100, 100);
        session.Select(shape);

        session.ConvertShapesToPaths();
        Assert.False(shape.IsShape);
        Assert.Contains("Converted", session.Status);

        PathNode first = shape.Path.SubPaths[0].Nodes[0];
        session.ReverseSelectedPaths();
        Assert.NotEqual(first.Point, shape.Path.SubPaths[0].Nodes[0].Point);
        Assert.Contains("reversed", session.Status);

        List<PathNode> before = [.. shape.Path.SubPaths[0].Nodes];
        session.SimplifySelectedPaths(1000f);
        Assert.True(shape.Path.SubPaths[0].Nodes.Count <= before.Count);
        Assert.Contains("simplified", session.Status);
    }

    [Fact]
    public void LayerCommandsAddDeleteAndProtectTheLastLayer()
    {
        EditorSession session = V.Session();
        LayerObject first = session.ActiveLayer;

        session.AddLayer();
        Assert.Equal(2, session.Document.Layers.Count());
        Assert.NotSame(first, session.ActiveLayer);

        session.DeleteActiveLayer();
        Assert.Single(session.Document.Layers);

        session.DeleteActiveLayer();
        Assert.Single(session.Document.Layers);
        Assert.Contains("at least one layer", session.Status);

        session.SetActiveLayer(first);
        Assert.Same(first, session.Document.ActiveLayer);
    }

    [Fact]
    public void ArtboardCommandsAddSelectDuplicateResizeAndDelete()
    {
        EditorSession session = V.Session();
        session.SetViewSize(800, 600);

        session.AddArtboard();
        Assert.Equal(2, session.Document.Artboards.Count);
        Assert.Equal(1, session.Document.ActiveArtboard);
        Assert.NotEqual(0f, session.Document.Active.X);

        session.DuplicateArtboard(1);
        Assert.Equal(3, session.Document.Artboards.Count);
        Assert.Contains("copy", session.Document.Active.Name);

        session.SelectArtboard(0);
        Assert.Equal(0, session.Document.ActiveArtboard);

        session.SetArtboardSize(400, 300);
        Assert.Equal(400f, session.Document.Active.Width);
        Assert.Equal(300f, session.Document.Active.Height);
        session.SetArtboardSize(1, 1);
        Assert.Equal(16f, session.Document.Active.Width);

        session.SetArtboardBackground(0xFF102030);
        Assert.Equal(0xFF102030u, session.Document.Active.Background);

        session.DeleteArtboard(0);
        Assert.Equal(2, session.Document.Artboards.Count);
        session.DeleteArtboard(0);
        session.DeleteArtboard(0);
        Assert.Single(session.Document.Artboards);
    }

    [Fact]
    public void SampleDocumentContainsShapesAndText()
    {
        EditorSession session = V.Session();
        session.LoadSample();

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal("Sample Poster", session.Document.Name);
        Assert.Equal("Illustration", session.Document.ActiveLayer!.Name);
        Assert.True(session.Document.ActiveLayer.Children.OfType<PathObject>().Count() >= 8);
        Assert.True(session.Document.ActiveLayer.Children.OfType<TextObject>().Count() >= 3);
    }

    [Fact]
    public void PushHistoryCapsTheUndoStack()
    {
        EditorSession session = V.Session();
        for (int i = 0; i < 120; i++)
        {
            session.PushHistory("Step " + i);
        }

        int count = 0;
        while (session.CanUndo)
        {
            session.Undo();
            count++;
        }

        Assert.Equal(100, count);
    }
}
