namespace VectorDotNet.Core;

/// <summary>Which appearance channel the colour picker edits.</summary>
public enum ColourTarget
{
    /// <summary>Object fill or the default fill for new objects.</summary>
    Fill,

    /// <summary>Object stroke or the default stroke for new objects.</summary>
    Stroke,

    /// <summary>The active artboard background.</summary>
    Artboard,
}

/// <summary>Colour picker state and live-preview plumbing.</summary>
public sealed partial class EditorSession
{
    private HistoryEntry? _colourSnapshot;
    private readonly List<(SceneObject Item, Paint? Fill, StrokeStyle? Stroke)> _colourBackup = [];
    private uint _backupFill;
    private uint _backupStroke;
    private uint _backupArtboard;

    /// <summary>Whether the colour picker is open.</summary>
    public bool ShowColourPicker { get; private set; }

    /// <summary>Which channel the picker is editing.</summary>
    public ColourTarget ColourPickerTarget { get; private set; }

    /// <summary>The colour the picker opened with.</summary>
    public uint ColourPickerInitial { get; private set; }

    /// <summary>Title for the picker dialog.</summary>
    public string ColourPickerTitle => ColourPickerTarget switch
    {
        ColourTarget.Fill => "Fill Colour",
        ColourTarget.Stroke => "Stroke Colour",
        _ => "Artboard Background",
    };

    /// <summary>Colours already used in the document, as an automatic palette.</summary>
    public IReadOnlyList<uint> DocumentColours
    {
        get
        {
            HashSet<uint> seen = [];
            List<uint> colours = [];
            foreach (SceneObject item in Document.Root.SelfAndDescendants())
            {
                // A filled object may also carry a visible stroke, so inspect both
                // faces instead of using a switch that only visits the first match.
                if (item is PathObject path)
                {
                    if (path.Fill is { } fill)
                    {
                        AddColour(fill.Color);
                    }

                    if (path.Stroke is { } stroke)
                    {
                        AddColour(stroke.Paint.Color);
                    }
                }
                else if (item is TextObject { Fill: { } textFill })
                {
                    AddColour(textFill.Color);
                }
            }

            AddColour(Document.Active.Background);
            return colours;

            void AddColour(uint colour)
            {
                if (Colour.A(colour) > 4 && seen.Add(colour))
                {
                    colours.Add(colour);
                }
            }
        }
    }

    /// <summary>The colour currently used by a picker target.</summary>
    public uint ColourForTarget(ColourTarget target)
    {
        switch (target)
        {
            case ColourTarget.Fill:
                TextObject? text = Selection.OfType<TextObject>().FirstOrDefault();
                if (text?.Fill is { } textFill)
                {
                    return textFill.Color;
                }

                PathObject? path = Selection.OfType<PathObject>().FirstOrDefault();
                return path?.Fill?.Color ?? FillColor;

            case ColourTarget.Stroke:
                PathObject? stroked = Selection.OfType<PathObject>().FirstOrDefault();
                return stroked?.Stroke?.Paint.Color ?? StrokeColor;

            default:
                return Document.Active.Background;
        }
    }

    /// <summary>Opens the picker for a channel and remembers the state to undo.</summary>
    public void OpenColourPicker(ColourTarget target)
    {
        _colourBackup.Clear();
        foreach (PathObject path in Selection.OfType<PathObject>())
        {
            // Clone the stroke: previewing a stroke colour mutates its paint in
            // place, so keeping the same reference would make Cancel a no-op.
            _colourBackup.Add((path, path.Fill?.Clone(), path.Stroke?.Clone()));
        }

        foreach (TextObject text in Selection.OfType<TextObject>())
        {
            _colourBackup.Add((text, text.Fill?.Clone(), null));
        }

        _backupFill = FillColor;
        _backupStroke = StrokeColor;
        _backupArtboard = Document.Active.Background;
        _colourSnapshot = Capture();

        ColourPickerTarget = target;
        ColourPickerInitial = ColourForTarget(target);
        ShowColourPicker = true;
        OpenMenu = null;
        Notify();
    }

    /// <summary>Applies a colour live without recording an undo step.</summary>
    public void PreviewColour(uint colour)
    {
        ApplyColour(colour);
        Touch();
    }

    /// <summary>Commits the picker colour as one undoable step.</summary>
    public void CommitColour(uint colour)
    {
        ApplyColour(colour);
        if (_colourSnapshot is not null)
        {
            _undo.Push(_colourSnapshot);
            _redo.Clear();
            _colourSnapshot = null;
        }

        RememberColour(colour);
        ShowColourPicker = false;
        Notify();
    }

    /// <summary>Discards the live preview and closes the picker.</summary>
    public void CancelColourPicker()
    {
        RestoreColours();
        ShowColourPicker = false;
        _colourSnapshot = null;
        Notify();
    }

    private void ApplyColour(uint colour)
    {
        switch (ColourPickerTarget)
        {
            case ColourTarget.Fill:
                FillColor = colour;
                foreach (PathObject path in Selection.OfType<PathObject>())
                {
                    path.Fill = Paint.Solid(colour);
                }

                foreach (TextObject text in Selection.OfType<TextObject>())
                {
                    text.Fill = Paint.Solid(colour);
                }

                break;

            case ColourTarget.Stroke:
                StrokeColor = colour;
                foreach (PathObject path in Selection.OfType<PathObject>())
                {
                    path.Stroke ??= new StrokeStyle
                    {
                        Paint = Paint.Solid(colour),
                        Width = MathF.Max(StrokeWidth, 1f),
                    };
                    path.Stroke.Paint = Paint.Solid(colour);
                }

                break;

            case ColourTarget.Artboard:
                Document.Active.Background = colour;
                break;
        }
    }

    private void RestoreColours()
    {
        foreach ((SceneObject item, Paint? fill, StrokeStyle? stroke) in _colourBackup)
        {
            if (item is PathObject path)
            {
                path.Fill = fill;
                path.Stroke = stroke;
            }
            else if (item is TextObject text)
            {
                text.Fill = fill;
            }
        }

        FillColor = _backupFill;
        StrokeColor = _backupStroke;
        Document.Active.Background = _backupArtboard;
        _colourBackup.Clear();
    }
}
