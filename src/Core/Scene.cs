using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>Base class for everything that appears on the canvas.</summary>
public abstract class SceneObject
{
    /// <summary>Stable identity used by selection, history and the layers panel.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-facing name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Whether the object paints.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Whether editing is refused.</summary>
    public bool Locked { get; set; }

    /// <summary>Compositing opacity from 0 to 1.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Local-to-parent transform.</summary>
    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

    /// <summary>The containing group, or null at document root.</summary>
    public GroupObject? Parent { get; set; }

    /// <summary>Whether this object can contain others.</summary>
    public virtual bool IsContainer => false;

    /// <summary>Geometry bounds in local coordinates.</summary>
    public abstract Bounds LocalBounds { get; }

    /// <summary>Bounds in the parent's coordinate space.</summary>
    public virtual Bounds BoundsInParent => Transform.TransformBounds(LocalBounds);

    /// <summary>Local-to-document transform, including every ancestor.</summary>
    public Matrix3x2 DocumentTransform
    {
        get
        {
            Matrix3x2 matrix = Transform;
            GroupObject? parent = Parent;
            while (parent is not null)
            {
                matrix *= parent.Transform;
                parent = parent.Parent;
            }

            return matrix;
        }
    }

    /// <summary>Axis-aligned bounds in document coordinates.</summary>
    public Bounds DocumentBounds => DocumentTransform.TransformBounds(LocalBounds);

    /// <summary>Whether this object and every ancestor is visible.</summary>
    public bool IsEffectivelyVisible
    {
        get
        {
            SceneObject? node = this;
            while (node is not null)
            {
                if (!node.Visible)
                {
                    return false;
                }

                node = node.Parent;
            }

            return true;
        }
    }

    /// <summary>Whether this object or any ancestor is locked.</summary>
    public bool IsEffectivelyLocked
    {
        get
        {
            SceneObject? node = this;
            while (node is not null)
            {
                if (node.Locked)
                {
                    return true;
                }

                node = node.Parent;
            }

            return false;
        }
    }

    /// <summary>The name shown in the interface.</summary>
    public virtual string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? (this is PathObject path && path.Shape is not null ? path.Shape.DisplayName : "Object")
        : Name;

    /// <summary>Deep copy, preserving identity when history needs it.</summary>
    public abstract SceneObject Clone(bool keepId);

    /// <summary>This object followed by every descendant, depth first.</summary>
    public IEnumerable<SceneObject> SelfAndDescendants()
    {
        yield return this;
        if (this is GroupObject group)
        {
            foreach (SceneObject child in group.Children)
            {
                foreach (SceneObject item in child.SelfAndDescendants())
                {
                    yield return item;
                }
            }
        }
    }
}

/// <summary>A group of objects with its own transform.</summary>
public class GroupObject : SceneObject
{
    /// <summary>Children in paint order, bottom first.</summary>
    public List<SceneObject> Children { get; } = [];

    /// <summary>Whether this group is a top-level layer.</summary>
    public virtual bool IsLayer => false;

    /// <summary>Whether the layers panel shows the children.</summary>
    public bool Expanded { get; set; } = true;

    /// <inheritdoc/>
    public override bool IsContainer => true;

    /// <inheritdoc/>
    public override Bounds LocalBounds
    {
        get
        {
            Bounds bounds = Bounds.Empty;
            foreach (SceneObject child in Children)
            {
                bounds.Add(child.BoundsInParent);
            }

            return bounds;
        }
    }

    /// <summary>Appends a child.</summary>
    public void Add(SceneObject child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    /// <summary>Inserts a child at a paint-order index.</summary>
    public void Insert(int index, SceneObject child)
    {
        child.Parent = this;
        Children.Insert(Math.Clamp(index, 0, Children.Count), child);
    }

    /// <summary>Removes a child, reparenting it to null.</summary>
    public bool Remove(SceneObject child)
    {
        if (!Children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        return true;
    }

    /// <summary>The index of a direct child in paint order, or -1.</summary>
    public int IndexOf(SceneObject child) => Children.IndexOf(child);

    /// <inheritdoc/>
    public override SceneObject Clone(bool keepId)
    {
        GroupObject copy = this is LayerObject ? new LayerObject() : new GroupObject();
        copy.Id = keepId ? Id : Guid.NewGuid();
        copy.Name = Name;
        copy.Visible = Visible;
        copy.Locked = Locked;
        copy.Opacity = Opacity;
        copy.Transform = Transform;
        copy.Expanded = Expanded;

        foreach (SceneObject child in Children)
        {
            SceneObject clone = child.Clone(keepId);
            clone.Parent = copy;
            copy.Children.Add(clone);
        }

        return copy;
    }
}

/// <summary>A top-level layer: a group the layers panel treats as a document band.</summary>
public sealed class LayerObject : GroupObject
{
    /// <inheritdoc/>
    public override bool IsLayer => true;
}

/// <summary>A drawn vector object: geometry, a fill and an optional stroke.</summary>
public sealed class PathObject : SceneObject
{
    private Bounds? _cachedBounds;

    /// <summary>The geometry in local coordinates.</summary>
    public VPath Path { get; set; } = new();

    /// <summary>Parameters when the object is a live shape, or null for a free path.</summary>
    public ShapeSpec? Shape { get; set; }

    /// <summary>The fill paint, or null for an unfilled object.</summary>
    public Paint? Fill { get; set; }

    /// <summary>The stroke bytes, or null for no stroke.</summary>
    public StrokeStyle? Stroke { get; set; }

    /// <inheritdoc/>
    public override Bounds LocalBounds => _cachedBounds ??= Path.GetBounds();

    /// <summary>Drops cached geometry bounds after the path changes.</summary>
    public void InvalidateGeometry()
    {
        _cachedBounds = null;
    }

    /// <summary>Whether the object is a parametric shape rather than a free path.</summary>
    public bool IsShape => Shape is not null;

    /// <summary>Regenerates geometry from <see cref="Shape"/>.</summary>
    public void Rebuild()
    {
        if (Shape is not null)
        {
            Path = Shape.Build();
            _cachedBounds = null;
        }
    }

    /// <summary>Converts the live shape into a free path, keeping its appearance.</summary>
    public void BakeShape()
    {
        Shape = null;
    }

    /// <inheritdoc/>
    public override string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? (Shape?.DisplayName ?? "Path")
        : Name;

    /// <inheritdoc/>
    public override SceneObject Clone(bool keepId)
    {
        PathObject copy = new()
        {
            Id = keepId ? Id : Guid.NewGuid(),
            Name = Name,
            Visible = Visible,
            Locked = Locked,
            Opacity = Opacity,
            Transform = Transform,
            Path = Path.Clone(),
            Shape = Shape?.Clone(),
            Fill = Fill?.Clone(),
            Stroke = Stroke?.Clone(),
        };

        return copy;
    }
}

/// <summary>Point text: a live string drawn with a system font.</summary>
public sealed class TextObject : SceneObject
{
    /// <summary>The text content.</summary>
    public string Text { get; set; } = "Text";

    /// <summary>Font family name.</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Font size in document units.</summary>
    public float FontSize { get; set; } = 48f;

    /// <summary>Whether the face is bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Whether the face is italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Multiply line spacing.</summary>
    public float LineHeight { get; set; } = 1.25f;

    /// <summary>The text paint.</summary>
    public Paint? Fill { get; set; } = Paint.Solid(0xFF17191D);

    /// <summary>Approximate advance width, good enough for layout and picking.</summary>
    public float MeasuredWidth
    {
        get
        {
            int longest = 0;
            foreach (string line in Text.Split('\n'))
            {
                longest = Math.Max(longest, line.Length);
            }

            return MathF.Max(FontSize * 0.5f, longest * FontSize * 0.56f);
        }
    }

    /// <inheritdoc/>
    public override Bounds LocalBounds
    {
        get
        {
            int lines = Math.Max(1, Text.Split('\n').Length);
            return new Bounds(0f, 0f, MeasuredWidth, FontSize * LineHeight * lines);
        }
    }

    /// <inheritdoc/>
    public override string DisplayName => string.IsNullOrWhiteSpace(Text) ? "Text" : Text.Split('\n')[0];

    /// <inheritdoc/>
    public override SceneObject Clone(bool keepId) => new TextObject
    {
        Id = keepId ? Id : Guid.NewGuid(),
        Name = Name,
        Visible = Visible,
        Locked = Locked,
        Opacity = Opacity,
        Transform = Transform,
        Text = Text,
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        LineHeight = LineHeight,
        Fill = Fill?.Clone(),
    };
}

/// <summary>A document guide line.</summary>
public struct Guide
{
    /// <summary>True for a horizontal guide, false for vertical.</summary>
    public bool Horizontal;

    /// <summary>Document-space coordinate.</summary>
    public float Position;
}

/// <summary>One page of the document.</summary>
public sealed class Artboard
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = "Artboard 1";

    /// <summary>Left edge in document space.</summary>
    public float X { get; set; }

    /// <summary>Top edge in document space.</summary>
    public float Y { get; set; }

    /// <summary>Width.</summary>
    public float Width { get; set; } = 1200f;

    /// <summary>Height.</summary>
    public float Height { get; set; } = 800f;

    /// <summary>Background colour of the page.</summary>
    public uint Background { get; set; } = 0xFFFFFFFF;

    /// <summary>Page rectangle.</summary>
    public Bounds Bounds => new(X, Y, Width, Height);

    /// <summary>Deep copy.</summary>
    public Artboard Clone() => (Artboard)MemberwiseClone();
}

/// <summary>A complete vector document: artboards, guides and a layer tree.</summary>
public sealed class Document
{
    /// <summary>Creates an empty document with one layer and one artboard.</summary>
    public Document(float width = 1200f, float height = 800f)
    {
        Root = new GroupObject { Name = "Document" };
        LayerObject layer = new() { Name = "Layer 1" };
        Root.Add(layer);
        ActiveLayer = layer;
        Artboards.Add(new Artboard { Name = "Artboard 1", Width = width, Height = height });
    }

    private Document(GroupObject root)
    {
        Root = root;
    }

    /// <summary>Document name shown in tabs and the title bar.</summary>
    public string Name { get; set; } = "Untitled-1";

    /// <summary>The artboards in this document.</summary>
    public List<Artboard> Artboards { get; } = [];

    /// <summary>Index of the artboard being edited.</summary>
    public int ActiveArtboard { get; set; }

    /// <summary>Guide lines.</summary>
    public List<Guide> Guides { get; } = [];

    /// <summary>The document layer root; its children are layers.</summary>
    public GroupObject Root { get; }

    /// <summary>The layer new objects are added to.</summary>
    public LayerObject? ActiveLayer { get; set; }

    /// <summary>The artboard being edited.</summary>
    public Artboard Active => Artboards.Count == 0
        ? new Artboard { Width = 1200f, Height = 800f }
        : Artboards[Math.Clamp(ActiveArtboard, 0, Artboards.Count - 1)];

    /// <summary>Top-level layers in paint order, bottom first.</summary>
    public IEnumerable<LayerObject> Layers => Root.Children.OfType<LayerObject>();

    /// <summary>Finds an object anywhere in the tree by identity.</summary>
    public SceneObject? Find(Guid id)
    {
        foreach (SceneObject item in Root.SelfAndDescendants())
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Deep copy, optionally preserving identities for history.</summary>
    public Document Clone(bool keepIds = true)
    {
        Document copy = new((GroupObject)Root.Clone(keepIds))
        {
            Name = Name,
            ActiveArtboard = ActiveArtboard,
            ActiveLayer = null,
        };

        foreach (Artboard artboard in Artboards)
        {
            copy.Artboards.Add(artboard.Clone());
        }

        copy.Guides.AddRange(Guides);

        // Restore the active layer by name-and-index when identities were kept.
        if (ActiveLayer is not null)
        {
            int index = Root.Children.IndexOf(ActiveLayer);
            if (index >= 0 && index < copy.Root.Children.Count && copy.Root.Children[index] is LayerObject layer)
            {
                copy.ActiveLayer = layer;
            }
        }

        copy.ActiveLayer ??= copy.Layers.FirstOrDefault();
        if (copy.Artboards.Count == 0)
        {
            copy.Artboards.Add(new Artboard());
        }

        return copy;
    }

    /// <summary>Union of the selected objects' document bounds.</summary>
    public static Bounds SelectionBounds(IEnumerable<SceneObject> objects)
    {
        Bounds bounds = Bounds.Empty;
        foreach (SceneObject item in objects)
        {
            bounds.Add(item.DocumentBounds);
        }

        return bounds;
    }
}
