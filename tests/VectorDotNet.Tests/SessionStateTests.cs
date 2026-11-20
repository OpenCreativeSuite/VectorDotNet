using System.Numerics;
using VectorDotNet.Components;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SessionStateTests
{
    [Fact]
    public void NewSessionHasABlankDocumentAndDefaults()
    {
        EditorSession session = new();

        Assert.Single(session.OpenDocuments);
        Assert.Equal("Untitled-1", session.Document.Name);
        Assert.Single(session.Document.Layers);
        Assert.Equal(ToolKind.Select, session.Tool);
        Assert.Equal("Selection", session.ToolName);
        Assert.Equal(0xFF4C8DFFu, session.FillColor);
        Assert.Equal(0xFF1B1D22u, session.StrokeColor);
        Assert.Equal(3, session.RecentColours.Count);
        Assert.Equal("Ready", session.Status);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.True(session.ShowSidebar);
        Assert.True(session.ShowRulers);
        Assert.Equal(22f, session.RulerSize);
    }

    [Fact]
    public void EveryToolHasANameAndStatusHint()
    {
        EditorSession session = V.Session();
        HashSet<string> names = [];

        foreach (ToolKind tool in Enum.GetValues<ToolKind>())
        {
            session.SetTool(tool);
            Assert.Equal(tool, session.Tool);
            Assert.False(string.IsNullOrWhiteSpace(session.ToolName));
            Assert.False(string.IsNullOrWhiteSpace(session.Status));
            names.Add(session.ToolName);
        }

        Assert.Equal(Enum.GetValues<ToolKind>().Length, names.Count);
    }

    [Fact]
    public void SwitchingToolsCancelsTransientEditors()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Pen);
        V.Click(session, new Vec2(20, 20));
        Assert.True(session.IsPenActive);

        session.SetTool(ToolKind.Rectangle);

        Assert.False(session.IsPenActive);
        Assert.Equal(ToolKind.Rectangle, session.Tool);
    }

    [Fact]
    public void SelectionPrimitivesMaintainTheList()
    {
        EditorSession session = V.Session();
        PathObject first = V.AddRect(session, 0, 0, 10, 10);
        PathObject second = V.AddRect(session, 20, 20, 10, 10);

        session.Select(first);
        Assert.Single(session.Selection);
        Assert.Same(first, session.Selection[0]);

        session.Toggle(second);
        Assert.Equal(2, session.Selection.Count);
        session.Toggle(first);
        Assert.Single(session.Selection);

        session.Toggle(second);
        session.Toggle(first);
        session.DeselectAll();
        Assert.Empty(session.Selection);
        Assert.Equal("Deselected", session.Status);

        session.SelectAll();
        Assert.Equal(2, session.Selection.Count);
        Assert.Contains("2 objects", session.Status);
    }

    [Fact]
    public void SelectAllSkipsHiddenAndLockedObjects()
    {
        EditorSession session = V.Session();
        V.AddRect(session, 0, 0, 10, 10);
        PathObject hidden = V.AddRect(session, 0, 0, 10, 10);
        hidden.Visible = false;
        PathObject locked = V.AddRect(session, 0, 0, 10, 10);
        locked.Locked = true;

        session.SelectAll();

        Assert.Single(session.Selection);
        Assert.DoesNotContain(hidden, session.Selection);
        Assert.DoesNotContain(locked, session.Selection);
    }

    [Fact]
    public void ToggleLockAlsoDeselects()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);

        session.ToggleLock(path);
        Assert.True(path.Locked);
        Assert.Empty(session.Selection);

        session.ToggleLock(path);
        Assert.False(path.Locked);
    }

    [Fact]
    public void ToggleVisibilityRoundTrips()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);

        session.ToggleVisibility(path);
        Assert.False(path.Visible);
        Assert.True(session.CanUndo);

        session.ToggleVisibility(path);
        Assert.True(path.Visible);
    }

    [Fact]
    public void SelectionBoundsUnionAndStatusReflectTheSelection()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 10, 10, 20, 20);
        PathObject b = V.AddRect(session, 40, 30, 10, 10);
        session.Select(a);
        session.Toggle(b);

        Bounds bounds = session.SelectionBounds;
        V.Nearly(10f, bounds.Left);
        V.Nearly(50f, bounds.Right);
        V.Nearly(40f, bounds.Bottom);
    }

    [Fact]
    public void ZoomConversionPanAndSelectionFit()
    {
        EditorSession session = V.Session();
        session.SetViewSize(800, 600);
        session.ZoomAt(new Vec2(100, 100), 2f);

        Vec2 document = session.ScreenToDocument(new Vec2(300, 200));
        Assert.Equal(new Vec2(300, 200), session.DocumentToScreen(document));

        float panX = session.PanX;
        float panY = session.PanY;
        session.PanBy(10, -5);
        Assert.Equal(2f, session.Zoom);
        Assert.Equal(panX + 10f, session.PanX);
        Assert.Equal(panY - 5f, session.PanY);

        PathObject path = V.AddRect(session, 100, 100, 200, 100);
        session.Select(path);
        session.ZoomToSelection();
        Assert.True(session.Zoom > 0.01f);
        Assert.True(session.Zoom <= 64f);
    }

    [Fact]
    public void ZoomInOutAndActualSizeStepThroughTheLevels()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1280, 800);
        session.ActualPixels();
        Assert.Equal(1f, session.Zoom);

        session.ZoomIn();
        Assert.Equal(1.25f, session.Zoom, 3);
        session.ZoomOut();
        Assert.Equal(1f, session.Zoom, 3);

        session.ZoomAt(new Vec2(100, 100), 9999f);
        Assert.Equal(64f, session.Zoom);
        session.ZoomAt(new Vec2(100, 100), 0.0001f);
        Assert.Equal(0.05f, session.Zoom);

        session.FitArtboard();
        Assert.True(session.Zoom < 1f);
    }

    [Fact]
    public void WheelZoomAndScrollPanBehaveDifferently()
    {
        EditorSession session = V.Session();
        session.SetViewSize(800, 600);
        session.ActualPixels();
        float before = session.Zoom;

        session.ZoomByWheel(120f, new Vec2(100, 100));
        Assert.True(session.Zoom > before);

        session.ZoomByWheel(0f, new Vec2(100, 100));
        Assert.True(session.Zoom > before);

        float startX = session.PanX;
        float startY = session.PanY;
        session.Scroll(10f, 20f, new Vec2(100, 100), control: false, shift: false);
        Assert.Equal(startX + 10f, session.PanX);
        Assert.Equal(startY + 20f, session.PanY);

        session.Scroll(10f, 20f, new Vec2(100, 100), control: false, shift: true);
        Assert.Equal(startX + 30f, session.PanX);
        Assert.Equal(startY + 20f, session.PanY);

        float zoom = session.Zoom;
        session.Scroll(0f, 120f, new Vec2(100, 100), control: true, shift: false);
        Assert.True(session.Zoom > zoom);
    }

    [Fact]
    public void ViewTogglesAndCanvasClipTrackSettings()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1000, 700);

        session.ToggleGrid();
        Assert.True(session.ShowGrid);
        session.ToggleRulers();
        Assert.False(session.ShowRulers);
        Assert.Equal(0f, session.RulerSize);
        session.ToggleGuides();
        Assert.False(session.ShowGuides);
        session.ToggleGridSnapping();
        Assert.True(session.SnapToGrid);
        session.ToggleObjectSnapping();
        Assert.False(session.SnapToObjects);
        session.ToggleSmartGuides();
        Assert.False(session.SmartGuides);

        Bounds clip = session.CanvasClip;
        Assert.Equal(0f, clip.X);
        Assert.Equal(0f, clip.Y);
        Assert.Equal(1000f, clip.Width);
    }

    [Fact]
    public void DocumentTabsOpenSwitchAndClose()
    {
        EditorSession session = V.Session();
        session.ZoomAt(new Vec2(10, 10), 2f);
        session.NewDocument(640, 480);

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal(1, session.ActiveDocumentIndex);
        Assert.Equal(640f, session.Document.Active.Width);

        session.SwitchDocument(0);
        Assert.Equal(0, session.ActiveDocumentIndex);
        Assert.Equal(1200f, session.Document.Active.Width);

        session.SwitchDocument(99);
        Assert.Equal(0, session.ActiveDocumentIndex);

        session.CloseDocument(0);
        Assert.Single(session.OpenDocuments);
        Assert.Equal(640f, session.Document.Active.Width);

        session.CloseDocument(0);
        Assert.Single(session.OpenDocuments);
        Assert.Equal("Untitled-1", session.Document.Name);
    }

    [Fact]
    public void ImportAndExportStatusesAndMenusAreManaged()
    {
        EditorSession session = V.Session();

        session.ToggleMenu("File");
        Assert.Equal("File", session.OpenMenu);
        session.HoverMenu("Edit");
        Assert.Equal("Edit", session.OpenMenu);
        session.CloseMenu();
        Assert.Null(session.OpenMenu);

        session.ToggleMenuAt("View", 40f, 30f);
        Assert.Equal("View", session.OpenMenu);
        Assert.Equal(40f, session.MenuX);
        session.HoverMenuAt("Object", 80f, 40f);
        Assert.Equal("Object", session.OpenMenu);
        Assert.Equal(80f, session.MenuX);
        session.ToggleMenuAt("Object", 80f, 40f);
        Assert.Null(session.OpenMenu);

        session.OpenShortcuts();
        Assert.True(session.ShowShortcuts);
        session.CloseShortcuts();
        Assert.False(session.ShowShortcuts);

        session.ToggleSidebar();
        Assert.False(session.ShowSidebar);
    }

    [Fact]
    public void HistoryNudgesAndKeyboardCommandsWork()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);

        session.HandleKey("ArrowRight");
        V.Nearly(1f, path.DocumentBounds.Left);
        session.HandleKey("Shift+ArrowDown");
        V.Nearly(10f, path.DocumentBounds.Top);

        session.HandleKey("Ctrl+Z");
        PathObject undone = (PathObject)session.Document.Find(path.Id)!;
        V.Nearly(0f, undone.DocumentBounds.Top);
        session.HandleKey("Ctrl+Y");
        PathObject redone = (PathObject)session.Document.Find(path.Id)!;
        V.Nearly(10f, redone.DocumentBounds.Top);

        session.HandleKey("Delete");
        Assert.Empty(session.Selection);
        Assert.Empty(session.ActiveLayer.Children);
    }

    [Fact]
    public void KeyboardToolShortcutsMapEveryTool()
    {
        EditorSession session = V.Session();
        Dictionary<string, ToolKind> map = new()
        {
            ["V"] = ToolKind.Select,
            ["A"] = ToolKind.Direct,
            ["P"] = ToolKind.Pen,
            ["N"] = ToolKind.Pencil,
            ["M"] = ToolKind.Rectangle,
            ["E"] = ToolKind.Ellipse,
            ["R"] = ToolKind.RoundedRectangle,
            ["L"] = ToolKind.Line,
            ["S"] = ToolKind.Star,
            ["G"] = ToolKind.Polygon,
            ["T"] = ToolKind.Text,
            ["Q"] = ToolKind.Lasso,
            ["Z"] = ToolKind.Zoom,
            ["H"] = ToolKind.Hand,
        };

        foreach ((string key, ToolKind tool) in map)
        {
            Assert.True(session.HandleKey(key));
            Assert.Equal(tool, session.Tool);
        }

        Assert.False(session.HandleKey("Unknown"));
        session.IsTyping = true;
        Assert.False(session.HandleKey("V"));
    }

    [Fact]
    public void EscapeClosesMenusPensNodeEditAndSelection()
    {
        EditorSession session = V.InputSession();
        session.ToggleMenu("File");
        session.HandleKey("Escape");
        Assert.Null(session.OpenMenu);

        session.SetTool(ToolKind.Pen);
        V.Click(session, new Vec2(20, 20));
        session.HandleKey("Escape");
        Assert.False(session.IsPenActive);

        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);
        session.SetTool(ToolKind.Direct);
        session.Select(path);
        Assert.NotNull(session.EditTarget);
        session.HandleKey("Escape");
        Assert.Null(session.EditTarget);
        Assert.NotEmpty(session.Selection);

        session.HandleKey("Escape");
        Assert.Empty(session.Selection);
    }

    [Fact]
    public void IconsAndUiHelpersCoverTheSharedFormattingRules()
    {
        Assert.Equal("12.5", Ui.Number(12.5f));
        Assert.Equal("13", Ui.Format(12.6f));
        Assert.Equal("50", Ui.Percent(0.5f));
        Assert.Equal(5f, Ui.Parse("5%", 0f));
        Assert.Equal(7f, Ui.Parse("nope", 7f));
        Assert.Equal("#102030", Ui.Hex(0xFF102030));
        Dictionary<string, string> size = Ui.Size(64, 48);
        Assert.Equal("64", size["width"]);

        Assert.Equal("fa-square", Icons.Rectangle);
        Assert.Equal("fa-bezier-curve", Icons.Pen);
        Assert.Equal("fa-arrow-pointer", Icons.Select);
    }
}
