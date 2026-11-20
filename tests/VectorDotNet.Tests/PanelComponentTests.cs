using System.Numerics;
using Donkeh.Testing;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab._;

namespace VectorDotNet.Tests;

public sealed class LayersPanelTests
{
    [Fact]
    public void LayersAppearTopDownWithTheActiveLayerMarked()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        var page = V.Render(session, C<LayersPanel>());

        Assert.Equal("Layers", page.Find(".panel-title").Text);
        Assert.Equal("1", page.Find(".panel-badge").Text);
        Assert.Equal(2, page.FindAll(".layer-row").Count);
        Assert.True(page.FindAll(".layer-row")[0].HasClass("is-layer"));
        Assert.True(page.FindAll(".layer-row")[0].HasClass("is-selected"));
        Assert.Equal("Rectangle", page.FindAll(".layer-row")[1].Find(".layer-name").Text);
    }

    [Fact]
    public void ClickingRowsSelectsObjectsOrActivatesLayers()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.AddLayer();
        var page = V.Render(session, C<LayersPanel>());

        page.FindAll(".layer-row")
            .First(row => row.Find(".layer-name").Text == "Rectangle")
            .Click();
        Assert.Same(path, session.Selection.Single());

        page.FindAll(".layer-row")
            .First(row => row.Find(".layer-name").Text == "Layer 1" && row.HasClass("is-layer"))
            .Click();
        Assert.Equal(2, session.Document.Layers.Count());
    }

    [Fact]
    public void VisibilityLockAndCaretButtonsUpdateTheModel()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        var page = V.Render(session, C<LayersPanel>());
        TestElement objectRow = page.FindAll(".layer-row")[1];

        objectRow.Find("[title='Hide']").Click();
        Assert.False(path.Visible);

        V.Reload<LayersPanel>(page, session);
        page.FindAll(".layer-row")[1].Find("[title='Show']").Click();
        Assert.True(path.Visible);

        V.Reload<LayersPanel>(page, session);
        page.FindAll(".layer-row")[1].Find("[title='Lock']").Click();
        Assert.True(path.Locked);

        V.Reload<LayersPanel>(page, session);
        page.FindAll(".layer-row")[1].Find("[title='Unlock']").Click();
        Assert.False(path.Locked);
    }

    [Fact]
    public void GroupCaretCollapsesAndExpandsChildren()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);
        session.GroupSelection();
        var page = V.Render(session, C<LayersPanel>());

        int rows = page.FindAll(".layer-row").Count;
        Assert.Contains(page.FindAll(".layer-row"), row => row.HasClass("is-layer"));

        TestElement groupRow = page.FindAll(".layer-row")
            .First(row => row.Find(".layer-name").Text == "Group");
        groupRow.Find(".layer-caret").Click();

        Assert.False(((GroupObject)session.ActiveLayer.Children[0]).Expanded);
        Assert.False(session.Selection.OfType<GroupObject>().First().Expanded);
        V.Reload<LayersPanel>(page, session);
        Assert.True(page.FindAll(".layer-row").Count < rows);
        TestElement reloadedGroup = page.FindAll(".layer-row")
            .First(row => row.Find(".layer-name").Text == "Group");
        Assert.Equal("Expand", reloadedGroup.Find(".layer-caret").GetAttribute("title"));
    }

    [Fact]
    public void LayerActionButtonsRunTheModelCommands()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<LayersPanel>());

        page.Click("[title='New layer']");
        Assert.Equal(2, session.Document.Layers.Count());

        page.Click("[title='Delete active layer']");
        Assert.Single(session.Document.Layers);

        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 20, 0, 10, 10);
        session.Select(a);
        session.Toggle(b);
        V.Reload<LayersPanel>(page, session);
        page.Click("[title='Group selection (Ctrl+G)']");
        Assert.Single(session.ActiveLayer.Children);
        Assert.IsType<GroupObject>(session.ActiveLayer.Children[0]);

        session.Selection.Clear();
        session.Selection.Add(session.ActiveLayer.Children[0]);
        V.Reload<LayersPanel>(page, session);
        page.Click("[title='Ungroup selection (Ctrl+Shift+G)']");
        Assert.Equal(2, session.ActiveLayer.Children.Count);
    }

    [Fact]
    public void GroupsShowTheirChildCountAndLayerThumbnailsUseTheFill()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10, 0xFFFF0000);
        session.Select(a);
        session.GroupSelection();
        var page = V.Render(session, C<LayersPanel>());

        TestElement groupRow = page.FindAll(".layer-row").First(row => row.HasClass("is-layer") || row.FindAll(".layer-count").Count > 0);
        Assert.Equal("1", groupRow.Find(".layer-count").Text);
        Assert.Contains(
            page.FindAll(".layer-thumb").Select(thumb => thumb.GetAttribute("style")),
            style => style!.Contains("#FF0000"));
    }
}

public sealed class PropertiesPanelTests
{
    [Fact]
    public void NoSelectionShowsArtboardFieldsAndPageAppearance()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<PropertiesPanel>());

        Assert.Equal(2, page.FindAll(".panel").Count);
        Assert.Contains(page.FindAll(".panel-badge"), badge => badge.Text == "Artboard");
        Assert.Contains(page.FindAll(".panel-badge"), badge => badge.Text == "Document");
        Assert.Equal("1200", page.FindAll(".num-field")[0].Value);
        Assert.Equal("800", page.FindAll(".num-field")[1].Value);
        Assert.Contains("Artboard size", page.Find(".panel-hint").Text);
        Assert.Contains(page.FindAll(".face-label"), label => label.Text == "Page");
    }

    [Fact]
    public void ArtboardFieldsResizeThePage()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<PropertiesPanel>());

        page.Change(page.FindAll(".num-field")[0], "640");
        page.Change(page.FindAll(".num-field")[1], "480");

        Assert.Equal(640f, session.Document.Active.Width);
        Assert.Equal(480f, session.Document.Active.Height);
    }

    [Fact]
    public void SelectionShowsTransformFieldsAndAppearance()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 10, 20, 30, 40, 0xFF112233, 0xFF445566);
        session.Select(path);
        var page = V.Render(session, C<PropertiesPanel>());

        Assert.Contains(page.FindAll(".panel-badge"), badge => badge.Text == "Selection");
        Assert.Equal(5, page.FindAll(".num-field").Count);
        Assert.Equal("10", page.FindAll(".num-field")[0].Value);
        Assert.Equal("20", page.FindAll(".num-field")[1].Value);
        Assert.Equal("30", page.FindAll(".num-field")[2].Value);
        Assert.Equal("40", page.FindAll(".num-field")[3].Value);

        page.Change(page.FindAll(".num-field")[0], "50");
        V.Nearly(50f, path.DocumentBounds.Left);

        page.Change(page.FindAll(".num-field")[2], "60");
        V.Nearly(60f, path.DocumentBounds.Width);

        Assert.Equal("#112233", page.FindAll(".face-hex")[0].Value);
        Assert.Equal("#445566", page.FindAll(".face-hex")[1].Value);
    }

    [Fact]
    public void FillStrokeAndOpacityControlsEditTheSelection()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233, 0xFF445566);
        session.Select(path);
        var page = V.Render(session, C<PropertiesPanel>());

        page.Change(page.FindAll(".face-hex")[0], "#ABCDEF");
        Assert.Equal(0xFFABCDEFu, path.Fill!.Color);

        page.Change(page.FindAll(".face-hex")[1], "#123456");
        Assert.Equal(0xFF123456u, path.Stroke!.Paint.Color);

        page.Find("[title='No fill']").Click();
        Assert.Null(path.Fill);

        page.Find("[title='No stroke']").Click();
        Assert.Null(path.Stroke);

        page.Change(page.FindAll(".num-field")[4], "5");
        Assert.NotNull(path.Stroke);
        Assert.Equal(5f, path.Stroke!.Width);
    }

    [Fact]
    public void OpacitySliderPreviewsAndCommits()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);
        var page = V.Render(session, C<PropertiesPanel>());

        page.Change(".slider-input", "0.4");

        Assert.Equal(0.4f, path.Opacity);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void RoundedRectangleShowsTheRadiusField()
    {
        EditorSession session = V.Session();
        PathObject path = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.RoundedRectangle, Width = 100, Height = 50, CornerRadius = 10 },
            Fill = Paint.Solid(0xFFFFFFFF),
            Transform = Mat.Translate(50, 25),
        };
        path.Rebuild();
        session.ActiveLayer.Add(path);
        session.Select(path);
        var page = V.Render(session, C<PropertiesPanel>());

        Assert.Contains("R", page.FindAll(".field-key").Select(key => key.Text));
        page.Change(page.FindAll(".num-field").Last(), "22");
        Assert.Equal(22f, path.Shape!.CornerRadius);
    }

    [Fact]
    public void TextSelectionShowsFontAndContentControls()
    {
        EditorSession session = V.Session();
        TextObject text = V.AddText(session, 0, 0, "Hello", 32f);
        session.Select(text);
        var page = V.Render(session, C<PropertiesPanel>());

        Assert.Equal("Segoe UI", page.Find(".font-choice").Text);
        page.Change(".text-field-wide", "Changed");
        Assert.Equal("Changed", text.Text);

        page.Change(page.FindAll(".num-field").Last(), "48");
        Assert.Equal(48f, text.FontSize);

        page.Find("[title='Bold']").Click();
        Assert.True(text.Bold);
        page.Find("[title='Italic']").Click();
        Assert.True(text.Italic);

        page.Find(".font-choice").Click();
        Assert.True(session.ShowFontPicker);
    }

    [Fact]
    public void PageBackgroundFieldChangesTheArtboard()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<PropertiesPanel>());

        page.Change(page.FindAll(".face-hex")[2], "#202020");

        Assert.Equal(0xFF202020u, session.Document.Active.Background);
    }
}

public sealed class ColourPanelTests
{
    [Fact]
    public void PreviewBarAndTargetsFollowTheSelection()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233, 0xFF445566);
        session.Select(path);
        var page = V.Render(session, C<ColourPanel>());

        Assert.Equal("Colour", page.Find(".panel-title").Text);
        Assert.Equal("Fill", page.Find(".panel-badge").Text);
        Assert.Contains("#112233", page.Find(".colour-preview-bar").GetAttribute("style"));

        page.Find("[title='Open the stroke colour picker']").Click();
        Assert.Equal(ColourTarget.Stroke, session.ColourPickerTarget);
        Assert.True(session.ShowColourPicker);
    }

    [Fact]
    public void HexFieldAppliesToTheSelectedFace()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233);
        session.Select(path);
        var page = V.Render(session, C<ColourPanel>());

        page.Change(".face-hex", "#AABBCC");
        Assert.Equal(0xFFAABBCCu, path.Fill!.Color);

        page.Find("[title='Open the stroke colour picker']").Click();
        V.Reload<ColourPanel>(page, session);
        page.Change(".face-hex", "#010203");
        Assert.Equal(0xFF010203u, session.StrokeColor);
    }

    [Fact]
    public void RecentAndDocumentSwatchesApplyColours()
    {
        EditorSession session = V.Session();
        V.AddRect(session, 0, 0, 10, 10, 0xFFFF0000);
        var page = V.Render(session, C<ColourPanel>());

        Assert.True(page.FindAll(".swatch-row.is-recent").Count > 0);
        TestElement swatch = page.FindAll(".swatch").First();
        string hex = swatch.GetAttribute("title")!;
        swatch.Click();

        Assert.Equal(hex, Ui.Hex(session.FillColor));
        Assert.Equal(hex, Ui.Hex(session.RecentColours[0]));
    }

    [Fact]
    public void NoSelectionEditsTheDefaultStrokeTarget()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<ColourPanel>());

        page.Find("[title='Open the stroke colour picker']").Click();
        V.Reload<ColourPanel>(page, session);
        page.Change(".face-hex", "#00FF00");
        Assert.Equal(0xFF00FF00u, session.StrokeColor);
    }
}

public sealed class ArtboardsPanelTests
{
    [Fact]
    public void ArtboardListStartsClosedThenShowsRows()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<ArtboardsPanel>());

        Assert.Equal("Artboards", page.Find(".panel-title").Text);
        Assert.Null(page.TryFind(".artboard-row"));

        page.Find(".panel-head").Click();
        Assert.Single(page.FindAll(".artboard-row"));
        Assert.Contains("1200 × 800", page.Find(".artboard-size").Text);
        Assert.True(page.Find(".artboard-row").HasClass("is-selected"));
    }

    [Fact]
    public void ArtboardRowsSelectAndButtonsAddDuplicateDelete()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<ArtboardsPanel>());
        page.Find(".panel-head").Click();
        page.Find("[title='New artboard']").Click();
        Assert.Equal(2, session.Document.Artboards.Count);

        page = V.Render(session, C<ArtboardsPanel>());
        page.Find(".panel-head").Click();
        Assert.Equal(2, page.FindAll(".artboard-row").Count);
        page.FindAll(".artboard-row")[0].Click();
        Assert.Equal(0, session.Document.ActiveArtboard);

        page.FindAll("[title='Duplicate artboard']")[0].Click();
        Assert.Equal(3, session.Document.Artboards.Count);

        page.FindAll("[title='Delete artboard']")[0].Click();
        Assert.Equal(2, session.Document.Artboards.Count);
    }

    [Fact]
    public void DeleteIsDisabledForTheOnlyArtboard()
    {
        EditorSession session = V.Session();
        var page = V.Render(session, C<ArtboardsPanel>());
        page.Find(".panel-head").Click();

        Assert.True(page.Find("[title='Delete artboard']").HasAttribute("disabled"));
        page.Find("[title='Delete artboard']").Click();
        Assert.Single(session.Document.Artboards);
    }
}
