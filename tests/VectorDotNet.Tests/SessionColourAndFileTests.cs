using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SessionColourTests
{
    [Fact]
    public void ColourTargetsResolveSelectionThenDefaults()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233, 0xFF445566);
        TextObject text = V.AddText(session, 0, 50);
        text.Fill = Paint.Solid(0xFF778899);

        Assert.Equal(session.FillColor, session.ColourForTarget(ColourTarget.Fill));
        session.Select(path);
        Assert.Equal(0xFF112233u, session.ColourForTarget(ColourTarget.Fill));
        Assert.Equal(0xFF445566u, session.ColourForTarget(ColourTarget.Stroke));
        session.Select(text);
        Assert.Equal(0xFF778899u, session.ColourForTarget(ColourTarget.Fill));
        session.DeselectAll();
        Assert.Equal(session.FillColor, session.ColourForTarget(ColourTarget.Fill));
        Assert.Equal(session.StrokeColor, session.ColourForTarget(ColourTarget.Stroke));
        Assert.Equal(session.Document.Active.Background, session.ColourForTarget(ColourTarget.Artboard));
    }

    [Fact]
    public void PickerTitlesDescribeTheTarget()
    {
        EditorSession session = V.Session();

        session.OpenColourPicker(ColourTarget.Fill);
        Assert.Equal("Fill Colour", session.ColourPickerTitle);
        session.OpenColourPicker(ColourTarget.Stroke);
        Assert.Equal("Stroke Colour", session.ColourPickerTitle);
        session.OpenColourPicker(ColourTarget.Artboard);
        Assert.Equal("Artboard Background", session.ColourPickerTitle);
    }

    [Fact]
    public void PreviewCommitUpdatesSelectionAndRecentColours()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Fill);

        Assert.True(session.ShowColourPicker);
        Assert.Equal(ColourTarget.Fill, session.ColourPickerTarget);
        Assert.Equal(path.Fill!.Color, session.ColourPickerInitial);

        session.PreviewColour(0xFFAA2211);
        Assert.Equal(0xFFAA2211u, path.Fill.Color);
        Assert.False(session.CanUndo);

        session.CommitColour(0xFFAA2211);
        Assert.False(session.ShowColourPicker);
        Assert.Equal(0xFFAA2211u, session.FillColor);
        Assert.Equal(0xFFAA2211u, session.RecentColours[0]);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void StrokeTargetCreatesAStrokeWhenNeeded()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, fill: 0xFFFFFFFF, stroke: null);
        session.Select(path);
        session.OpenColourPicker(ColourTarget.Stroke);

        session.CommitColour(0xFF336699);

        Assert.NotNull(path.Stroke);
        Assert.Equal(0xFF336699u, path.Stroke!.Paint.Color);
        Assert.True(path.Stroke.Width >= 1f);
        Assert.Equal(0xFF336699u, session.StrokeColor);
    }

    [Fact]
    public void ArtboardTargetPreviewsAndCommitsTheBackground()
    {
        EditorSession session = V.Session();
        session.OpenColourPicker(ColourTarget.Artboard);

        session.PreviewColour(0xFF203040);
        Assert.Equal(0xFF203040u, session.Document.Active.Background);

        session.CommitColour(0xFF203040);
        Assert.Equal(0xFF203040u, session.Document.Active.Background);
    }

    [Fact]
    public void CancelRestoresEveryPreviewedFace()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10, 0xFF112233, 0xFF445566);
        TextObject text = V.AddText(session, 0, 50);
        text.Fill = Paint.Solid(0xFF778899);
        session.Select(path);
        session.Toggle(text);
        uint fill = session.FillColor;
        uint stroke = session.StrokeColor;

        session.OpenColourPicker(ColourTarget.Fill);
        session.PreviewColour(0xFFFFFFFF);
        session.CancelColourPicker();

        Assert.False(session.ShowColourPicker);
        Assert.Equal(0xFF112233u, path.Fill!.Color);
        Assert.Equal(0xFF778899u, text.Fill!.Color);
        Assert.Equal(fill, session.FillColor);

        session.OpenColourPicker(ColourTarget.Stroke);
        session.PreviewColour(0xFF00FF00);
        session.CancelColourPicker();
        Assert.Equal(0xFF445566u, path.Stroke!.Paint.Color);
        Assert.Equal(stroke, session.StrokeColor);
    }

    [Fact]
    public void DocumentColoursCollectUniqueVisiblePaintsAndBackground()
    {
        EditorSession session = V.Session();
        V.AddRect(session, 0, 0, 10, 10, 0xFFFF0000, 0xFF00FF00);
        TextObject text = V.AddText(session, 0, 20);
        text.Fill = Paint.Solid(0xFF0000FF);
        session.Document.Active.Background = 0xFFABCDEF;
        V.AddRect(session, 20, 0, 10, 10, 0x00FFFFFF);

        IReadOnlyList<uint> colours = session.DocumentColours;

        Assert.Contains(0xFFFF0000u, colours);
        Assert.Contains(0xFF00FF00u, colours);
        Assert.Contains(0xFF0000FFu, colours);
        Assert.Contains(0xFFABCDEFu, colours);
        Assert.DoesNotContain(0x00FFFFFFu, colours);
        Assert.Equal(colours.Distinct().Count(), colours.Count);
    }

    [Fact]
    public void RecentColoursDeduplicateAndCapAtTwelve()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);

        for (int i = 0; i < 20; i++)
        {
            session.SetSelectionFill(0xFF000000u | (uint)i);
        }

        Assert.Equal(12, session.RecentColours.Count);
        Assert.Equal(0xFF000013u, session.RecentColours[0]);

        session.SetSelectionFill(0xFF00000Au);
        Assert.Equal(0xFF00000Au, session.RecentColours[0]);
        Assert.Single(session.RecentColours, colour => colour == 0xFF00000Au);
    }
}

public sealed class SessionFileTests
{
    [Fact]
    public void BrowserModesTitlesAndExtensionsFollowThePurpose()
    {
        EditorSession session = V.Session();

        session.OpenImportDialog();
        Assert.True(session.ShowFileBrowser);
        Assert.Equal(FileBrowserMode.Open, session.BrowserMode);
        Assert.Equal(FileBrowserPurpose.ImportSvg, session.BrowserPurpose);
        Assert.Equal("Import SVG", session.BrowserTitle);
        Assert.Equal("Import", session.BrowserConfirmLabel);
        Assert.Equal([".svg", ".svgz"], session.BrowserExtensions);
        session.CloseFileBrowser();
        Assert.False(session.ShowFileBrowser);

        session.OpenExportDialog();
        Assert.Equal(FileBrowserMode.Save, session.BrowserMode);
        Assert.Equal("Export SVG", session.BrowserTitle);
        Assert.Equal("Export", session.BrowserConfirmLabel);
        Assert.EndsWith(".svg", session.BrowserName);

        session.OpenFontBrowser();
        Assert.Equal(FileBrowserPurpose.Font, session.BrowserPurpose);
        Assert.Equal("Load Font", session.BrowserTitle);
        Assert.Equal([".ttf", ".otf", ".woff", ".woff2"], session.BrowserExtensions);
    }

    [Fact]
    public void ImportBrowserAddsTheChosenSvgAsANewTab()
    {
        using TempDirectory temp = new();
        string svg = temp.File("drawing.svg");
        File.WriteAllText(svg, """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="80">
              <rect x="10" y="10" width="30" height="20" fill="#ff0000"/>
            </svg>
            """);

        EditorSession session = V.Session();
        session.OpenImportDialog();
        session.NavigateBrowser(temp.Path);
        FileEntry entry = session.BrowserEntries.Single(item => item.Name == "drawing.svg");
        session.BrowserClickEntry(entry);

        Assert.Equal(svg, session.BrowserSelected);
        Assert.Equal("drawing.svg", session.BrowserName);

        session.BrowserConfirm();

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal("drawing", session.Document.Name);
        Assert.Equal(100f, session.Document.Active.Width);
        Assert.Contains(
            session.Document.Root.SelfAndDescendants().OfType<PathObject>(),
            path => path.Name == "Rectangle");
        Assert.False(session.ShowFileBrowser);
        Assert.Equal(string.Empty, session.ImportStatus);
    }

    [Fact]
    public void ImportMissingFileReportsAnError()
    {
        using TempDirectory temp = new();
        EditorSession session = V.Session();
        session.OpenImportDialog();
        session.NavigateBrowser(temp.Path);

        session.BrowserConfirm();
        Assert.Equal("Choose a file.", session.BrowserStatus);
        Assert.True(session.ShowFileBrowser);

        session.SetImportPath(temp.File("missing.svg"));
        session.ConfirmImport();
        Assert.Contains("File not found", session.ImportStatus);
    }

    [Fact]
    public void ExportBrowserWritesSvgAndSvgz()
    {
        using TempDirectory temp = new();
        EditorSession session = V.Session();
        V.AddRect(session, 0, 0, 10, 10);

        session.OpenExportDialog();
        session.NavigateBrowser(temp.Path);
        session.BrowserName = "drawing";
        session.BrowserConfirm();

        string svg = temp.File("drawing.svg");
        Assert.True(File.Exists(svg));
        Assert.Contains("<svg", File.ReadAllText(svg));
        Assert.False(session.ShowFileBrowser);
        Assert.StartsWith("Saved", session.ExportStatus);

        using TempDirectory compressed = new();
        session.OpenExportDialog();
        session.NavigateBrowser(compressed.Path);
        session.BrowserName = "zipped.svgz";
        session.BrowserConfirm();

        string svgz = compressed.File("zipped.svgz");
        Assert.True(File.Exists(svgz));
        using System.IO.Compression.GZipStream gzip = new(File.OpenRead(svgz), System.IO.Compression.CompressionMode.Decompress);
        using StreamReader reader = new(gzip);
        Assert.Contains("<svg", reader.ReadToEnd());
    }

    [Fact]
    public void ConfirmExportAddsTheSvgExtensionAndHandlesBadPaths()
    {
        using TempDirectory temp = new();
        EditorSession session = V.Session();
        session.SetExportPath(temp.File("no-extension"));
        session.ConfirmExport();
        Assert.True(File.Exists(temp.File("no-extension.svg")));

        session.SetExportPath(System.IO.Path.Combine(temp.Path, "bad\0name.svg"));
        session.ConfirmExport();
        Assert.StartsWith("Could not save", session.ExportStatus);
    }

    [Fact]
    public void BrowserListsFoldersFirstFiltersExtensionsAndNavigates()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(temp.File("Folder"));
        File.WriteAllText(temp.File("b.svg"), "<svg/>");
        File.WriteAllText(temp.File("a.svgz"), "gzip");
        File.WriteAllText(temp.File("ignore.png"), "png");

        EditorSession session = V.Session();
        session.OpenImportDialog();
        session.NavigateBrowser(temp.Path);

        Assert.Equal(3, session.BrowserEntries.Count);
        Assert.True(session.BrowserEntries[0].IsFolder);
        Assert.Equal("Folder", session.BrowserEntries[0].Name);
        Assert.Equal("a.svgz", session.BrowserEntries[1].Name);
        Assert.Equal("b.svg", session.BrowserEntries[2].Name);

        session.BrowserClickEntry(session.BrowserEntries[0]);
        Assert.Equal(temp.File("Folder"), session.BrowserFolder);
        session.BrowserGoUp();
        Assert.Equal(temp.Path, session.BrowserFolder);

        session.NavigateBrowser(temp.File("missing"));
        Assert.Equal("Folder not found.", session.BrowserStatus);
    }

    [Fact]
    public void SaveBrowserRequiresAName()
    {
        using TempDirectory temp = new();
        EditorSession session = V.Session();
        session.OpenExportDialog();
        session.NavigateBrowser(temp.Path);
        session.BrowserName = "   ";

        session.BrowserConfirm();

        Assert.Equal("Enter a file name.", session.BrowserStatus);
        Assert.True(session.ShowFileBrowser);
    }

    [Fact]
    public void FontBrowserRegistersTheChosenFile()
    {
        using TempDirectory temp = new();
        string font = temp.File("custom.ttf");
        File.WriteAllBytes(font, [0, 1, 0, 0]);
        EditorSession session = V.Session();
        session.RegisterFontFile = path => System.IO.Path.GetFileName(path) == "custom.ttf" ? "Custom Family" : null;
        session.OpenFontBrowser();
        session.NavigateBrowser(temp.Path);

        FileEntry entry = session.BrowserEntries.Single(item => item.Name == "custom.ttf");
        session.BrowserClickEntry(entry);
        session.BrowserConfirm();

        Assert.Equal("Custom Family", session.DefaultFontFamily);
        Assert.False(session.ShowFileBrowser);
        Assert.Contains("Custom Family", session.Status);

        session.RegisterFontFile = _ => null;
        session.OpenFontBrowser();
        session.NavigateBrowser(temp.Path);
        session.BrowserClickEntry(session.BrowserEntries.Single(item => item.Name == "custom.ttf"));
        session.BrowserConfirm();
        Assert.Equal("Could not read that font file.", session.BrowserStatus);
    }

    [Fact]
    public void FontPickerSearchAndApplyFlow()
    {
        EditorSession session = V.Session();
        TextObject text = V.AddText(session, 0, 0);
        session.Select(text);

        session.OpenFontPicker();
        Assert.True(session.ShowFontPicker);
        Assert.Equal(string.Empty, session.FontSearch);
        Assert.Equal(text.FontFamily, session.CurrentFontFamily);

        session.SetFontSearch("Arial");
        Assert.All(session.FilteredFontFamilies, family => Assert.Contains("Arial", family, StringComparison.OrdinalIgnoreCase));

        session.ApplyFontFamily("Arial");
        Assert.False(session.ShowFontPicker);
        Assert.Equal("Arial", text.FontFamily);
        Assert.Equal("Arial", session.DefaultFontFamily);

        session.OpenFontPicker();
        session.LoadFontFromPicker();
        Assert.False(session.ShowFontPicker);
        Assert.True(session.ShowFileBrowser);
        Assert.Equal(FileBrowserPurpose.Font, session.BrowserPurpose);

        session.CloseFontPicker();
        Assert.False(session.ShowFontPicker);
    }
}

public sealed class SessionSharedStateTests
{
    [Fact]
    public void RecentColoursAndDocumentColoursAreStableAcrossUndo()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);
        session.SetSelectionFill(0xFF123456);
        session.Undo();

        Assert.Equal(0xFF123456u, session.RecentColours[0]);
        Assert.Contains(0xFF4C8DFFu, session.DocumentColours);
    }

    [Fact]
    public void NotifyRefreshesAndTouchAdvancesRevision()
    {
        EditorSession session = V.Session();
        int refreshes = 0;
        session.Refresh = () => refreshes++;
        int revision = session.Revision;

        session.Touch();
        Assert.Equal(revision + 1, session.Revision);
        Assert.Equal(0, refreshes);

        session.Notify();
        Assert.Equal(1, refreshes);
        Assert.Equal(revision + 2, session.Revision);

        session.SetStatus("Hello");
        Assert.Equal("Hello", session.Status);
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public void SelectionBoundsEmptyAndZoomToSelectionNoOp()
    {
        EditorSession session = V.Session();
        session.ZoomAt(new Vec2(10, 10), 2f);
        float zoom = session.Zoom;

        session.ZoomToSelection();
        Assert.Equal(zoom, session.Zoom);

        session.Select(null);
        Assert.Empty(session.Selection);
    }
}
