using System.Numerics;
using VectorDotNet.Components;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SessionKeyboardAndHelperTests
{
    [Fact]
    public void ClipboardAndGroupShortcutsEditTheDocument()
    {
        EditorSession session = V.Session();
        PathObject a = V.AddRect(session, 0, 0, 10, 10);
        PathObject b = V.AddRect(session, 30, 0, 10, 10);
        session.Select(a);
        Assert.True(session.HandleKey("Ctrl+C"));
        session.HandleKey("Ctrl+V");
        Assert.Equal(3, session.ActiveLayer.Children.Count);

        session.HandleKey("Ctrl+X");
        Assert.Equal(2, session.ActiveLayer.Children.Count);

        session.Select(a);
        session.HandleKey("Ctrl+D");
        Assert.Equal(3, session.ActiveLayer.Children.Count);

        session.HandleKey("Ctrl+A");
        Assert.Equal(3, session.Selection.Count);
        session.HandleKey("Ctrl+G");
        Assert.Single(session.ActiveLayer.Children);
        Assert.IsType<GroupObject>(session.ActiveLayer.Children[0]);

        session.HandleKey("Ctrl+Shift+G");
        Assert.Equal(3, session.ActiveLayer.Children.Count);
    }

    [Fact]
    public void DocumentImportExportAndZoomShortcutsOpenDialogsAndChangeView()
    {
        EditorSession session = V.Session();
        session.SetViewSize(1280, 800);
        session.ActualPixels();

        session.HandleKey("Ctrl+N");
        Assert.Equal(2, session.DocumentCount);

        session.HandleKey("Ctrl+Shift+I");
        Assert.True(session.ShowFileBrowser);
        Assert.Equal(FileBrowserPurpose.ImportSvg, session.BrowserPurpose);
        session.CloseFileBrowser();

        session.HandleKey("Ctrl+Shift+E");
        Assert.Equal(FileBrowserPurpose.ExportSvg, session.BrowserPurpose);
        session.CloseFileBrowser();

        session.ZoomAt(new Vec2(10, 10), 2f);
        session.HandleKey("Ctrl+0");
        Assert.Equal(1f, session.Zoom);
        session.HandleKey("Ctrl+Equal");
        Assert.Equal(1.25f, session.Zoom, 3);
        session.HandleKey("Ctrl+Minus");
        Assert.Equal(1f, session.Zoom, 3);
    }

    [Fact]
    public void UnknownAndTypingShortcutsAreRejected()
    {
        EditorSession session = V.Session();

        Assert.False(session.HandleKey("Unknown"));
        Assert.False(session.HandleKey("Ctrl+Unknown"));

        session.IsTyping = true;
        Assert.False(session.HandleKey("V"));
        Assert.False(session.HandleKey("Ctrl+Z"));
    }

    [Fact]
    public void EnterFinishesAnOpenPenPath()
    {
        EditorSession session = V.InputSession();
        session.SetTool(ToolKind.Pen);
        V.Click(session, new Vec2(20, 20));
        V.Click(session, new Vec2(80, 20));
        Assert.True(session.IsPenActive);

        Assert.True(session.HandleKey("Enter"));

        Assert.False(session.IsPenActive);
        PathObject path = Assert.IsType<PathObject>(session.Selection[0]);
        Assert.False(path.Path.SubPaths[0].Closed);
        Assert.Null(path.Fill);
    }

    [Fact]
    public void ArrowKeysNudgeByOneOrTen()
    {
        EditorSession session = V.Session();
        PathObject path = V.AddRect(session, 0, 0, 10, 10);
        session.Select(path);

        session.HandleKey("ArrowRight");
        session.HandleKey("ArrowDown");
        V.Nearly(1f, path.DocumentBounds.Left);
        V.Nearly(1f, path.DocumentBounds.Top);

        session.HandleKey("Shift+ArrowLeft");
        session.HandleKey("Shift+ArrowUp");
        V.Nearly(-9f, path.DocumentBounds.Left);
        V.Nearly(-9f, path.DocumentBounds.Top);
    }

    [Fact]
    public void StaticTransformHelpersRespectParents()
    {
        Document document = new();
        GroupObject group = new() { Transform = Mat.Translate(100, 50) };
        PathObject path = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 10, Height = 10 },
        };
        path.Rebuild();
        group.Add(path);
        document.ActiveLayer!.Add(group);

        EditorSession.TranslateObject(path, 5, 7);
        Assert.Equal(new Vec2(5, 7), path.Transform.Apply(Vec2.Zero));

        EditorSession.ApplyDocumentMatrix(path, Mat.Translate(10, 0));
        V.Nearly(110f, path.DocumentBounds.Left);

        EditorSession.TranslateObject(path, 0, 0);
        Assert.Equal(110f, path.DocumentBounds.Left);
    }

    [Fact]
    public void ShapeAndTextDefaultsCanBeUpdated()
    {
        EditorSession session = V.Session();
        PathObject polygon = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Polygon, Width = 100, Height = 100, Points = 5 },
            Fill = Paint.Solid(0xFFFFFFFF),
        };
        polygon.Rebuild();
        session.ActiveLayer.Add(polygon);
        PathObject star = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Star, Width = 100, Height = 100, Points = 5, InnerRatio = 0.5f },
            Fill = Paint.Solid(0xFFFFFFFF),
        };
        star.Rebuild();
        session.ActiveLayer.Add(star);
        TextObject text = V.AddText(session, 0, 0, "Hi", 20f);
        session.Select(polygon);
        session.Toggle(star);
        session.Toggle(text);

        session.SetSelectionPoints(9);
        Assert.Equal(9, polygon.Shape!.Points);
        Assert.Equal(9, star.Shape!.Points);
        Assert.Equal(9, session.DefaultPoints);

        session.SetSelectionInnerRatio(0.2f);
        Assert.Equal(0.2f, star.Shape.InnerRatio);
        Assert.Equal(0.2f, session.DefaultInnerRatio);

        session.ToggleTextItalic();
        Assert.True(text.Italic);
        session.NudgeTextSize(4f);
        Assert.Equal(24f, text.FontSize);
    }

    [Fact]
    public void OpenDocumentAddsANamedTab()
    {
        EditorSession session = V.Session();
        Document document = new(300, 200) { Name = "Imported Art" };
        document.ActiveLayer!.Add(V.AddRectToDocument(document, 0, 0, 10, 10, 0xFFFF0000));

        session.OpenDocument(document);

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal("Imported Art", session.Document.Name);
        Assert.Equal(300f, session.Document.Active.Width);
    }

    [Fact]
    public void BrowserUsePlaceAndFontHelpersAreSafe()
    {
        EditorSession session = V.Session();
        session.OpenImportDialog();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home))
        {
            session.BrowserUsePlace(home);
            Assert.Equal(home, session.BrowserFolder);
        }

        Assert.Null(FontLibrary.Register(System.IO.Path.Combine(home, "missing-font.ttf")));
        FontLibrary.EnsureFamily("Arial");
    }
}
