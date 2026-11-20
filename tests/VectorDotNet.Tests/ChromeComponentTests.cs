using Donkeh.Testing;
using Donkeh.UILab;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab._;

namespace VectorDotNet.Tests;

public sealed class ToolRailTests
{
    [Fact]
    public void RailListsEveryToolIncludingGroupSeparators()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<ToolRail>());

        Assert.Equal(15, page.FindAll(".tool-btn").Count);
        Assert.Equal(4, page.FindAll(".rail-sep").Count);
        Assert.True(page.Find(".tool-btn").HasClass("is-active"));

        page.Click("[title='Rectangle (M)']");
        Assert.Equal(ToolKind.Rectangle, session.Tool);

        page.Click("[title='Pen (P)']");
        Assert.Equal(ToolKind.Pen, session.Tool);
    }

    [Fact]
    public void ColourSwatchesOpenThePickerAndSwapValues()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<ToolRail>());

        page.Click("[title='Fill colour picker']");
        Assert.True(session.ShowColourPicker);
        Assert.Equal(ColourTarget.Fill, session.ColourPickerTarget);
        session.CancelColourPicker();

        uint fill = session.FillColor;
        uint stroke = session.StrokeColor;
        page.Click("[title='Stroke colour picker']");
        Assert.Equal(ColourTarget.Stroke, session.ColourPickerTarget);
        session.CancelColourPicker();

        page.Click("[title='Swap fill and stroke']");
        Assert.Equal(stroke, session.FillColor);
        Assert.Equal(fill, session.StrokeColor);
    }

    [Fact]
    public void SwatchesShowTheCurrentFillAndStroke()
    {
        EditorSession session = V.Session();
        session.FillColor = 0xFF112233;
        session.StrokeColor = 0xFF445566;
        var page = V.Render(session, C<ToolRail>());

        Assert.Contains("#112233", page.Find(".rail-swatch.is-fill").GetAttribute("style"));
        Assert.Contains("#445566", page.Find(".rail-swatch.is-stroke").GetAttribute("style"));
    }
}

public sealed class MenuBarTests
{
    [Fact]
    public void MenubarRendersBrandDocumentReadoutAndEveryMenu()
    {
        EditorSession session = V.Session();
        session.Document.Name = "Poster";
        var page = V.Render(session, C<MenuBar>());

        Assert.Equal("VectorDotNet", page.Find(".brand-name").InnerText);
        Assert.Equal(9, page.FindAll(".menu-title").Count);
        Assert.Contains("Poster", page.Find(".menu-doc-name").InnerText);
        Assert.Contains("1200 × 800 px", page.Find(".menu-doc-name").InnerText);
    }

    [Fact]
    public void MenuTitlesOpenAndHoverSweeps()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<MenuBar>());

        page.FindAll(".menu-title").First(title => title.Text == "File").Click();
        Assert.Equal("File", session.OpenMenu);

        page.Hover(page.FindAll(".menu-title").First(title => title.Text == "Edit"));
        Assert.Equal("Edit", session.OpenMenu);
    }

    [Fact]
    public void OverlayRendersRowsSeparatorsShortcutsAndChecks()
    {
        EditorSession session = V.Session();
        session.ToggleMenuAt("View", 20, 30);
        session.ShowGrid = true;
        var page = V.Render(session, C<MenuOverlay>());

        Assert.Contains("left:20px", page.Find(".menu-float").GetAttribute("style"));
        Assert.NotEmpty(page.FindAll(".menu-item"));
        Assert.NotEmpty(page.FindAll(".menu-sep"));
        Assert.Contains(page.FindAll(".menu-item-key"), key => key.Text == "Ctrl+0");
        TestElement gridRow = page.FindAll(".menu-item")
            .First(row => row.Find(".menu-item-label").Text == "Show Grid");
        Assert.NotEmpty(gridRow.FindAll(".fa-check"));
    }

    [Fact]
    public void ClickingAMenuRowRunsTheAction()
    {
        EditorSession session = V.Session();
        session.OpenMenu = "File";
        var page = V.Render(session, C<MenuOverlay>());

        page.FindByText("New Square Document").Click();

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal(1080f, session.Document.Active.Width);
        Assert.Null(session.OpenMenu);
    }

    [Fact]
    public void EveryMenuActionRunsWithoutThrowing()
    {
        foreach (MenuDefinition menu in MenuBar.Menus)
        {
            for (int index = 0; index < menu.Items.Length; index++)
            {
                EditorSession session = V.Session();
                MenuAction action = menu.Items[index];
                action.Run(session);
            }
        }
    }

    [Fact]
    public void FindMenuLooksUpByTitle()
    {
        Assert.NotNull(MenuBar.Find("File"));
        Assert.NotNull(MenuBar.Find("Help"));
        Assert.Null(MenuBar.Find("Nope"));
        Assert.Null(MenuBar.Find(null));
    }
}

public sealed class OptionsBarTests
{
    [Fact]
    public void ZoomGroupShowsAndChangesZoom()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1280, 800);
        session.ActualPixels();
        var page = V.Render(session, C<OptionsBar>());

        Assert.Equal("100%", page.Find(".zoom-value").Text);
        page.Click("[title='Zoom in (Ctrl++)']");
        Assert.Equal(1.25f, session.Zoom, 3);

        page.Find(".zoom-value").Click();
        Assert.Equal(1f, session.Zoom);
    }

    [Fact]
    public void ViewAndActionTogglesUpdateState()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<OptionsBar>());

        page.Click("[title='Show grid']");
        Assert.True(session.ShowGrid);
        page.Click("[title='Snap to objects']");
        Assert.False(session.SnapToObjects);
        page.Click("[title='Show rulers']");
        Assert.False(session.ShowRulers);
        page.Click("[title='Import SVG (Ctrl+Shift+I)']");
        Assert.True(session.ShowFileBrowser);
        Assert.Equal(FileBrowserPurpose.ImportSvg, session.BrowserPurpose);
        session.CloseFileBrowser();
        page.Click("[title='Export SVG (Ctrl+Shift+E)']");
        Assert.Equal(FileBrowserPurpose.ExportSvg, session.BrowserPurpose);
    }

    [Fact]
    public void AlignButtonsTrackSelectionAvailability()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<OptionsBar>());

        Assert.All(page.FindAll("[title='Align left']"), button => Assert.True(button.HasAttribute("disabled")));

        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);
        V.Reload<OptionsBar>(page, session);

        Assert.False(page.Find("[title='Align left']").HasAttribute("disabled"));
        page.Click("[title='Align right']");
        V.Nearly(session.Document.Active.Bounds.Right, path.DocumentBounds.Right);
    }

    [Fact]
    public void GroupButtonsEnableForSelectionsAndGroups()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);
        var page = V.Render(session, C<OptionsBar>());

        page.Click("[title='Group selection (Ctrl+G)']");
        Assert.Single(session.ActiveLayer.Children);
        Assert.IsType<GroupObject>(session.ActiveLayer.Children[0]);

        session.Selection.Clear();
        session.Selection.Add(session.ActiveLayer.Children[0]);
        V.Reload<OptionsBar>(page, session);
        page.Click("[title='Ungroup selection (Ctrl+Shift+G)']");
        Assert.Equal(2, session.ActiveLayer.Children.Count);
    }

    [Fact]
    public void SelectionReadoutCoversNoneOneAndMany()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<OptionsBar>());

        Assert.Equal("No selection", page.Find(".options-readout strong").Text);
        Assert.Contains("1200 × 800 px", page.Find(".options-detail").Text);

        PathObject a = V.AddRect(session, 10, 20, 30, 40);
        session.Select(a);
        V.Reload<OptionsBar>(page, session);
        Assert.Equal("Rectangle", page.Find(".options-readout strong").Text);
        Assert.Contains("30 × 40", page.Find(".options-detail").Text);

        PathObject b = V.AddRect(session, 100, 100, 10, 10);
        session.Toggle(b);
        V.Reload<OptionsBar>(page, session);
        Assert.Equal("2 objects", page.Find(".options-readout strong").Text);
    }

    [Fact]
    public void RoundedRectangleContextFieldUpdatesTheRadius()
    {
        EditorSession session = V.Session();
        session.SetTool(ToolKind.RoundedRectangle);
        var page = V.Render(session, C<OptionsBar>());

        Assert.Equal("Radius", page.Find(".options-context-label").Text);
        page.Change(".options-context-input", "24");
        Assert.Equal(24f, session.DefaultCornerRadius);
    }

    [Fact]
    public void PolygonAndStarContextFieldsUpdateDefaults()
    {
        EditorSession session = V.Session();
        session.SetTool(ToolKind.Polygon);
        var page = V.Render(session, C<OptionsBar>());

        Assert.Equal("Sides", page.Find(".options-context-label").Text);
        page.Change(".options-context-input", "8");
        Assert.Equal(8, session.DefaultPoints);

        session.SetTool(ToolKind.Star);
        page = V.Render(session, C<OptionsBar>());
        Assert.Equal(2, page.FindAll(".options-context").Count);
        page.Change(page.FindAll(".options-context-input")[1], "35");
        Assert.Equal(0.35f, session.DefaultInnerRatio, 2);
    }
}

public sealed class StatusBarTests
{
    [Fact]
    public void StatusBarShowsToolSelectionCursorObjectsArtboardAndMessage()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 10, 10, 20, 20);
        session.Select(path);
        session.SetTool(ToolKind.Rectangle);
        session.PointerMove(session.DocumentToScreen(new Vec2(50, 60)));
        session.SetStatus("Working");

        var page = V.Render(session, C<StatusBar>());

        Assert.Equal("Rectangle", page.Find(".status-tool").Text);
        Assert.Contains("Rectangle", page.FindAll(".status-item")[1].Text);
        Assert.Contains("20 × 20", page.FindAll(".status-item")[1].Text);
        Assert.Contains("50, 60", page.FindAll(".status-item")[2].Text);
        Assert.Contains("1 objects", page.FindAll(".status-item").Select(item => item.Text));
        Assert.Contains("Artboard 1", page.FindAll(".status-item").Select(item => item.Text));
        Assert.Equal("Working", page.Find(".status-message").Text);
    }

    [Fact]
    public void MultipleAndNoSelectionsReadCorrectly()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<StatusBar>());
        Assert.Contains("No selection", page.FindAll(".status-item")[1].Text);

        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);
        V.Reload<StatusBar>(page, session);
        Assert.Contains("2 objects", page.FindAll(".status-item")[1].Text);
        Assert.Contains("2 objects", page.FindAll(".status-item").Select(item => item.Text));
    }
}
