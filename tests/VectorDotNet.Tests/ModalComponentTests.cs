using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab._;

namespace VectorDotNet.Tests;

public sealed class FileBrowserTests
{
    [Fact]
    public void ImportBrowserListsSvgRowsAndSelectsAFile()
    {
        using TempDirectory temp = new();
        File.WriteAllText(temp.File("art.svg"), "<svg/>");
        EditorSession session = V.Session();
        session.OpenImportDialog();
        session.NavigateBrowser(temp.Path);
        var page = V.Render(session, C<FileBrowser>());

        Assert.Equal("Import SVG", page.Find(".modal-title").Text);
        Assert.Equal(temp.Path, page.Find(".file-path").Value);
        Assert.Single(page.FindAll(".file-row"));
        Assert.Contains("Import", page.FindAll(".btn").Select(button => button.Text));

        page.Find(".file-row").Click();
        Assert.EndsWith("art.svg", session.BrowserSelected);
        Assert.Equal("art.svg", session.BrowserName);

        page.FindAll(".btn").First(button => button.Text == "Cancel").Click();
        Assert.False(session.ShowFileBrowser);
    }

    [Fact]
    public void EmptyFolderShowsTheEmptyState()
    {
        using TempDirectory temp = new();
        EditorSession session = V.Session();
        session.OpenImportDialog();
        session.NavigateBrowser(temp.Path);
        var page = V.Render(session, C<FileBrowser>());

        Assert.NotNull(page.TryFind(".file-empty"));
        Assert.Null(page.TryFind(".file-row"));
    }

    [Fact]
    public void ExportBrowserWritesTheFileFromTheNameField()
    {
        using TempDirectory temp = new();
        EditorSession session = V.Session();
        session.OpenExportDialog();
        session.NavigateBrowser(temp.Path);
        var page = V.Render(session, C<FileBrowser>());

        Assert.Equal("Export SVG", page.Find(".modal-title").Text);
        page.Input(".file-name", "poster");
        Assert.Equal("poster", session.BrowserName);

        page.FindAll(".btn").First(button => button.Text == "Export").Click();

        Assert.True(File.Exists(temp.File("poster.svg")));
        Assert.False(session.ShowFileBrowser);
    }

    [Fact]
    public void FolderPlacesAndParentNavigationAreRendered()
    {
        EditorSession session = V.Session();
        session.OpenImportDialog();
        var page = V.Render(session, C<FileBrowser>());

        Assert.NotEmpty(page.FindAll(".file-place"));
        TestElement place = page.FindAll(".file-place")[0];
        place.Click();
        Assert.Equal(place.GetAttribute("title"), session.BrowserFolder);

        page.Click("[title='Parent folder']");
        Assert.NotEqual(place.GetAttribute("title"), session.BrowserFolder);
    }

    [Fact]
    public void FontPurposeShowsTheFontConfirmLabel()
    {
        EditorSession session = V.Session();
        session.OpenFontBrowser();
        var page = V.Render(session, C<FileBrowser>());

        Assert.Equal("Load Font", page.Find(".modal-title").Text);
        Assert.Contains("Load Font", page.FindAll(".btn").Select(button => button.Text));
    }
}

public sealed class FontPickerTests
{
    [Fact]
    public void PickerListsAndFiltersFamilies()
    {
        EditorSession session = V.Session();
        session.OpenFontPicker();
        var page = V.Render(session, C<FontPicker>());

        Assert.Equal("Fonts", page.Find(".modal-title").Text);
        Assert.NotEmpty(page.FindAll(".font-row"));
        Assert.Contains("fonts", page.Find(".font-count").Text);

        page.Input(".font-search", "Arial");
        Assert.Equal("Arial", session.FontSearch);
        Assert.All(session.FilteredFontFamilies, family => Assert.Contains("Arial", family, StringComparison.OrdinalIgnoreCase));
        V.Reload<FontPicker>(page, session);
        Assert.NotEmpty(page.FindAll(".font-row"));
    }

    [Fact]
    public void ChoosingARowAppliesTheFamilyAndCloses()
    {
        EditorSession session = V.Session();
        TextObject text = V.AddText(session, 0, 0);
        session.Select(text);
        session.OpenFontPicker();
        var page = V.Render(session, C<FontPicker>());
        string family = page.FindAll(".font-row")[0].GetAttribute("title")!;

        page.FindAll(".font-row")[0].Click();

        Assert.Equal(family, text.FontFamily);
        Assert.Equal(family, session.DefaultFontFamily);
        Assert.False(session.ShowFontPicker);
    }

    [Fact]
    public void EmptySearchResultShowsTheEmptyState()
    {
        EditorSession session = V.Session();
        session.OpenFontPicker();
        session.SetFontSearch("definitely-not-a-font-81");
        var page = V.Render(session, C<FontPicker>());

        Assert.Empty(page.FindAll(".font-row"));
        Assert.NotNull(page.TryFind(".font-empty"));
        Assert.Equal("no matches", page.Find(".font-count").Text);
    }

    [Fact]
    public void LoadFontFileSwitchesToTheBrowser()
    {
        EditorSession session = V.Session();
        session.OpenFontPicker();
        var page = V.Render(session, C<FontPicker>());

        page.FindAll(".btn").First(button => button.Text.StartsWith("Load font file")).Click();

        Assert.False(session.ShowFontPicker);
        Assert.True(session.ShowFileBrowser);
        Assert.Equal(FileBrowserPurpose.Font, session.BrowserPurpose);
    }

    [Fact]
    public void CloseButtonClosesThePicker()
    {
        EditorSession session = V.Session();
        session.OpenFontPicker();
        var page = V.Render(session, C<FontPicker>());

        page.Click("[title='Close']");
        Assert.False(session.ShowFontPicker);
    }
}

public sealed class ColourPickerTests
{
    [Fact]
    public void PickerShowsOriginalAndLivePreview()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Fill);
        var page = V.Render(session, C<ColourPicker>());

        Assert.Equal("Fill Colour", page.Find(".modal-title").Text);
        Assert.Equal(2, page.FindAll(".picker-preview-swatch").Count);
        Assert.Contains("#112233", page.FindAll(".picker-preview-swatch")[0].GetAttribute("style"));
        Assert.NotEmpty(page.FindAll(".picker-field"));
        Assert.Equal(2, page.FindAll(".picker-strip").Count);
        Assert.Equal(4, page.FindAll(".picker-channel-input").Count);
    }

    [Fact]
    public void HexEntryPreviewsAndApplyCommits()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Fill);
        var page = V.Render(session, C<ColourPicker>());

        page.Change(".picker-hex", "#00FF00");

        Assert.Equal(0xFF00FF00u, path.Fill!.Color);
        Assert.False(session.CanUndo);

        page.FindAll(".btn").First(button => button.Text == "Apply").Click();

        Assert.False(session.ShowColourPicker);
        Assert.Equal(0xFF00FF00u, session.FillColor);
        Assert.Equal(0xFF00FF00u, path.Fill.Color);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void CancelRestoresTheOriginalColour()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Fill);
        var page = V.Render(session, C<ColourPicker>());

        page.Change(".picker-hex", "#00FF00");
        page.FindAll(".btn").First(button => button.Text == "Cancel").Click();

        Assert.False(session.ShowColourPicker);
        Assert.Equal(0xFF112233u, path.Fill!.Color);
    }

    [Fact]
    public void RgbAndAlphaFieldsPreviewLive()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF102030);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Fill);
        var page = V.Render(session, C<ColourPicker>());
        var channels = page.FindAll(".picker-channel-input");

        page.Input(channels[0], "255");
        Assert.Equal(255, Colour.R(path.Fill!.Color));

        page.Input(channels[1], "128");
        Assert.Equal(128, Colour.G(path.Fill.Color));

        page.Input(channels[2], "64");
        Assert.Equal(64, Colour.B(path.Fill.Color));

        page.Input(channels[3], "50");
        Assert.InRange(Colour.A(path.Fill.Color), 125, 130);
    }

    [Fact]
    public void SaturationFieldAndStripsRespondToPointerInput()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFFFF0000);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Fill);
        var page = V.Render(session, C<ColourPicker>());

        TestElement field = page.Find(".picker-field");
        Rect fieldBox = field.Bounds;
        page.Dispatch(
            field,
            new PointerDownEvent(fieldBox.X + 100, fieldBox.Y + 50),
            bubble: false);

        Assert.NotEqual(0xFFFF0000u, path.Fill!.Color);

        TestElement hue = page.FindAll(".picker-strip")[0];
        Rect hueBox = hue.Bounds;
        page.Dispatch(
            hue,
            new PointerDownEvent(hueBox.X + 180, hueBox.Y + 5),
            bubble: false);
        Assert.NotEqual(0xFFFF0000u, path.Fill.Color);

        TestElement alpha = page.FindAll(".picker-strip")[1];
        Rect alphaBox = alpha.Bounds;
        page.Dispatch(
            alpha,
            new PointerDownEvent(alphaBox.X + 100, alphaBox.Y + 5),
            bubble: false);
        Assert.InRange(Colour.A(path.Fill.Color), 1, 254);
    }

    [Fact]
    public void RecentSwatchesPreviewTheClickedColour()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Stroke);
        var page = V.Render(session, C<ColourPicker>());

        Assert.NotEmpty(page.FindAll(".picker-swatch"));
        TestElement swatch = page.FindAll(".picker-swatch")[0];
        uint expected = Convert.ToUInt32(swatch.GetAttribute("title")!.TrimStart('#'), 16);
        swatch.Click();

        Assert.NotNull(path.Stroke);
        Assert.Equal(expected, path.Stroke!.Paint.Color & 0x00FFFFFF);
    }
}

public sealed class ShortcutsModalTests
{
    [Fact]
    public void ShortcutSheetListsToolAndEditKeys()
    {
        EditorSession session = V.Session();
        session.OpenShortcuts();
        var page = V.Render(session, C<ShortcutsModal>());

        Assert.Equal("Keyboard Shortcuts", page.Find(".modal-title").Text);
        Assert.Equal(2, page.FindAll(".modal-group-title").Count);
        Assert.Equal(29, page.FindAll(".shortcut-row").Count);
        Assert.Contains(page.FindAll("kbd").Select(key => key.Text), text => text == "Ctrl+Z");
        Assert.Contains(page.FindAll(".shortcut-row"), row => row.InnerText.Contains("Selection"));

        page.Click("[title='Close']");
        Assert.False(session.ShowShortcuts);
    }
}
