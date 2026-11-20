using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using Donkeh.Windowing;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Tests;

public sealed class ViewportTests
{
    [Fact]
    public void StageRendersTabsAndTheCanvas()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<Viewport>());

        Assert.NotNull(page.TryFind(".stage"));
        Assert.Single(page.FindAll(".doc-tab"));
        Assert.True(page.Find(".doc-tab").HasClass("is-active"));
        Assert.NotNull(page.TryFind(".doc-tab-add"));
        Assert.Equal("1280", page.Find(".surface").GetAttribute("width"));
        Assert.Equal("800", page.Find(".surface").GetAttribute("height"));
        Assert.True(page.Find(".surface").HasClass("is-select"));
    }

    [Fact]
    public void TabsAddSwitchAndClose()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<Viewport>());

        page.Find(".doc-tab-add").Click();
        Assert.Equal(2, session.DocumentCount);
        V.Reload<Viewport>(page, session);
        Assert.Equal(2, page.FindAll(".doc-tab").Count);

        page.FindAll(".doc-tab")[0].Click();
        Assert.Equal(0, session.ActiveDocumentIndex);

        page.FindAll(".doc-tab-close")[1].Click();
        Assert.Single(session.OpenDocuments);
    }

    [Fact]
    public void CursorClassFollowsTheTool()
    {
        EditorSession session = V.Session();
        Dictionary<ToolKind, string> expected = new()
        {
            [ToolKind.Select] = "is-select",
            [ToolKind.Direct] = "is-direct",
            [ToolKind.Pen] = "is-pen",
            [ToolKind.Pencil] = "is-draw",
            [ToolKind.Text] = "is-text",
            [ToolKind.Lasso] = "is-lasso",
            [ToolKind.Zoom] = "is-zoom",
            [ToolKind.Hand] = "is-hand",
        };

        foreach ((ToolKind tool, string cls) in expected)
        {
            session.SetTool(tool);
            var page = V.Render(session, C<Viewport>());
            Assert.True(page.Find(".surface").HasClass(cls));
        }
    }

    [Fact]
    public void CanvasForwardsCreationPointerEvents()
    {
        EditorSession session = V.Session();
        session.SetTool(ToolKind.Rectangle);
        session.SetViewSize(1280, 800);
        session.ActualPixels();
        var page = V.Render(session, C<Viewport>());
        TestElement canvas = page.Find(".surface");
        Rect box = canvas.Bounds;

        page.Dispatch(canvas, new PointerDownEvent(box.X + 200, box.Y + 200), bubble: false);
        page.Dispatch(canvas, new PointerMoveEvent(box.X + 400, box.Y + 320), bubble: false);
        page.Dispatch(canvas, new PointerUpEvent(box.X + 400, box.Y + 320), bubble: false);

        Assert.Single(session.ActiveLayer.Children.OfType<PathObject>());
        Assert.Single(session.Selection);
    }

    [Fact]
    public void CanvasContextMenuDeselects()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 10, 10, 20, 20);
        session.Select(path);
        var page = V.Render(session, C<Viewport>());
        TestElement canvas = page.Find(".surface");
        Rect box = canvas.Bounds;

        page.Dispatch(
            canvas,
            new ContextMenuEvent(
                canvas.Element,
                canvas.Element,
                5,
                5,
                box.X + 5,
                box.Y + 5),
            bubble: false);

        Assert.Empty(session.Selection);
    }

    [Fact]
    public void CanvasScrollWithControlZooms()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1280, 800);
        session.ActualPixels();
        var page = V.Render(session, C<Viewport>());
        TestElement canvas = page.Find(".surface");
        Rect box = canvas.Bounds;
        float zoom = session.Zoom;

        InputControl.Pointer(box.X + (box.Width * 0.5f), box.Y + (box.Height * 0.5f));
        InputControl.Set("Control", true);
        try
        {
            page.Dispatch(canvas, new ScrollEvent(0, 120), bubble: false);
        }
        finally
        {
            InputControl.Reset();
        }

        Assert.True(session.Zoom > zoom);
    }

    [Fact]
    public void CanvasScrollWithoutModifiersPans()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1280, 800);
        session.ActualPixels();
        var page = V.Render(session, C<Viewport>());
        TestElement canvas = page.Find(".surface");
        float panX = session.PanX;
        float panY = session.PanY;

        page.Dispatch(canvas, new ScrollEvent(15, 25), bubble: false);

        V.Nearly(panX + 15f, session.PanX);
        V.Nearly(panY + 25f, session.PanY);
    }

    [Fact]
    public void MiddlePanGestureScrollsTheSession()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1280, 800);
        session.ActualPixels();
        float panX = session.PanX;
        float panY = session.PanY;
        var page = V.Render(session, C<Viewport>());
        TestElement canvas = page.Find(".surface");
        float x = canvas.Bounds.X + (canvas.Bounds.Width * 0.5f);
        float y = canvas.Bounds.Y + (canvas.Bounds.Height * 0.5f);

        try
        {
            InputControl.PressSequence(501);
            InputControl.Set("Middle", true);
            InputControl.Pointer(x, y);
            Assert.True(MiddlePan.Handle(session));

            InputControl.Pointer(x + 20f, y + 10f);
            Assert.True(MiddlePan.Handle(session));
            V.Nearly(panX + 20f, session.PanX);
            V.Nearly(panY + 10f, session.PanY);
        }
        finally
        {
            MiddlePan.Reset();
            InputControl.Reset();
        }
    }
}

public sealed class PanelShellTests
{
    [Fact]
    public void SidePanelShowsTheSubjectAndEverySection()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<SidePanel>());

        Assert.Equal("Layer 1", page.Find(".side-head-name").Text);
        Assert.Equal(5, page.FindAll(".panel").Count);
        Assert.Contains("Transform", page.FindAll(".panel-title").Select(title => title.Text));
        Assert.Contains("Appearance", page.FindAll(".panel-title").Select(title => title.Text));
        Assert.Contains("Layers", page.FindAll(".panel-title").Select(title => title.Text));
        Assert.Contains("Colour", page.FindAll(".panel-title").Select(title => title.Text));
        Assert.Contains("Artboards", page.FindAll(".panel-title").Select(title => title.Text));
    }

    [Fact]
    public void SidePanelSubjectTracksOneOrManySelections()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        session.Select(a);
        var page = V.Render(session, C<SidePanel>());
        Assert.Equal("Rectangle", page.Find(".side-head-name").Text);

        session.Toggle(b);
        V.Reload<SidePanel>(page, session);
        Assert.Equal("2 objects", page.Find(".side-head-name").Text);
    }

    [Fact]
    public void PanelHeaderCollapsesAndExpands()
    {
        var page = V.Render(V.Session(), C<Panel, PanelProps>(new PanelProps
        {
            Title = "Test Panel",
            Badge = "3",
            Children = [Div(new() { ClassName = "inner" })],
        }));

        Assert.NotNull(page.TryFind(".inner"));
        Assert.Equal("3", page.Find(".panel-badge").Text);

        page.Find(".panel-head").Click();
        Assert.Null(page.TryFind(".inner"));

        page.Find(".panel-head").Click();
        Assert.NotNull(page.TryFind(".inner"));
    }

    [Fact]
    public void ClosedPanelDoesNotRenderItsChildren()
    {
        var page = V.Render(V.Session(), C<Panel, PanelProps>(new PanelProps
        {
            Title = "Closed",
            StartOpen = false,
            Children = [Div(new() { ClassName = "inner" })],
        }));

        Assert.Null(page.TryFind(".inner"));
        Assert.Equal("Closed", page.Find(".panel-title").Text);
    }
}
