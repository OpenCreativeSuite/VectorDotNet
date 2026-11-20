using System.Numerics;
using System.Text;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class SvgExportTests
{
    [Fact]
    public void ExportWritesArtboardPageAndObjectGeometry()
    {
        Document document = new(200, 100) { Name = "Poster & <Test>" };
        document.Active.Background = 0xFF101114;
        PathObject rect = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = 40, Height = 20 },
            Fill = Paint.Solid(0x80FF0000),
            Stroke = new StrokeStyle
            {
                Paint = Paint.Solid(0xFF00FF00),
                Width = 3,
                Cap = LineCap.Round,
                Join = LineJoin.Bevel,
                Dash = [4, 2],
                DashOffset = 1,
            },
            Transform = Mat.Translate(60, 40),
        };
        rect.Rebuild();
        document.ActiveLayer!.Add(rect);
        TextObject text = new()
        {
            Text = "A & B < C\nSecond",
            FontFamily = "Georgia",
            FontSize = 20,
            Bold = true,
            Italic = true,
            Fill = Paint.Solid(0xCC112233),
            Transform = Mat.Translate(10, 20),
        };
        document.ActiveLayer.Add(text);

        string svg = SvgExporter.Export(document);

        Assert.Contains("width=\"200\" height=\"100\" viewBox=\"0 0 200 100\"", svg);
        Assert.Contains("<title>Poster &amp; &lt;Test&gt;</title>", svg);
        Assert.Contains("fill=\"#101114\"", svg);
        Assert.Contains("<path", svg);
        Assert.Contains("fill=\"#FF0000\"", svg);
        Assert.Contains("fill-opacity=\"0.502\"", svg);
        Assert.Contains("stroke=\"#00FF00\"", svg);
        Assert.Contains("stroke-width=\"3\"", svg);
        Assert.Contains("stroke-linecap=\"round\"", svg);
        Assert.Contains("stroke-linejoin=\"bevel\"", svg);
        Assert.Contains("stroke-dasharray=\"4 2\"", svg);
        Assert.Contains("stroke-dashoffset=\"1\"", svg);
        Assert.Contains("fill-rule=\"nonzero\"", svg);
        Assert.Contains("matrix(", svg);
        Assert.Contains("font-family=\"Georgia\"", svg);
        Assert.Contains("font-size=\"20\"", svg);
        Assert.Contains("font-weight=\"700\"", svg);
        Assert.Contains("font-style=\"italic\"", svg);
        Assert.Contains("A &amp; B &lt; C", svg);
        Assert.Contains("<tspan", svg);
        Assert.Contains("dy=\"25\"", svg);
    }

    [Fact]
    public void HiddenObjectsGroupsAndEvenOddRulesRespectVisibility()
    {
        Document document = new();
        PathObject hidden = V.AddRectToDocument(document, 0, 0, 10, 10, 0xFFFF0000);
        hidden.Visible = false;
        PathObject faded = V.AddRectToDocument(document, 0, 0, 10, 10, 0xFF00FF00);
        faded.Opacity = 0.5f;
        faded.Path.FillRule = FillRule.EvenOdd;
        GroupObject group = new() { Name = "Group", Opacity = 0.25f };
        group.Add(V.AddRectToDocument(document, 0, 0, 10, 10, 0xFF0000FF));
        document.ActiveLayer!.Add(group);

        string svg = SvgExporter.Export(document);

        Assert.DoesNotContain("#FF0000", svg);
        Assert.Contains("opacity=\"0.5\"", svg);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
        Assert.Contains("<g", svg);
        Assert.Contains("opacity=\"0.25\"", svg);
    }

    [Fact]
    public void ExportCanSelectAnotherArtboardAndOffsetsItsOrigin()
    {
        Document document = new(100, 100);
        document.Artboards.Add(new Artboard { Name = "Second", X = 50, Y = 30, Width = 200, Height = 120, Background = 0xFFEEEEEE });
        document.ActiveArtboard = 1;

        string svg = SvgExporter.Export(document);

        Assert.Contains("width=\"200\" height=\"120\"", svg);
        Assert.Contains("translate(-50 -30)", svg);
        Assert.Contains("fill=\"#EEEEEE\"", svg);

        string first = SvgExporter.Export(document, 0);
        Assert.Contains("width=\"100\" height=\"100\"", first);
    }

    [Fact]
    public void TextWithoutFillIsSkipped()
    {
        Document document = new();
        TextObject text = new() { Text = "Invisible", Fill = null };
        document.ActiveLayer!.Add(text);

        string svg = SvgExporter.Export(document);

        Assert.DoesNotContain("Invisible", svg);
    }
}

public sealed class SvgImportTests
{
    [Fact]
    public void ImporterReadsSupportedPrimitivesStylesAndViewBox()
    {
        using TempDirectory temp = new();
        string path = temp.File("drawing.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 100 50">
              <rect x="10" y="10" width="20" height="10" fill="#ff0000" stroke="#0000ff" stroke-width="2"/>
              <circle cx="60" cy="20" r="5" fill="green"/>
              <line x1="0" y1="0" x2="10" y2="10" stroke="black"/>
              <polygon points="0,0 10,0 10,10" fill="#ffffff"/>
              <polyline points="0,0 10,0 10,10" fill="none" stroke="#000000"/>
              <path d="M20 20 L30 20 L30 30 Z" fill="#ff0000" fill-rule="evenodd"/>
              <text x="10" y="40" font-size="12" fill="#123456" font-weight="bold" font-style="italic" text-anchor="middle"><tspan>Hello</tspan></text>
              <g id="faded" transform="translate(5 5)" opacity="0.5">
                <rect x="0" y="0" width="5" height="5" fill="#00ff00"/>
              </g>
              <rect x="0" y="0" width="5" height="5" fill="#123456" display="none"/>
            </svg>
            """);

        Document document = SvgImporter.Load(path);

        Assert.Equal(100f, document.Active.Width);
        Assert.Equal(50f, document.Active.Height);
        Assert.Equal("drawing", document.Name);
        Assert.Equal("drawing", document.Active.Name);
        Assert.Single(document.ActiveLayer!.Children);
        Assert.Equal(1f, document.ActiveLayer.Transform.M11, 3);
        Assert.Equal(1f, document.ActiveLayer.Transform.M22, 3);

        List<PathObject> paths = document.Root.SelfAndDescendants().OfType<PathObject>().ToList();
        Assert.Equal(7, paths.Count);
        PathObject rect = paths.Single(path => path.Name == "Rectangle" && path.Fill!.Color == 0xFFFF0000);
        Assert.Equal(0xFFFF0000u, rect.Fill!.Color);
        Assert.Equal(0xFF0000FFu, rect.Stroke!.Paint.Color);
        Assert.Equal(2f, rect.Stroke.Width);
        Assert.Equal(new Bounds(10, 10, 20, 10), rect.DocumentBounds);

        PathObject circle = paths.Single(path => path.Name == "Circle");
        Assert.Equal(0xFF2E7D32u, circle.Fill!.Color);

        PathObject pathShape = paths.Single(path => path.Name == "Path");
        Assert.Equal(FillRule.EvenOdd, pathShape.Path.FillRule);

        TextObject text = document.Root.SelfAndDescendants().OfType<TextObject>().Single();
        Assert.Equal("Hello", text.Text);
        Assert.True(text.Bold);
        Assert.True(text.Italic);
        Assert.Equal(0xFF123456u, text.Fill!.Color);
        Assert.Equal(12f, text.FontSize);

        GroupObject group = document.Root.SelfAndDescendants().OfType<GroupObject>()
            .First(item => item.Name == "faded");
        Assert.Equal(0.5f, group.Opacity);
        Assert.Single(group.Children);
    }

    [Fact]
    public void UseSymbolsAndTransformsAreExpanded()
    {
        using TempDirectory temp = new();
        string path = temp.File("use.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">
              <defs>
                <g id="shape"><rect x="0" y="0" width="10" height="10" fill="#ff0000"/></g>
              </defs>
              <use href="#shape" x="5" y="6" transform="scale(2)"/>
            </svg>
            """);

        Document document = SvgImporter.Load(path);

        GroupObject use = document.Root.SelfAndDescendants().OfType<GroupObject>()
            .First(item => item.Name == "Use");
        Assert.Single(use.Children);
        Assert.Equal(2f, use.Transform.M11, 3);
        Assert.Equal(10f, use.Transform.M31, 3);
        Assert.Equal(12f, use.Transform.M32, 3);
    }

    [Fact]
    public void InlineStylesAndTransformsAreAppliedToPrimitives()
    {
        using TempDirectory temp = new();
        string path = temp.File("styled.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
              <rect x="1" y="2" width="10" height="20"
                    transform="translate(5 6) scale(2)"
                    style="fill:#112233;stroke:#445566;stroke-width:3;stroke-dasharray:2 1;stroke-linecap:round;stroke-linejoin:bevel;opacity:0.5"/>
              <rect x="0" y="0" width="5" height="5" fill="none" stroke="orange"/>
            </svg>
            """);

        Document document = SvgImporter.Load(path);
        List<PathObject> paths = document.Root.SelfAndDescendants().OfType<PathObject>().ToList();

        Assert.Equal(2, paths.Count);
        PathObject styled = paths[0];
        Assert.Equal(0xFF112233u, styled.Fill!.Color);
        Assert.Equal(0xFF445566u, styled.Stroke!.Paint.Color);
        Assert.Equal(3f, styled.Stroke.Width);
        Assert.Equal([2f, 1f], styled.Stroke.Dash);
        Assert.Equal(LineCap.Round, styled.Stroke.Cap);
        Assert.Equal(LineJoin.Bevel, styled.Stroke.Join);
        Assert.Equal(0.5f, styled.Opacity);
        Assert.Equal(2f, styled.Transform.M11, 3);
        Assert.Equal(5f, styled.Transform.M31, 3);
        Assert.Equal(6f, styled.Transform.M32, 3);

        PathObject unfilled = paths[1];
        Assert.Null(unfilled.Fill);
        Assert.Equal(0xFFFB8C00u, unfilled.Stroke!.Paint.Color);
    }

    [Fact]
    public void MissingSizeFallsBackToTheDefaultArtboard()
    {
        using TempDirectory temp = new();
        string path = temp.File("bare.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg">
              <rect width="10" height="10"/>
            </svg>
            """);

        Document document = SvgImporter.Load(path);

        Assert.Equal(1200f, document.Active.Width);
        Assert.Equal(800f, document.Active.Height);
    }

    [Fact]
    public void SvgzIsDecompressed()
    {
        using TempDirectory temp = new();
        string path = temp.File("zip.svgz");
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="40" height="30">
              <rect width="10" height="10" fill="#ff0000"/>
            </svg>
            """;
        using (FileStream file = File.Create(path))
        using (System.IO.Compression.GZipStream gzip = new(file, System.IO.Compression.CompressionLevel.Fastest))
        using (StreamWriter writer = new(gzip))
        {
            writer.Write(svg);
        }

        Document document = SvgImporter.Load(path);

        Assert.Equal(40f, document.Active.Width);
        Assert.Single(document.Root.SelfAndDescendants().OfType<PathObject>());
    }

    [Fact]
    public void InvalidAndForeignXmlIsRejected()
    {
        using TempDirectory temp = new();

        string invalid = temp.File("invalid.svg");
        File.WriteAllText(invalid, "<svg");
        Assert.Throws<InvalidDataException>(() => SvgImporter.Load(invalid));

        string foreign = temp.File("foreign.svg");
        File.WriteAllText(foreign, "<html><body/></html>");
        Assert.Throws<InvalidDataException>(() => SvgImporter.Load(foreign));

        string empty = temp.File("empty.svg");
        File.WriteAllText(empty, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        Document blank = SvgImporter.Load(empty);
        Assert.Empty(blank.Root.SelfAndDescendants().OfType<PathObject>());
        Assert.Empty(blank.Root.SelfAndDescendants().OfType<TextObject>());
    }
}

public sealed class SystemFontCatalogTests
{
    [Fact]
    public void DiscoveryIncludesFallbacksAndIsSorted()
    {
        IReadOnlyList<string> names = SystemFontCatalog.FamilyNames;

        Assert.NotEmpty(names);
        Assert.Contains("Arial", names);
        Assert.Contains("Segoe UI", names);
        Assert.Equal(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public void ExternalFamiliesAreRegisteredOnce()
    {
        const string family = "VectorDotNet Test Family 7C31";
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vector-test-font.ttf");

        SystemFontCatalog.AddExternal(family, path, 700, true);
        SystemFontCatalog.AddExternal(family, path, 700, true);
        SystemFontCatalog.AddExternal("", path, 400, false);
        SystemFontCatalog.AddExternal(family, "", 400, false);

        Assert.Contains(family, SystemFontCatalog.FamilyNames);
        IReadOnlyList<FontFaceInfo> faces = SystemFontCatalog.FacesFor(family);
        Assert.Single(faces);
        Assert.Equal(700, faces[0].Weight);
        Assert.True(faces[0].Italic);
        Assert.Empty(SystemFontCatalog.FacesFor("Definitely Not A Family"));
    }

    [Fact]
    public void ReadFamilyNameHandlesRealAndBrokenFiles()
    {
        using TempDirectory temp = new();
        string garbage = temp.File("broken.ttf");
        File.WriteAllBytes(garbage, [1, 2, 3, 4]);
        Assert.Null(SystemFontCatalog.ReadFamilyName(garbage));
        Assert.Null(SystemFontCatalog.ReadFamilyName(temp.File("missing.ttf")));

        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string candidate = System.IO.Path.Combine(fonts, "arial.ttf");
        if (!File.Exists(candidate))
        {
            candidate = System.IO.Path.Combine(fonts, "segoeui.ttf");
        }

        if (File.Exists(candidate))
        {
            Assert.False(string.IsNullOrWhiteSpace(SystemFontCatalog.ReadFamilyName(candidate)));
        }
    }
}
