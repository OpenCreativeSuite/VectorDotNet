namespace VectorDotNet.Core;

/// <summary>The kinds of paint an object face can carry.</summary>
public enum PaintKind
{
    /// <summary>No paint: the face is skipped.</summary>
    None,

    /// <summary>A single packed colour.</summary>
    Solid,
}

/// <summary>A fill or stroke colour source.</summary>
public sealed class Paint
{
    /// <summary>What kind of paint this is.</summary>
    public PaintKind Kind { get; set; } = PaintKind.Solid;

    /// <summary>The colour used by a solid paint, packed 0xAARRGGBB.</summary>
    public uint Color { get; set; } = 0xFF000000;

    /// <summary>Whether the paint contributes anything at all.</summary>
    public bool IsVisible => Kind != PaintKind.None && Colour.A(Color) > 0;

    /// <summary>Deep copy.</summary>
    public Paint Clone() => new() { Kind = Kind, Color = Color };

    /// <summary>Creates a solid paint.</summary>
    public static Paint Solid(uint color) => new() { Kind = PaintKind.Solid, Color = color };

    /// <summary>Creates an explicitly absent paint.</summary>
    public static Paint None() => new() { Kind = PaintKind.None };
}

/// <summary>How a stroke ends.</summary>
public enum LineCap
{
    /// <summary>Ends exactly at the anchor.</summary>
    Butt,

    /// <summary>Ends with a semicircle.</summary>
    Round,

    /// <summary>Ends half a width past the anchor.</summary>
    Square,
}

/// <summary>How two stroke segments meet.</summary>
public enum LineJoin
{
    /// <summary>Extends the outer edges to a point.</summary>
    Miter,

    /// <summary>Rounds the outer corner.</summary>
    Round,

    /// <summary>Connects the outer corners with a straight edge.</summary>
    Bevel,
}

/// <summary>A stroke face: paint, width and the details of its outline.</summary>
public sealed class StrokeStyle
{
    /// <summary>The stroke paint.</summary>
    public Paint Paint { get; set; } = Paint.Solid(0xFF1A1D21);

    /// <summary>Stroke width in document units.</summary>
    public float Width { get; set; } = 1f;

    /// <summary>How open ends are finished.</summary>
    public LineCap Cap { get; set; } = LineCap.Butt;

    /// <summary>How segments meet.</summary>
    public LineJoin Join { get; set; } = LineJoin.Miter;

    /// <summary>Miter length limit relative to the stroke width.</summary>
    public float MiterLimit { get; set; } = 4f;

    /// <summary>Dash lengths, alternating on and off. Empty means a solid line.</summary>
    public float[] Dash { get; set; } = [];

    /// <summary>Offset into the dash pattern.</summary>
    public float DashOffset { get; set; }

    /// <summary>Deep copy.</summary>
    public StrokeStyle Clone() => new()
    {
        Paint = Paint.Clone(),
        Width = Width,
        Cap = Cap,
        Join = Join,
        MiterLimit = MiterLimit,
        Dash = (float[])Dash.Clone(),
        DashOffset = DashOffset,
    };

    /// <summary>Whether the stroke would draw anything.</summary>
    public bool IsVisible => Width > 0.001f && Paint.IsVisible;
}
