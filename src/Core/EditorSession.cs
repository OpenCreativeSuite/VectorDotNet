using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>The editing tools available in the rail.</summary>
public enum ToolKind
{
    /// <summary>Select and transform whole objects.</summary>
    Select,

    /// <summary>Select and drag path nodes.</summary>
    Direct,

    /// <summary>Draw cubic paths point by point.</summary>
    Pen,

    /// <summary>Draw freehand smoothed paths.</summary>
    Pencil,

    /// <summary>Draw a rectangle.</summary>
    Rectangle,

    /// <summary>Draw a rounded rectangle.</summary>
    RoundedRectangle,

    /// <summary>Draw an ellipse.</summary>
    Ellipse,

    /// <summary>Draw a regular polygon.</summary>
    Polygon,

    /// <summary>Draw a star.</summary>
    Star,

    /// <summary>Draw a straight line.</summary>
    Line,

    /// <summary>Draw a spiral.</summary>
    Spiral,

    /// <summary>Create point text.</summary>
    Text,

    /// <summary>Select with a freehand loop.</summary>
    Lasso,

    /// <summary>Click to zoom in, alt-click to zoom out.</summary>
    Zoom,

    /// <summary>Drag to pan the canvas.</summary>
    Hand,
}

/// <summary>What the current pointer drag is doing.</summary>
public enum DragKind
{
    /// <summary>No interaction.</summary>
    None,

    /// <summary>Moving the selection.</summary>
    Move,

    /// <summary>Scaling the selection from a handle.</summary>
    Scale,

    /// <summary>Rubber-band selection.</summary>
    Marquee,

    /// <summary>Freehand lasso selection.</summary>
    Lasso,

    /// <summary>Creating a parametric shape.</summary>
    Create,

    /// <summary>Freehand pencil stroke.</summary>
    Pencil,

    /// <summary>Dragging a pen handle while placing an anchor.</summary>
    PenHandle,

    /// <summary>Dragging a path node.</summary>
    Node,

    /// <summary>Dragging a Bézier handle.</summary>
    Handle,

    /// <summary>Panning the view.</summary>
    Pan,
}

/// <summary>One undoable snapshot of the document and selection.</summary>
internal sealed record HistoryEntry(Document Document, Guid[] Selection, int ActiveArtboard);

/// <summary>
/// The editor model: document, selection, view transform, tools, interaction
/// state and undo history. The UI reads it; the canvas surface drives it.
/// </summary>
public sealed partial class EditorSession
{
    private static readonly float[] ZoomLevels =
    [
        0.05f, 0.0625f, 0.0833f, 0.125f, 0.1667f, 0.25f, 0.3333f, 0.5f,
        0.6667f, 0.75f, 1f, 1.25f, 1.5f, 2f, 3f, 4f, 6f, 8f, 12f, 16f, 24f, 32f, 48f, 64f,
    ];

    private readonly List<DocumentTab> _documents = [];
    private int _activeDocument;
    private int _documentCounter = 1;
    private Stack<HistoryEntry> _undo = new();
    private Stack<HistoryEntry> _redo = new();
    private readonly List<SceneObject> _clipboard = [];
    private readonly Dictionary<Guid, Matrix3x2> _startTransforms = [];
    private HistoryEntry? _opacitySnapshot;

    /// <summary>One open document with its own history and view state.</summary>
    private sealed class DocumentTab
    {
        public Document Document = new();

        public Stack<HistoryEntry> Undo { get; } = new();

        public Stack<HistoryEntry> Redo { get; } = new();

        public float Zoom = 1f;

        public float PanX;

        public float PanY;

        public bool ViewInitialised;

        public Guid[] Selection = [];
    }

    private DragKind _drag;
    private Vec2 _dragStartDocument;
    private Vec2 _dragLastDocument;
    private Bounds _dragStartBounds;
    private int _scaleHandle = -1;
    private bool _dragMoved;
    private PathObject? _created;
    private Vec2 _createAnchor;
    private bool _createFromCenter;
    private PathObject? _editTarget;
    private (int Sub, int Node, bool Out)? _handleDrag;
    private (int Sub, int Node)? _nodeDrag;
    private string _toolHint = "Ready";
    private bool _viewInitialised;
    private float _pencilDistance;
    private long _lastZoomUiTick;

    /// <summary>Creates a session with a blank document.</summary>
    public EditorSession()
    {
        _documents.Add(new DocumentTab { Document = new Document(1200, 800) { Name = "Untitled-1" } });
        _undo = _documents[0].Undo;
        _redo = _documents[0].Redo;
        FillColor = 0xFF4C8DFF;
        StrokeColor = 0xFF1B1D22;
        RecentColours.Add(FillColor);
        RecentColours.Add(0xFFF2F4F7);
        RecentColours.Add(0xFF1B1D22);
    }

    private DocumentTab ActiveTab => _documents[_activeDocument];

    /// <summary>Raised when the component tree should re-render.</summary>
    public Action? Refresh { get; set; }

    /// <summary>The document being edited.</summary>
    public Document Document
    {
        get => ActiveTab.Document;
        private set => ActiveTab.Document = value;
    }

    /// <summary>The number of open document tabs.</summary>
    public int DocumentCount => _documents.Count;

    /// <summary>Index of the visible document tab.</summary>
    public int ActiveDocumentIndex => _activeDocument;

    /// <summary>The open documents in tab order.</summary>
    public IReadOnlyList<Document> OpenDocuments => _documents.Select(tab => tab.Document).ToList();

    /// <summary>Selected top-level objects.</summary>
    public List<SceneObject> Selection { get; } = [];

    /// <summary>The active tool.</summary>
    public ToolKind Tool { get; private set; } = ToolKind.Select;

    /// <summary>Current zoom factor.</summary>
    public float Zoom { get; private set; } = 1f;

    /// <summary>Document-origin X in canvas pixels, including the ruler band.</summary>
    public float PanX { get; private set; }

    /// <summary>Document-origin Y in canvas pixels, including the ruler band.</summary>
    public float PanY { get; private set; }

    /// <summary>Canvas viewport width in pixels, updated by the surface.</summary>
    public float ViewWidth { get; private set; }

    /// <summary>Canvas viewport height in pixels.</summary>
    public float ViewHeight { get; private set; }

    /// <summary>Monotonic version used by the render cache.</summary>
    public int Revision { get; private set; }

    /// <summary>Whether the grid overlay is shown.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Grid spacing in document units.</summary>
    public float GridSize { get; set; } = 20f;

    /// <summary>Whether rulers are shown.</summary>
    public bool ShowRulers { get; set; } = true;

    /// <summary>Whether guides are shown.</summary>
    public bool ShowGuides { get; set; } = true;

    /// <summary>Whether dragging snaps to the grid.</summary>
    public bool SnapToGrid { get; set; }

    /// <summary>Whether dragging snaps to other objects.</summary>
    public bool SnapToObjects { get; set; } = true;

    /// <summary>Whether smart alignment guides are shown.</summary>
    public bool SmartGuides { get; set; } = true;

    /// <summary>The fill colour used by new objects.</summary>
    public uint FillColor { get; set; }

    /// <summary>The stroke colour used by new objects.</summary>
    public uint StrokeColor { get; set; }

    /// <summary>The current default stroke width.</summary>
    public float StrokeWidth { get; set; } = 1f;

    /// <summary>Recent colours, most recent first.</summary>
    public List<uint> RecentColours { get; } = [];

    /// <summary>The document-space cursor position.</summary>
    public Vec2 CursorDocument { get; private set; }

    /// <summary>Whether the pointer is over the artwork area.</summary>
    public bool CursorOnCanvas { get; private set; }

    /// <summary>Currently open menu title, or null.</summary>
    public string? OpenMenu { get; set; }

    /// <summary>True while a text field owns the keyboard.</summary>
    public bool IsTyping { get; set; }

    /// <summary>Recent status line text.</summary>
    public string Status => _toolHint;

    /// <summary>Whether undo is available.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether redo is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Width of the ruler bands in pixels.</summary>
    public float RulerSize => ShowRulers ? 22f : 0f;

    /// <summary>The drawable area inside the rulers.</summary>
    public Bounds CanvasClip => new(RulerSize, RulerSize, MathF.Max(1f, ViewWidth - RulerSize), MathF.Max(1f, ViewHeight - RulerSize));

    /// <summary>The in-progress interaction.</summary>
    public DragKind Drag => _drag;

    /// <summary>Rubber-band rectangle in document space while dragging.</summary>
    public Bounds? Marquee
    {
        get
        {
            if (_drag != DragKind.Marquee)
            {
                return null;
            }

            return Bounds.FromPoints(_dragStartDocument, _dragLastDocument).Normalized();
        }
    }

    /// <summary>Lasso points in document space.</summary>
    public IReadOnlyList<Vec2> LassoPoints => _lasso;

    /// <summary>Pen anchors placed so far, in document space.</summary>
    public IReadOnlyList<PathNode> PenNodes => _penNodes;

    /// <summary>Preview point for the pen's next segment.</summary>
    public Vec2 PenPreview => _penPreview;

    /// <summary>Whether the pen tool has an unfinished path.</summary>
    public bool IsPenActive => _penNodes.Count > 0;

    /// <summary>The path currently in node-editing mode.</summary>
    public PathObject? EditTarget => _editTarget;

    /// <summary>The node selected for direct editing.</summary>
    public (int Sub, int Node)? SelectedNode => _selectedNode;

    /// <summary>Union bounds of the selection in document space.</summary>
    public Bounds SelectionBounds => Document.SelectionBounds(Selection);

    /// <summary>Name of the active tool.</summary>
    public string ToolName => Tool switch
    {
        ToolKind.Select => "Selection",
        ToolKind.Direct => "Direct Selection",
        ToolKind.Pen => "Pen",
        ToolKind.Pencil => "Pencil",
        ToolKind.Rectangle => "Rectangle",
        ToolKind.RoundedRectangle => "Rounded Rectangle",
        ToolKind.Ellipse => "Ellipse",
        ToolKind.Polygon => "Polygon",
        ToolKind.Star => "Star",
        ToolKind.Line => "Line",
        ToolKind.Spiral => "Spiral",
        ToolKind.Text => "Text",
        ToolKind.Lasso => "Lasso",
        ToolKind.Zoom => "Zoom",
        _ => "Hand",
    };

    /// <summary>The active layer, creating one when the document has none.</summary>
    public LayerObject ActiveLayer
    {
        get
        {
            if (Document.ActiveLayer is null)
            {
                LayerObject layer = new() { Name = "Layer 1" };
                Document.Root.Add(layer);
                Document.ActiveLayer = layer;
            }

            return Document.ActiveLayer;
        }
    }

    private readonly List<PathNode> _penNodes = [];
    private Vec2 _penPreview;
    private readonly List<Vec2> _lasso = [];
    private (int Sub, int Node)? _selectedNode;

    /// <summary>Marks the canvas dirty without rebuilding the component tree.</summary>
    public void Touch() => Revision++;

    /// <summary>Marks the canvas dirty and asks the UI to re-render.</summary>
    public void Notify()
    {
        Revision++;
        Refresh?.Invoke();
    }

    /// <summary>Sets the status line.</summary>
    public void SetStatus(string message)
    {
        _toolHint = message;
        Notify();
    }

    /// <summary>Selects a tool and clears transient editing state.</summary>
    public void SetTool(ToolKind tool)
    {
        if (Tool == tool)
        {
            return;
        }

        CancelPen();
        EndNodeEdit();
        Tool = tool;
        _toolHint = tool switch
        {
            ToolKind.Select => "Click to select, drag to move, drag a handle to scale.",
            ToolKind.Direct => "Click a path to edit its nodes.",
            ToolKind.Pen => "Click to add corners, drag for curves, click the first node to close.",
            ToolKind.Pencil => "Drag to draw a smoothed freehand path.",
            ToolKind.Rectangle => "Drag to draw a rectangle. Shift keeps it square.",
            ToolKind.RoundedRectangle => "Drag to draw a rounded rectangle.",
            ToolKind.Ellipse => "Drag to draw an ellipse. Shift keeps it circular.",
            ToolKind.Polygon => "Drag to draw a polygon.",
            ToolKind.Star => "Drag to draw a star.",
            ToolKind.Line => "Drag to draw a line. Shift constrains the angle.",
            ToolKind.Spiral => "Drag to draw a spiral.",
            ToolKind.Text => "Click to place text, then edit it in the properties panel.",
            ToolKind.Lasso => "Drag a loop around the objects you want.",
            ToolKind.Zoom => "Click to zoom in, alt-click to zoom out.",
            _ => "Drag to pan the canvas.",
        };
        Notify();
    }

    /// <summary>Opens a blank document in a new tab, keeping the current one.</summary>
    public void NewDocument(float width, float height)
    {
        _documentCounter++;
        Document document = new(width, height) { Name = $"Untitled-{_documentCounter}" };
        OpenTab(document);
    }

    /// <summary>Adds a document tab and makes it active.</summary>
    private void OpenTab(Document document)
    {
        SaveActiveView();
        _documents.Add(new DocumentTab { Document = document });
        _activeDocument = _documents.Count - 1;
        _undo = ActiveTab.Undo;
        _redo = ActiveTab.Redo;
        Selection.Clear();
        ResetTransientEditing();
        Zoom = 1f;
        PanX = 0f;
        PanY = 0f;
        _viewInitialised = false;
        if (ViewWidth > 2f)
        {
            _viewInitialised = true;
            FitArtboardSilently();
        }

        _toolHint = document.Name;
        Notify();
    }

    /// <summary>Makes another open document active.</summary>
    public void SwitchDocument(int index)
    {
        if (index < 0 || index >= _documents.Count || index == _activeDocument)
        {
            return;
        }

        SaveActiveView();
        _activeDocument = index;
        LoadActiveTab();
        _toolHint = Document.Name;
        Notify();
    }

    /// <summary>Closes an open document tab.</summary>
    public void CloseDocument(int index)
    {
        if (index < 0 || index >= _documents.Count)
        {
            return;
        }

        SaveActiveView();
        _documents.RemoveAt(index);
        if (_documents.Count == 0)
        {
            _documentCounter = 1;
            _documents.Add(new DocumentTab { Document = new Document(1200, 800) { Name = "Untitled-1" } });
            _activeDocument = 0;
        }
        else
        {
            if (index < _activeDocument)
            {
                _activeDocument--;
            }

            _activeDocument = Math.Clamp(_activeDocument, 0, _documents.Count - 1);
        }

        LoadActiveTab();
        _toolHint = "Document closed";
        Notify();
    }

    /// <summary>Stores the active document's view and selection for a tab switch.</summary>
    private void SaveActiveView()
    {
        DocumentTab tab = ActiveTab;
        tab.Zoom = Zoom;
        tab.PanX = PanX;
        tab.PanY = PanY;
        tab.ViewInitialised = _viewInitialised;
        tab.Selection = Selection.Select(item => item.Id).ToArray();
    }

    /// <summary>Restores the active tab's history, view and selection.</summary>
    private void LoadActiveTab()
    {
        DocumentTab tab = ActiveTab;
        _undo = tab.Undo;
        _redo = tab.Redo;
        Zoom = tab.Zoom;
        PanX = tab.PanX;
        PanY = tab.PanY;
        _viewInitialised = tab.ViewInitialised;

        ResetTransientEditing();
        Selection.Clear();
        foreach (Guid id in tab.Selection)
        {
            SceneObject? item = Document.Find(id);
            if (item is not null)
            {
                Selection.Add(item);
            }
        }

        if (!_viewInitialised && ViewWidth > 2f)
        {
            _viewInitialised = true;
            FitArtboardSilently();
        }
    }

    private void ResetTransientEditing()
    {
        _editTarget = null;
        _selectedNode = null;
        _nodeDrag = null;
        _handleDrag = null;
        _drag = DragKind.None;
        _penNodes.Clear();
        _penPreview = Vec2.Zero;
        _lasso.Clear();
        _pencilPoints.Clear();
        _pending = null;
        _opacitySnapshot = null;
    }

    /// <summary>Registers the canvas size and fits the artboard the first time.</summary>
    public void SetViewSize(float width, float height)
    {
        if (width < 2f || height < 2f)
        {
            return;
        }

        bool changed = MathF.Abs(width - ViewWidth) > 0.5f || MathF.Abs(height - ViewHeight) > 0.5f;
        ViewWidth = width;
        ViewHeight = height;

        if (!_viewInitialised)
        {
            _viewInitialised = true;
            FitArtboardSilently();
            Touch();
            return;
        }

        if (changed)
        {
            Touch();
        }
    }

    /// <summary>Converts a canvas-local point to document coordinates.</summary>
    public Vec2 ScreenToDocument(Vec2 screen) => new((screen.X - PanX) / Zoom, (screen.Y - PanY) / Zoom);

    /// <summary>Converts a document point to canvas-local coordinates.</summary>
    public Vec2 DocumentToScreen(Vec2 document) => new((document.X * Zoom) + PanX, (document.Y * Zoom) + PanY);

    /// <summary>Zooms while keeping one screen point pinned.</summary>
    public void ZoomAt(Vec2 screenAnchor, float zoom) => ApplyZoom(screenAnchor, zoom, true);

    private void ApplyZoom(Vec2 screenAnchor, float zoom, bool notify)
    {
        float next = Math.Clamp(zoom, ZoomLevels[0], ZoomLevels[^1]);
        Vec2 document = ScreenToDocument(screenAnchor);
        Zoom = next;
        PanX = screenAnchor.X - (document.X * next);
        PanY = screenAnchor.Y - (document.Y * next);
        if (notify)
        {
            Notify();
        }
        else
        {
            Touch();
        }
    }

    /// <summary>Moves to the next larger zoom level.</summary>
    public void ZoomIn()
    {
        float next = ZoomLevels.FirstOrDefault(level => level > Zoom + 0.0001f, ZoomLevels[^1]);
        ZoomAt(ViewportCenter, next);
    }

    /// <summary>Moves to the next smaller zoom level.</summary>
    public void ZoomOut()
    {
        float next = ZoomLevels.LastOrDefault(level => level < Zoom - 0.0001f, ZoomLevels[0]);
        ZoomAt(ViewportCenter, next);
    }

    /// <summary>Returns to 100%.</summary>
    public void ActualPixels() => ZoomAt(ViewportCenter, 1f);

    /// <summary>Centres the active artboard in the canvas area.</summary>
    public void FitArtboard()
    {
        FitArtboardSilently();
        _toolHint = $"Fit artboard at {Zoom * 100f:0}%";
        Notify();
    }

    private void FitArtboardSilently()
    {
        if (ViewWidth < 2f || ViewHeight < 2f)
        {
            return;
        }

        Bounds clip = CanvasClip;
        Artboard artboard = Document.Active;
        float margin = 64f;
        float availableWidth = MathF.Max(64f, clip.Width - margin);
        float availableHeight = MathF.Max(64f, clip.Height - margin);
        float zoom = MathF.Min(availableWidth / MathF.Max(artboard.Width, 1f), availableHeight / MathF.Max(artboard.Height, 1f));
        Zoom = Math.Clamp(zoom, ZoomLevels[0], ZoomLevels[^1]);
        PanX = clip.X + ((clip.Width - (artboard.Width * Zoom)) * 0.5f) - (artboard.X * Zoom);
        PanY = clip.Y + ((clip.Height - (artboard.Height * Zoom)) * 0.5f) - (artboard.Y * Zoom);
        Touch();
    }

    /// <summary>Zooms so the selection fills the canvas.</summary>
    public void ZoomToSelection()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        Bounds bounds = SelectionBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        Bounds clip = CanvasClip;
        float zoom = MathF.Min(
            (clip.Width - 120f) / MathF.Max(bounds.Width, 1f),
            (clip.Height - 120f) / MathF.Max(bounds.Height, 1f));
        Zoom = Math.Clamp(zoom, ZoomLevels[0], ZoomLevels[^1]);
        PanX = clip.X + (clip.Width * 0.5f) - (bounds.Center.X * Zoom);
        PanY = clip.Y + (clip.Height * 0.5f) - (bounds.Center.Y * Zoom);
        Notify();
    }

    /// <summary>Zooms with a wheel delta around the pointer.</summary>
    public void ZoomByWheel(float delta, Vec2 screenAnchor)
    {
        if (MathF.Abs(delta) < 0.001f)
        {
            return;
        }

        // Hosts report wheel distance in CSS pixels, while some report only the
        // raw direction (±1). Normalising both to "notches" keeps the feel the
        // same across platforms, then the sensitivity multiplier lets the wheel
        // travel as far as the user expects.
        float notches = MathF.Abs(delta) < 10f ? MathF.Sign(delta) : delta / 120f;
        float factor = MathF.Pow(1.16f, notches * ZoomScrollMultiplier);

        // Panning and zooming repaint the canvas every frame. Rebuilding the
        // component tree on every wheel tick makes zooming crawl, so only the
        // canvas is invalidated and the inspector text catches up a few times
        // per second.
        bool refreshUi = Environment.TickCount64 - _lastZoomUiTick > 90;
        if (refreshUi)
        {
            _lastZoomUiTick = Environment.TickCount64;
        }

        ApplyZoom(screenAnchor, Zoom * factor, refreshUi);
    }

    /// <summary>Multiplier applied to wheel zoom travel.</summary>
    public float ZoomScrollMultiplier { get; set; } = 1.45f;

    /// <summary>Pans by a screen-space delta.</summary>
    public void PanBy(float dx, float dy)
    {
        PanX += dx;
        PanY += dy;
        Touch();
    }

    private Vec2 ViewportCenter => new(CanvasClip.Center.X, CanvasClip.Center.Y);

    /// <summary>Records the current state so the next mutation can be undone.</summary>
    public void PushHistory(string label)
    {
        _pending = null;
        _undo.Push(Capture());
        _redo.Clear();
        while (_undo.Count > 100)
        {
            // Drop the oldest entry: Stack has no removal from the bottom, so
            // rebuilding the stack is the cheapest reliable option at this size.
            HistoryEntry[] entries = _undo.ToArray();
            _undo.Clear();
            for (int i = entries.Length - 2; i >= 0; i--)
            {
                _undo.Push(entries[i]);
            }
        }

        _ = label;
    }

    private HistoryEntry Capture() => new(
        Document.Clone(true),
        Selection.Select(item => item.Id).ToArray(),
        Document.ActiveArtboard);

    /// <summary>Undoes the last change.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Capture());
        Restore(_undo.Pop());
        _toolHint = "Undo";
        Notify();
    }

    /// <summary>Redoes the last undone change.</summary>
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Capture());
        Restore(_redo.Pop());
        _toolHint = "Redo";
        Notify();
    }

    private void Restore(HistoryEntry entry)
    {
        Document = entry.Document;
        Document.ActiveArtboard = entry.ActiveArtboard;
        Selection.Clear();
        foreach (Guid id in entry.Selection)
        {
            SceneObject? item = Document.Find(id);
            if (item is not null)
            {
                Selection.Add(item);
            }
        }

        _editTarget = null;
        RestoreNodeEditTarget();
        CancelPen();
    }

    private void RestoreNodeEditTarget()
    {
        foreach (SceneObject item in Selection)
        {
            if (item is PathObject path)
            {
                _editTarget = path;
                break;
            }
        }
    }

    /// <summary>Selects a single object.</summary>
    public void Select(SceneObject? item)
    {
        Selection.Clear();
        if (item is not null)
        {
            Selection.Add(item);
        }

        if (Tool == ToolKind.Direct)
        {
            _editTarget = item as PathObject;
            _selectedNode = null;
        }

        Notify();
    }

    /// <summary>Adds or removes an object from the selection.</summary>
    public void Toggle(SceneObject item)
    {
        if (Selection.Remove(item))
        {
            if (Selection.Count == 0)
            {
                _editTarget = null;
            }

            Notify();
            return;
        }

        Selection.Add(item);
        Notify();
    }

    /// <summary>Clears the selection and any node editing state.</summary>
    public void DeselectAll(bool notify = true)
    {
        Selection.Clear();
        _editTarget = null;
        _selectedNode = null;
        _toolHint = "Deselected";
        if (notify)
        {
            Notify();
        }
        else
        {
            Touch();
        }
    }

    /// <summary>Selects every top-level object in the active layer.</summary>
    public void SelectAll()
    {
        Selection.Clear();
        foreach (SceneObject item in Document.Root.Children.SelectMany(layer => layer is GroupObject group ? group.Children : []))
        {
            if (item.Visible && !item.IsEffectivelyLocked)
            {
                Selection.Add(item);
            }
        }

        _toolHint = $"{Selection.Count} object{(Selection.Count == 1 ? "" : "s")} selected";
        Notify();
    }

    /// <summary>Removes the selected objects.</summary>
    public void DeleteSelection()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        PushHistory("Delete");
        foreach (SceneObject item in Selection.ToArray())
        {
            item.Parent?.Remove(item);
        }

        Selection.Clear();
        _editTarget = null;
        _toolHint = "Deleted";
        Notify();
    }

    /// <summary>Duplicates the selection in place with a small offset.</summary>
    public void DuplicateSelection()
    {
        if (Selection.Count == 0)
        {
            return;
        }

        PushHistory("Duplicate");
        List<SceneObject> copies = [];
        foreach (SceneObject item in Selection)
        {
            SceneObject copy = item.Clone(false);
            copy.Transform *= Mat.Translate(18f, 18f);
            GroupObject parent = item.Parent ?? Document.Root;
            parent.Add(copy);
            copies.Add(copy);
        }

        Selection.Clear();
        Selection.AddRange(copies);
        _toolHint = "Duplicated";
        Notify();
    }

    /// <summary>Copies the selection to the internal clipboard.</summary>
    public void Copy()
    {
        _clipboard.Clear();
        foreach (SceneObject item in Selection)
        {
            _clipboard.Add(item.Clone(false));
        }

        _toolHint = _clipboard.Count == 0 ? "Nothing to copy" : $"Copied {_clipboard.Count}";
        Notify();
    }

    /// <summary>Pastes clipboard objects into the active layer.</summary>
    public void Paste()
    {
        if (_clipboard.Count == 0)
        {
            return;
        }

        PushHistory("Paste");
        Selection.Clear();
        foreach (SceneObject item in _clipboard)
        {
            SceneObject copy = item.Clone(false);
            copy.Transform *= Mat.Translate(24f, 24f);
            ActiveLayer.Add(copy);
            Selection.Add(copy);
        }

        _toolHint = "Pasted";
        Notify();
    }

    /// <summary>Cuts the selection.</summary>
    public void Cut()
    {
        Copy();
        DeleteSelection();
    }

    /// <summary>Groups the selected objects.</summary>
    public void GroupSelection()
    {
        if (Selection.Count < 1)
        {
            return;
        }

        List<SceneObject> ordered = Selection
            .Where(item => item.Parent is not null)
            .OrderBy(item => item.Parent!.Children.IndexOf(item))
            .ToList();
        if (ordered.Count < 1)
        {
            return;
        }

        GroupObject parent = ordered[0].Parent!;
        if (ordered.Any(item => item.Parent != parent))
        {
            SetStatus("Grouping needs every object in the same layer.");
            return;
        }

        PushHistory("Group");
        int index = parent.IndexOf(ordered[^1]);
        GroupObject group = new() { Name = "Group" };
        foreach (SceneObject item in ordered)
        {
            parent.Remove(item);
            group.Add(item);
        }

        parent.Insert(Math.Max(index, 0), group);
        Selection.Clear();
        Selection.Add(group);
        _toolHint = "Grouped";
        Notify();
    }

    /// <summary>Ungroups every selected group.</summary>
    public void UngroupSelection()
    {
        List<GroupObject> groups = Selection.OfType<GroupObject>().Where(group => !group.IsLayer).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        PushHistory("Ungroup");
        List<SceneObject> released = [];
        foreach (GroupObject group in groups)
        {
            GroupObject? parent = group.Parent;
            if (parent is null)
            {
                continue;
            }

            int index = parent.IndexOf(group);
            Matrix3x2 groupTransform = group.Transform;
            SceneObject[] children = group.Children.ToArray();
            foreach (SceneObject child in children)
            {
                group.Remove(child);
                child.Transform *= groupTransform;
                parent.Insert(index++, child);
                released.Add(child);
            }

            parent.Remove(group);
        }

        Selection.Clear();
        Selection.AddRange(released);
        _toolHint = "Ungrouped";
        Notify();
    }

    /// <summary>Creates a new layer above the active one.</summary>
    public void AddLayer()
    {
        PushHistory("New Layer");
        LayerObject layer = new() { Name = $"Layer {Document.Layers.Count() + 1}" };
        Document.Root.Add(layer);
        Document.ActiveLayer = layer;
        Selection.Clear();
        _toolHint = "Layer added";
        Notify();
    }

    /// <summary>Deletes a layer and everything inside it.</summary>
    public void DeleteLayer(LayerObject layer)
    {
        if (Document.Layers.Count() <= 1)
        {
            SetStatus("A document needs at least one layer.");
            return;
        }

        PushHistory("Delete Layer");
        Document.Root.Remove(layer);
        Selection.RemoveAll(item => item == layer || item.Parent == layer);
        Document.ActiveLayer = Document.Layers.LastOrDefault();
        _toolHint = "Layer deleted";
        Notify();
    }

    /// <summary>Raises or lowers the selection within its parent.</summary>
    public void Arrange(int direction)
    {
        if (Selection.Count == 0)
        {
            return;
        }

        PushHistory("Arrange");
        foreach (SceneObject item in Selection)
        {
            GroupObject? parent = item.Parent;
            if (parent is null)
            {
                continue;
            }

            int index = parent.IndexOf(item);
            int target = direction switch
            {
                2 => parent.Children.Count - 1,
                -2 => 0,
                _ => Math.Clamp(index + direction, 0, parent.Children.Count - 1),
            };

            if (target == index)
            {
                continue;
            }

            parent.Remove(item);
            parent.Insert(target, item);
        }

        _toolHint = "Order changed";
        Notify();
    }

    /// <summary>Alignment targets.</summary>
    public enum AlignMode
    {
        /// <summary>Left edges.</summary>
        Left,

        /// <summary>Horizontal centres.</summary>
        CenterHorizontal,

        /// <summary>Right edges.</summary>
        Right,

        /// <summary>Top edges.</summary>
        Top,

        /// <summary>Vertical centres.</summary>
        CenterVertical,

        /// <summary>Bottom edges.</summary>
        Bottom,

        /// <summary>Equal horizontal gaps.</summary>
        DistributeHorizontal,

        /// <summary>Equal vertical gaps.</summary>
        DistributeVertical,
    }

    /// <summary>Aligns or distributes the selection.</summary>
    public void Align(AlignMode mode, bool toArtboard = false)
    {
        if (Selection.Count == 0)
        {
            SetStatus("Select an object to align.");
            return;
        }

        // A single selected object aligns to the artboard; aligning it to its
        // own bounds would do nothing.
        bool useArtboard = toArtboard || Selection.Count == 1;
        Bounds frame = useArtboard ? Document.Active.Bounds : SelectionBounds;
        if (frame.IsEmpty)
        {
            return;
        }

        PushHistory("Align");
        if (mode is AlignMode.DistributeHorizontal or AlignMode.DistributeVertical)
        {
            bool horizontal = mode == AlignMode.DistributeHorizontal;
            List<SceneObject> ordered = Selection
                .OrderBy(item => horizontal ? item.DocumentBounds.Left : item.DocumentBounds.Top)
                .ToList();
            if (ordered.Count < 3)
            {
                return;
            }

            float total = ordered.Sum(item => horizontal ? item.DocumentBounds.Width : item.DocumentBounds.Height);
            float span = horizontal ? frame.Width : frame.Height;
            float gap = (span - total) / (ordered.Count - 1);
            float cursor = horizontal ? frame.Left : frame.Top;
            foreach (SceneObject item in ordered)
            {
                Bounds bounds = item.DocumentBounds;
                float delta = cursor - (horizontal ? bounds.Left : bounds.Top);
                TranslateObject(item, horizontal ? delta : 0f, horizontal ? 0f : delta);
                cursor += (horizontal ? bounds.Width : bounds.Height) + gap;
            }

            Notify();
            return;
        }

        foreach (SceneObject item in Selection)
        {
            Bounds bounds = item.DocumentBounds;
            float dx = mode switch
            {
                AlignMode.Left => frame.Left - bounds.Left,
                AlignMode.CenterHorizontal => frame.Center.X - bounds.Center.X,
                AlignMode.Right => frame.Right - bounds.Right,
                _ => 0f,
            };
            float dy = mode switch
            {
                AlignMode.Top => frame.Top - bounds.Top,
                AlignMode.CenterVertical => frame.Center.Y - bounds.Center.Y,
                AlignMode.Bottom => frame.Bottom - bounds.Bottom,
                _ => 0f,
            };
            TranslateObject(item, dx, dy);
        }

        _toolHint = "Aligned";
        Notify();
    }

    /// <summary>Applies a document-space translation to an object.</summary>
    public static void TranslateObject(SceneObject item, float dx, float dy)
    {
        if (MathF.Abs(dx) < 1e-5f && MathF.Abs(dy) < 1e-5f)
        {
            return;
        }

        item.Transform = item.Transform * Mat.Translate(dx, dy);
    }

    /// <summary>Applies a document-space matrix to an object through its parent transform.</summary>
    public static void ApplyDocumentMatrix(SceneObject item, Matrix3x2 documentMatrix)
    {
        Matrix3x2 parent = Matrix3x2.Identity;
        GroupObject? node = item.Parent;
        while (node is not null)
        {
            parent *= node.Transform;
            node = node.Parent;
        }

        if (Matrix3x2.Invert(parent, out Matrix3x2 inverse))
        {
            item.Transform = item.Transform * parent * documentMatrix * inverse;
        }
        else
        {
            item.Transform = item.Transform * documentMatrix;
        }
    }

    /// <summary>Sets the selection bounds origin through the properties panel.</summary>
    public void SetSelectionPosition(float x, float y)
    {
        if (Selection.Count == 0)
        {
            return;
        }

        Bounds bounds = SelectionBounds;
        PushHistory("Move");
        foreach (SceneObject item in Selection)
        {
            TranslateObject(item, x - bounds.Left, y - bounds.Top);
        }

        Notify();
    }

    /// <summary>Resizes the selection bounds through the properties panel.</summary>
    public void SetSelectionSize(float width, float height)
    {
        if (Selection.Count == 0)
        {
            return;
        }

        Bounds bounds = SelectionBounds;
        if (bounds.Width < 0.01f || bounds.Height < 0.01f)
        {
            return;
        }

        float sx = MathF.Max(width, 0.1f) / bounds.Width;
        float sy = MathF.Max(height, 0.1f) / bounds.Height;
        Matrix3x2 scale = Mat.ScaleAbout(new Vec2(bounds.Left, bounds.Top), sx, sy);
        PushHistory("Resize");
        foreach (SceneObject item in Selection)
        {
            ApplyDocumentMatrix(item, scale);
        }

        Notify();
    }

    /// <summary>Sets the fill of every selected path or text object.</summary>
    public void SetSelectionFill(uint color)
    {
        FillColor = color;
        RememberColour(color);
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        List<TextObject> texts = Selection.OfType<TextObject>().ToList();
        if (paths.Count == 0 && texts.Count == 0)
        {
            Notify();
            return;
        }

        PushHistory("Fill Colour");
        foreach (PathObject path in paths)
        {
            path.Fill = Paint.Solid(color);
        }

        foreach (TextObject text in texts)
        {
            text.Fill = Paint.Solid(color);
        }

        Notify();
    }

    /// <summary>Removes the fill from the selected paths and text.</summary>
    public void ClearSelectionFill()
    {
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        List<TextObject> texts = Selection.OfType<TextObject>().ToList();
        if (paths.Count == 0 && texts.Count == 0)
        {
            return;
        }

        PushHistory("Remove Fill");
        foreach (PathObject path in paths)
        {
            path.Fill = null;
        }

        foreach (TextObject text in texts)
        {
            text.Fill = null;
        }

        Notify();
    }

    /// <summary>Sets the stroke colour of every selected path.</summary>
    public void SetSelectionStroke(uint color)
    {
        StrokeColor = color;
        RememberColour(color);
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        if (paths.Count == 0)
        {
            Notify();
            return;
        }

        PushHistory("Stroke Colour");
        foreach (PathObject path in paths)
        {
            path.Stroke ??= new StrokeStyle { Width = StrokeWidth };
            path.Stroke.Paint = Paint.Solid(color);
            if (path.Stroke.Width <= 0.01f)
            {
                path.Stroke.Width = StrokeWidth;
            }
        }

        Notify();
    }

    /// <summary>Removes the stroke from the selected paths.</summary>
    public void ClearSelectionStroke()
    {
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        if (paths.Count == 0)
        {
            return;
        }

        PushHistory("Remove Stroke");
        foreach (PathObject path in paths)
        {
            path.Stroke = null;
        }

        Notify();
    }

    /// <summary>Sets the stroke width of every selected path.</summary>
    public void SetSelectionStrokeWidth(float width)
    {
        StrokeWidth = MathF.Max(0f, width);
        List<PathObject> paths = Selection.OfType<PathObject>().ToList();
        if (paths.Count == 0)
        {
            Notify();
            return;
        }

        PushHistory("Stroke Width");
        foreach (PathObject path in paths)
        {
            path.Stroke ??= new StrokeStyle { Paint = Paint.Solid(StrokeColor) };
            path.Stroke.Width = StrokeWidth;
        }

        Notify();
    }

    /// <summary>Sets the object opacity of the selection.</summary>
    public void SetSelectionOpacity(float opacity)
    {
        if (Selection.Count == 0)
        {
            return;
        }

        PushHistory("Opacity");
        foreach (SceneObject item in Selection)
        {
            item.Opacity = Math.Clamp(opacity, 0f, 1f);
        }

        Notify();
    }

    /// <summary>Updates opacity live during a slider drag without recording history.</summary>
    public void PreviewSelectionOpacity(float opacity)
    {
        if (Selection.Count == 0)
        {
            return;
        }

        _opacitySnapshot ??= Capture();
        foreach (SceneObject item in Selection)
        {
            item.Opacity = Math.Clamp(opacity, 0f, 1f);
        }

        Touch();
    }

    /// <summary>Commits the live opacity change as one undoable step.</summary>
    public void CommitSelectionOpacity(float opacity)
    {
        PreviewSelectionOpacity(opacity);
        if (_opacitySnapshot is null)
        {
            return;
        }

        _undo.Push(_opacitySnapshot);
        _redo.Clear();
        _opacitySnapshot = null;
        Notify();
    }

    /// <summary>Sets the corner radius of selected live rounded rectangles.</summary>
    public void SetSelectionCornerRadius(float radius)
    {
        List<PathObject> shapes = Selection.OfType<PathObject>()
            .Where(path => path.Shape?.Kind == ShapeKind.RoundedRectangle)
            .ToList();
        if (shapes.Count == 0)
        {
            return;
        }

        PushHistory("Corner Radius");
        foreach (PathObject path in shapes)
        {
            path.Shape!.CornerRadius = MathF.Max(0f, radius);
            path.Rebuild();
        }

        Notify();
    }

    private void RememberColour(uint color)
    {
        RecentColours.Remove(color);
        RecentColours.Insert(0, color);
        if (RecentColours.Count > 12)
        {
            RecentColours.RemoveAt(RecentColours.Count - 1);
        }
    }

    /// <summary>Handles a keyboard shortcut reported by the host.</summary>
    public bool HandleKey(string key)
    {
        if (IsTyping)
        {
            return false;
        }

        bool shift = key.Contains("Shift+", StringComparison.Ordinal);
        string plain = key.Replace("Shift+", string.Empty, StringComparison.Ordinal)
            .Replace("Ctrl+", string.Empty, StringComparison.Ordinal)
            .Replace("Alt+", string.Empty, StringComparison.Ordinal);

        switch (key)
        {
            case "Ctrl+Z":
                Undo();
                return true;
            case "Ctrl+Y":
            case "Ctrl+Shift+Z":
                Redo();
                return true;
            case "Ctrl+C":
                Copy();
                return true;
            case "Ctrl+X":
                Cut();
                return true;
            case "Ctrl+V":
                Paste();
                return true;
            case "Ctrl+D":
                DuplicateSelection();
                return true;
            case "Ctrl+A":
                SelectAll();
                return true;
            case "Ctrl+G":
                GroupSelection();
                return true;
            case "Ctrl+Shift+G":
                UngroupSelection();
                return true;
            case "Ctrl+N":
                NewDocument(1200, 800);
                return true;
            case "Ctrl+Shift+E":
                OpenExportDialog();
                return true;
            case "Ctrl+Shift+I":
                OpenImportDialog();
                return true;
            case "Ctrl+0":
            case "Ctrl+Number0":
                ActualPixels();
                return true;
            case "Ctrl+Equal":
            case "Ctrl+Plus":
                ZoomIn();
                return true;
            case "Ctrl+Minus":
                ZoomOut();
                return true;
            case "Delete":
            case "Backspace":
                if (_editTarget is not null && _selectedNode is not null)
                {
                    DeleteSelectedNode();
                }
                else
                {
                    DeleteSelection();
                }

                return true;
            case "Escape":
                if (OpenMenu is not null)
                {
                    OpenMenu = null;
                    Notify();
                }
                else if (IsPenActive)
                {
                    CancelPen();
                }
                else if (_editTarget is not null)
                {
                    EndNodeEdit();
                }
                else
                {
                    DeselectAll();
                }

                return true;
            case "Enter":
            case "Return":
                if (IsPenActive)
                {
                    FinishPen(false);
                    return true;
                }

                return false;
            case "ArrowLeft":
            case "Shift+ArrowLeft":
                Nudge(shift ? -10f : -1f, 0f);
                return true;
            case "ArrowRight":
            case "Shift+ArrowRight":
                Nudge(shift ? 10f : 1f, 0f);
                return true;
            case "ArrowUp":
            case "Shift+ArrowUp":
                Nudge(0f, shift ? -10f : -1f);
                return true;
            case "ArrowDown":
            case "Shift+ArrowDown":
                Nudge(0f, shift ? 10f : 1f);
                return true;
        }

        if (key.Contains("+", StringComparison.Ordinal) || key.Contains("Ctrl", StringComparison.Ordinal))
        {
            return false;
        }

        switch (plain)
        {
            case "V":
                SetTool(ToolKind.Select);
                return true;
            case "A":
                SetTool(ToolKind.Direct);
                return true;
            case "P":
                SetTool(ToolKind.Pen);
                return true;
            case "N":
                SetTool(ToolKind.Pencil);
                return true;
            case "M":
                SetTool(ToolKind.Rectangle);
                return true;
            case "E":
                SetTool(ToolKind.Ellipse);
                return true;
            case "R":
                SetTool(ToolKind.RoundedRectangle);
                return true;
            case "L":
                SetTool(ToolKind.Line);
                return true;
            case "S":
                SetTool(ToolKind.Star);
                return true;
            case "G":
                SetTool(ToolKind.Polygon);
                return true;
            case "T":
                SetTool(ToolKind.Text);
                return true;
            case "Q":
                SetTool(ToolKind.Lasso);
                return true;
            case "Z":
                SetTool(ToolKind.Zoom);
                return true;
            case "H":
                SetTool(ToolKind.Hand);
                return true;
        }

        return false;
    }

    private void Nudge(float dx, float dy)
    {
        if (Selection.Count == 0)
        {
            return;
        }

        PushHistory("Nudge");
        foreach (SceneObject item in Selection)
        {
            TranslateObject(item, dx, dy);
        }

        Touch();
    }
}
