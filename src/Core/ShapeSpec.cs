namespace VectorDotNet.Core;

/// <summary>The parametric shapes the editor can keep live.</summary>
public enum ShapeKind
{
    /// <summary>A free path with no parameters.</summary>
    Path,

    /// <summary>An axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>A rectangle with rounded corners.</summary>
    RoundedRectangle,

    /// <summary>An ellipse inscribed in the shape's box.</summary>
    Ellipse,

    /// <summary>A regular polygon.</summary>
    Polygon,

    /// <summary>A star.</summary>
    Star,

    /// <summary>A straight segment.</summary>
    Line,

    /// <summary>An Archimedean spiral.</summary>
    Spiral,
}

/// <summary>
/// Parameters of a live shape. The geometry is centred on the local origin so
/// rotation and scaling need no extra pivot bookkeeping; the object transform
/// carries position, rotation and any post-creation scaling.
/// </summary>
public sealed class ShapeSpec
{
    /// <summary>Which parametric shape this describes.</summary>
    public ShapeKind Kind { get; set; } = ShapeKind.Rectangle;

    /// <summary>Width of the shape's local box.</summary>
    public float Width { get; set; } = 100f;

    /// <summary>Height of the shape's local box.</summary>
    public float Height { get; set; } = 100f;

    /// <summary>Corner radius for rounded rectangles.</summary>
    public float CornerRadius { get; set; } = 12f;

    /// <summary>Number of sides or star points.</summary>
    public int Points { get; set; } = 5;

    /// <summary>Star inner radius as a fraction of the outer radius.</summary>
    public float InnerRatio { get; set; } = 0.5f;

    /// <summary>Shape rotation in degrees, for polygons and stars.</summary>
    public float Rotation { get; set; }

    /// <summary>Number of spiral turns.</summary>
    public float Turns { get; set; } = 3f;

    /// <summary>Builds the path for the current parameters.</summary>
    public VPath Build()
    {
        Bounds box = new(-Width * 0.5f, -Height * 0.5f, Width, Height);
        return Kind switch
        {
            ShapeKind.Rectangle => VPath.Rectangle(box),
            ShapeKind.RoundedRectangle => VPath.RoundedRectangle(box, CornerRadius),
            ShapeKind.Ellipse => VPath.Ellipse(box),
            ShapeKind.Polygon => VPath.Polygon(Vec2.Zero, Width * 0.5f, Height * 0.5f, Points, Rotation),
            ShapeKind.Star => VPath.Star(Vec2.Zero, Width * 0.5f, Height * 0.5f, Points, InnerRatio, Rotation),
            ShapeKind.Line => VPath.Line(new Vec2(-Width * 0.5f, 0f), new Vec2(Width * 0.5f, 0f)),
            ShapeKind.Spiral => VPath.Spiral(Vec2.Zero, MathF.Min(Width, Height) * 0.5f, Turns, 0f),
            _ => new VPath(),
        };
    }

    /// <summary>Deep copy.</summary>
    public ShapeSpec Clone() => (ShapeSpec)MemberwiseClone();

    /// <summary>The display name used by the layers panel and properties.</summary>
    public string DisplayName => Kind switch
    {
        ShapeKind.Rectangle => "Rectangle",
        ShapeKind.RoundedRectangle => "Rounded Rectangle",
        ShapeKind.Ellipse => "Ellipse",
        ShapeKind.Polygon => "Polygon",
        ShapeKind.Star => "Star",
        ShapeKind.Line => "Line",
        ShapeKind.Spiral => "Spiral",
        _ => "Path",
    };
}
