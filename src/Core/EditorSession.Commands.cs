using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>High-level commands used by the menus, panels and options bar.</summary>
public sealed partial class EditorSession
{
    /// <summary>Whether the inspector column is visible.</summary>
    public bool ShowSidebar { get; private set; } = true;

    /// <summary>Whether the shortcut sheet is open.</summary>
    public bool ShowShortcuts { get; private set; }

    /// <summary>Whether the SVG export dialog is open.</summary>
    public bool ShowExport { get; private set; }

    /// <summary>Whether the SVG import dialog is open.</summary>
    public bool ShowImport { get; private set; }

    /// <summary>Source path shown by the import dialog.</summary>
    public string ImportPath { get; private set; } = DefaultImportPath();

    /// <summary>Result message from the last import attempt.</summary>
    public string ImportStatus { get; private set; } = string.Empty;

    /// <summary>Opens the file browser to import an SVG document.</summary>
    public void OpenImportDialog() => OpenFileBrowser(FileBrowserPurpose.ImportSvg);

    /// <summary>Opens the file browser to export the active artboard.</summary>
    public void OpenExportDialog() => OpenFileBrowser(FileBrowserPurpose.ExportSvg);

    /// <summary>Opens the file browser to load a font file.</summary>
    public void OpenFontBrowser() => OpenFileBrowser(FileBrowserPurpose.Font);

    /// <summary>Updates the import source path.</summary>
    public void SetImportPath(string path)
    {
        ImportPath = path;
    }

    /// <summary>Reads the chosen SVG into a new document tab.</summary>
    public void ConfirmImport()
    {
        try
        {
            if (!System.IO.File.Exists(ImportPath))
            {
                ImportStatus = "File not found.";
                Notify();
                return;
            }

            Document imported = SvgImporter.Load(ImportPath);
            ShowImport = false;
            ImportStatus = string.Empty;
            OpenDocument(imported);
            _toolHint = $"Imported {System.IO.Path.GetFileName(ImportPath)}";
            Notify();
        }
        catch (Exception error)
        {
            ImportStatus = "Could not import: " + error.Message;
            Notify();
        }
    }

    private static string DefaultImportPath()
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return System.IO.Path.Combine(folder, "drawing.svg");
    }

    /// <summary>Adds an externally created document as a new tab.</summary>
    public void OpenDocument(Document document)
    {
        _documentCounter++;
        if (string.IsNullOrWhiteSpace(document.Name))
        {
            document.Name = $"Untitled-{_documentCounter}";
        }

        OpenTab(document);
    }

    /// <summary>Destination path shown by the export dialog.</summary>
    public string ExportPath { get; private set; } = DefaultExportPath();

    /// <summary>Result message from the last export attempt.</summary>
    public string ExportStatus { get; private set; } = string.Empty;

    /// <summary>Updates the export destination.</summary>
    public void SetExportPath(string path)
    {
        ExportPath = path;
    }

    /// <summary>Writes the active artboard to the chosen SVG file.</summary>
    public void ConfirmExport()
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(ExportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string path = ExportPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                || ExportPath.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase)
                ? ExportPath
                : ExportPath + ".svg";
            string svg = SvgExporter.Export(Document);
            if (path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase))
            {
                using System.IO.FileStream file = System.IO.File.Create(path);
                using System.IO.Compression.GZipStream gzip = new(file, System.IO.Compression.CompressionMode.Compress);
                using System.IO.StreamWriter writer = new(gzip);
                writer.Write(svg);
            }
            else
            {
                System.IO.File.WriteAllText(path, svg);
            }

            ExportPath = path;
            ExportStatus = $"Saved {System.IO.Path.GetFileName(path)}";
            _toolHint = ExportStatus;
        }
        catch (Exception error)
        {
            ExportStatus = "Could not save: " + error.Message;
        }

        Notify();
    }

    private static string DefaultExportPath()
    {
        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VectorDotNet");
        return System.IO.Path.Combine(folder, "VectorDotNet.svg");
    }

    /// <summary>Default corner radius for new rounded rectangles.</summary>
    public float DefaultCornerRadius { get; set; } = 14f;

    /// <summary>Default side or point count for new polygons and stars.</summary>
    public int DefaultPoints { get; set; } = 5;

    /// <summary>Default star inner ratio.</summary>
    public float DefaultInnerRatio { get; set; } = 0.5f;

    /// <summary>Shows or hides the inspector.</summary>
    public void ToggleSidebar()
    {
        ShowSidebar = !ShowSidebar;
        Notify();
    }

    /// <summary>Opens or closes a top-level menu.</summary>
    public void ToggleMenu(string title)
    {
        OpenMenu = OpenMenu == title ? null : title;
        Notify();
    }

    /// <summary>Left position of the open drop-down, in window coordinates.</summary>
    public float MenuX { get; private set; }

    /// <summary>Top position of the open drop-down, in window coordinates.</summary>
    public float MenuY { get; private set; } = 28f;

    /// <summary>Opens a menu underneath a title, or closes it when already open.</summary>
    public void ToggleMenuAt(string title, float x, float y)
    {
        if (OpenMenu == title)
        {
            OpenMenu = null;
            Notify();
            return;
        }

        OpenMenu = title;
        MenuX = x;
        MenuY = y;
        Notify();
    }

    /// <summary>Switches the open menu when the pointer sweeps the title bar.</summary>
    public void HoverMenuAt(string title, float x, float y)
    {
        if (OpenMenu is null)
        {
            return;
        }

        MenuX = x;
        MenuY = y;
        if (OpenMenu != title)
        {
            OpenMenu = title;
        }

        Notify();
    }

    /// <summary>Closes the open menu.</summary>
    public void CloseMenu()
    {
        if (OpenMenu is null)
        {
            return;
        }

        OpenMenu = null;
        Notify();
    }

    /// <summary>Switches the open menu while the pointer sweeps the bar.</summary>
    public void HoverMenu(string title)
    {
        if (OpenMenu is null || OpenMenu == title)
        {
            return;
        }

        OpenMenu = title;
        Notify();
    }

    /// <summary>Opens the shortcut sheet.</summary>
    public void OpenShortcuts()
    {
        ShowShortcuts = true;
        OpenMenu = null;
        Notify();
    }

    /// <summary>Closes the shortcut sheet.</summary>
    public void CloseShortcuts()
    {
        ShowShortcuts = false;
        Notify();
    }

    /// <summary>Toggles the grid overlay.</summary>
    public void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
        Notify();
    }

    /// <summary>Toggles the ruler bands.</summary>
    public void ToggleRulers()
    {
        ShowRulers = !ShowRulers;
        Touch();
        Notify();
    }

    /// <summary>Toggles guide visibility.</summary>
    public void ToggleGuides()
    {
        ShowGuides = !ShowGuides;
        Notify();
    }

    /// <summary>Toggles grid snapping.</summary>
    public void ToggleGridSnapping()
    {
        SnapToGrid = !SnapToGrid;
        Notify();
    }

    /// <summary>Toggles object snapping.</summary>
    public void ToggleObjectSnapping()
    {
        SnapToObjects = !SnapToObjects;
        Notify();
    }

    /// <summary>Toggles smart alignment guides.</summary>
    public void ToggleSmartGuides()
    {
        SmartGuides = !SmartGuides;
        Notify();
    }

    /// <summary>Deletes the active layer when another remains.</summary>
    public void DeleteActiveLayer()
    {
        LayerObject? layer = Document.ActiveLayer;
        if (layer is null)
        {
            return;
        }

        DeleteLayer(layer);
    }

    /// <summary>Resizes the active artboard.</summary>
    public void SetArtboardSize(float width, float height)
    {
        Artboard artboard = Document.Active;
        float w = Math.Clamp(width, 16f, 20000f);
        float h = Math.Clamp(height, 16f, 20000f);
        if (MathF.Abs(artboard.Width - w) < 0.01f && MathF.Abs(artboard.Height - h) < 0.01f)
        {
            return;
        }

        PushHistory("Artboard Size");
        artboard.Width = w;
        artboard.Height = h;
        FitArtboard();
        Notify();
    }

    /// <summary>Sets the artboard background colour.</summary>
    public void SetArtboardBackground(uint color)
    {
        Artboard artboard = Document.Active;
        if (artboard.Background == color)
        {
            return;
        }

        PushHistory("Artboard Background");
        artboard.Background = color;
        RememberColour(color);
        Notify();
    }

    /// <summary>Changes the size of selected text by a delta in points.</summary>
    public void NudgeTextSize(float delta)
    {
        List<TextObject> items = Selection.OfType<TextObject>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        PushHistory("Text Size");
        foreach (TextObject item in items)
        {
            item.FontSize = Math.Clamp(item.FontSize + delta, 4f, 512f);
        }

        Notify();
    }

    /// <summary>Sets the side count of selected polygons and stars.</summary>
    public void SetSelectionPoints(int points)
    {
        DefaultPoints = Math.Clamp(points, 3, 64);
        List<PathObject> shapes = Selection.OfType<PathObject>()
            .Where(path => path.Shape?.Kind is ShapeKind.Polygon or ShapeKind.Star)
            .ToList();
        if (shapes.Count == 0)
        {
            Notify();
            return;
        }

        PushHistory("Points");
        foreach (PathObject path in shapes)
        {
            path.Shape!.Points = DefaultPoints;
            path.Rebuild();
        }

        Notify();
    }

    /// <summary>Toggles italic on selected text objects.</summary>
    public void ToggleTextItalic()
    {
        List<TextObject> items = Selection.OfType<TextObject>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        PushHistory("Italic");
        bool value = !items[0].Italic;
        foreach (TextObject item in items)
        {
            item.Italic = value;
        }

        Notify();
    }

    /// <summary>Sets the inner radius of selected stars.</summary>
    public void SetSelectionInnerRatio(float ratio)
    {
        DefaultInnerRatio = Math.Clamp(ratio, 0.05f, 0.95f);
        List<PathObject> shapes = Selection.OfType<PathObject>()
            .Where(path => path.Shape?.Kind == ShapeKind.Star)
            .ToList();
        if (shapes.Count == 0)
        {
            Notify();
            return;
        }

        PushHistory("Star Radius");
        foreach (PathObject path in shapes)
        {
            path.Shape!.InnerRatio = DefaultInnerRatio;
            path.Rebuild();
        }

        Notify();
    }

    /// <summary>Toggles an object's visibility.</summary>
    public void ToggleVisibility(SceneObject item)
    {
        PushHistory("Visibility");
        item.Visible = !item.Visible;
        Notify();
    }

    /// <summary>Toggles an object's lock state.</summary>
    public void ToggleLock(SceneObject item)
    {
        PushHistory("Lock");
        item.Locked = !item.Locked;
        if (item.Locked)
        {
            Selection.Remove(item);
        }

        Notify();
    }

    /// <summary>Makes a layer active for new objects.</summary>
    public void SetActiveLayer(LayerObject layer)
    {
        Document.ActiveLayer = layer;
        _toolHint = $"{layer.DisplayName} is active";
        Notify();
    }

    /// <summary>Adds a new artboard beside the existing ones.</summary>
    public void AddArtboard()
    {
        PushHistory("New Artboard");
        float right = Document.Artboards.Count == 0 ? 0f : Document.Artboards.Max(item => item.X + item.Width);
        Artboard artboard = new()
        {
            Name = $"Artboard {Document.Artboards.Count + 1}",
            X = right + 120f,
            Y = 0f,
            Width = Document.Active.Width,
            Height = Document.Active.Height,
        };
        Document.Artboards.Add(artboard);
        Document.ActiveArtboard = Document.Artboards.Count - 1;
        FitArtboard();
        Notify();
    }

    /// <summary>Makes an artboard active.</summary>
    public void SelectArtboard(int index)
    {
        if (index < 0 || index >= Document.Artboards.Count)
        {
            return;
        }

        Document.ActiveArtboard = index;
        FitArtboard();
        Notify();
    }

    /// <summary>Deletes an artboard when more than one remains.</summary>
    public void DeleteArtboard(int index)
    {
        if (Document.Artboards.Count <= 1 || index < 0 || index >= Document.Artboards.Count)
        {
            return;
        }

        PushHistory("Delete Artboard");
        Document.Artboards.RemoveAt(index);
        Document.ActiveArtboard = Math.Clamp(Document.ActiveArtboard, 0, Document.Artboards.Count - 1);
        FitArtboard();
        Notify();
    }

    /// <summary>Duplicates an artboard.</summary>
    public void DuplicateArtboard(int index)
    {
        if (index < 0 || index >= Document.Artboards.Count)
        {
            return;
        }

        PushHistory("Duplicate Artboard");
        Artboard source = Document.Artboards[index];
        Artboard copy = source.Clone();
        copy.Name = $"{source.Name} copy";
        copy.X += source.Width + 120f;
        Document.Artboards.Add(copy);
        Document.ActiveArtboard = Document.Artboards.Count - 1;
        FitArtboard();
        Notify();
    }

    /// <summary>Loads a small sample illustration that exercises the engine.</summary>
    public void LoadSample()
    {
        Document sample = new(1200, 800) { Name = "Sample Poster" };
        LayerObject layer = sample.Root.Children[0] as LayerObject ?? new LayerObject();
        layer.Name = "Illustration";
        sample.ActiveLayer = layer;

        // Paper circle.
        AddPath(layer, new ShapeSpec { Kind = ShapeKind.Ellipse, Width = 620f, Height = 620f }, new Vec2(480f, 470f), 0f, 0xFFE9EFFB, null);

        // Amber star.
        AddPath(layer, new ShapeSpec
        {
            Kind = ShapeKind.Star,
            Width = 210f,
            Height = 210f,
            Points = 7,
            InnerRatio = 0.46f,
            Rotation = 8f,
        }, new Vec2(930f, 250f), 0f, 0xFFF5A524, null);

        // Blue panel.
        AddPath(layer, new ShapeSpec
        {
            Kind = ShapeKind.RoundedRectangle,
            Width = 360f,
            Height = 210f,
            CornerRadius = 34f,
        }, new Vec2(790f, 500f), -7f, 0xFF4C8DFF, null);

        // Ink triangle.
        AddPath(layer, new ShapeSpec
        {
            Kind = ShapeKind.Polygon,
            Width = 250f,
            Height = 250f,
            Points = 3,
            Rotation = -12f,
        }, new Vec2(430f, 500f), 0f, 0xFF1B1E24, null);

        // Outline ring.
        PathObject ring = AddPath(layer, new ShapeSpec { Kind = ShapeKind.Ellipse, Width = 260f, Height = 260f }, new Vec2(600f, 470f), 0f, null, null);
        ring.Stroke = new StrokeStyle
        {
            Paint = Paint.Solid(0xFFF5A524),
            Width = 14f,
            Join = LineJoin.Round,
        };

        // A few rule lines.
        for (int i = 0; i < 4; i++)
        {
            PathObject rule = AddPath(
                layer,
                new ShapeSpec { Kind = ShapeKind.Line, Width = 120f - (i * 18f), Height = 0f },
                new Vec2(200f + (i * 26f), 660f + (i * 16f)),
                0f,
                null,
                null);
            rule.Stroke = new StrokeStyle
            {
                Paint = Paint.Solid(0xFF8B94A1),
                Width = 3f,
                Cap = LineCap.Round,
            };
        }

        layer.Add(new TextObject
        {
            Text = "VectorDotNet",
            FontFamily = "Segoe UI",
            FontSize = 76f,
            Bold = true,
            Fill = Paint.Solid(0xFF14161A),
            Transform = Mat.Translate(130f, 110f),
        });

        layer.Add(new TextObject
        {
            Text = "Design without limits.",
            FontFamily = "Segoe UI",
            FontSize = 28f,
            Fill = Paint.Solid(0xFF6F7885),
            Transform = Mat.Translate(136f, 208f),
        });

        layer.Add(new TextObject
        {
            Text = "A free, open-source vector studio.",
            FontFamily = "Segoe UI",
            FontSize = 18f,
            Fill = Paint.Solid(0xFF8B94A1),
            Transform = Mat.Translate(138f, 700f),
        });

        OpenTab(sample);
        _toolHint = "Sample loaded";
        Notify();
    }

    private static PathObject AddPath(
        LayerObject layer,
        ShapeSpec spec,
        Vec2 center,
        float rotationDegrees,
        uint? fill,
        uint? stroke)
    {
        PathObject created = new()
        {
            Name = spec.DisplayName,
            Shape = spec,
            Fill = fill.HasValue ? Paint.Solid(fill.Value) : null,
            Stroke = stroke.HasValue
                ? new StrokeStyle { Paint = Paint.Solid(stroke.Value), Width = 2f }
                : null,
            Transform = Mat.Translate(center) * Mat.Rotate(rotationDegrees * MathF.PI / 180f),
        };
        created.Rebuild();
        layer.Add(created);
        return created;
    }
}
