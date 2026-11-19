using System.Numerics;

namespace VectorDotNet.Core;

/// <summary>A point or direction in document or device space.</summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    /// <summary>Creates a vector.</summary>
    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Horizontal component.</summary>
    public float X { get; }

    /// <summary>Vertical component.</summary>
    public float Y { get; }

    /// <summary>The zero vector.</summary>
    public static Vec2 Zero => default;

    /// <summary>Length in the current space.</summary>
    public float Length => MathF.Sqrt((X * X) + (Y * Y));

    /// <summary>This vector scaled to unit length, or zero when it has no length.</summary>
    public Vec2 Normalized
    {
        get
        {
            float length = Length;
            return length < 1e-6f ? Zero : new Vec2(X / length, Y / length);
        }
    }

    /// <summary>The vector rotated a quarter turn.</summary>
    public Vec2 Perpendicular => new(-Y, X);

    /// <summary>Adds two vectors.</summary>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    /// <summary>Subtracts one vector from another.</summary>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Negates a vector.</summary>
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

    /// <summary>Scales a vector.</summary>
    public static Vec2 operator *(Vec2 a, float scale) => new(a.X * scale, a.Y * scale);

    /// <summary>Scales a vector.</summary>
    public static Vec2 operator *(float scale, Vec2 a) => a * scale;

    /// <summary>Divides a vector.</summary>
    public static Vec2 operator /(Vec2 a, float scale) => new(a.X / scale, a.Y / scale);

    /// <summary>Distance between two points.</summary>
    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

    /// <summary>Linear interpolation between two points.</summary>
    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + ((b - a) * t);

    /// <summary>Dot product.</summary>
    public static float Dot(Vec2 a, Vec2 b) => (a.X * b.X) + (a.Y * b.Y);

    /// <summary>Two-dimensional cross product magnitude.</summary>
    public static float Cross(Vec2 a, Vec2 b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>Rotates the vector around the origin.</summary>
    public Vec2 Rotate(float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vec2((X * cos) - (Y * sin), (X * sin) + (Y * cos));
    }

    /// <inheritdoc/>
    public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Compares two vectors.</summary>
    public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);

    /// <summary>Compares two vectors.</summary>
    public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

    /// <inheritdoc/>
    public override string ToString() => $"{X:0.###}, {Y:0.###}";
}

/// <summary>An axis-aligned rectangle used for bounds and viewports.</summary>
public struct Bounds
{
    private bool _valid;

    /// <summary>Creates a rectangle from its top-left corner and size.</summary>
    public Bounds(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        _valid = true;
    }

    /// <summary>Left edge.</summary>
    public float X { get; set; }

    /// <summary>Top edge.</summary>
    public float Y { get; set; }

    /// <summary>Width.</summary>
    public float Width { get; set; }

    /// <summary>Height.</summary>
    public float Height { get; set; }

    /// <summary>Left edge.</summary>
    public readonly float Left => X;

    /// <summary>Top edge.</summary>
    public readonly float Top => Y;

    /// <summary>Right edge.</summary>
    public readonly float Right => X + Width;

    /// <summary>Bottom edge.</summary>
    public readonly float Bottom => Y + Height;

    /// <summary>Centre point.</summary>
    public readonly Vec2 Center => new(X + (Width * 0.5f), Y + (Height * 0.5f));

    /// <summary>Whether the rectangle has no area.</summary>
    public readonly bool IsEmpty => !_valid || Width <= 1e-6f || Height <= 1e-6f;

    /// <summary>Whether the rectangle has been given any points.</summary>
    public readonly bool IsValid => _valid;

    /// <summary>A rectangle that absorbs any union, used as an accumulator.</summary>
    public static Bounds Empty => default;

    /// <summary>Creates the smallest rectangle containing two points.</summary>
    public static Bounds FromPoints(Vec2 a, Vec2 b) => new(
        MathF.Min(a.X, b.X),
        MathF.Min(a.Y, b.Y),
        MathF.Abs(a.X - b.X),
        MathF.Abs(a.Y - b.Y));

    /// <summary>Expands this rectangle to contain a point.</summary>
    public void Add(Vec2 point)
    {
        if (!_valid)
        {
            X = point.X;
            Y = point.Y;
            Width = 0f;
            Height = 0f;
            _valid = true;
            return;
        }

        float left = MathF.Min(X, point.X);
        float top = MathF.Min(Y, point.Y);
        float right = MathF.Max(Right, point.X);
        float bottom = MathF.Max(Bottom, point.Y);
        X = left;
        Y = top;
        Width = right - left;
        Height = bottom - top;
    }

    /// <summary>Expands this rectangle to contain another.</summary>
    public void Add(Bounds other)
    {
        if (!other._valid)
        {
            return;
        }

        if (!_valid)
        {
            this = other;
            return;
        }

        Add(new Vec2(other.X, other.Y));
        Add(new Vec2(other.Right, other.Bottom));
    }

    /// <summary>Returns this rectangle grown by the same amount on every side.</summary>
    public readonly Bounds Inflate(float amount) => new(X - amount, Y - amount, Width + (amount * 2f), Height + (amount * 2f));

    /// <summary>Whether a point lies inside the rectangle.</summary>
    public readonly bool Contains(Vec2 point) =>
        _valid && point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>Whether two rectangles share any area.</summary>
    public readonly bool Intersects(Bounds other) =>
        _valid && other._valid && other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;

    /// <summary>The overlapping region of two rectangles.</summary>
    public readonly Bounds Intersect(Bounds other)
    {
        if (!_valid || !other._valid)
        {
            return Empty;
        }

        float left = MathF.Max(X, other.X);
        float top = MathF.Max(Y, other.Y);
        float right = MathF.Min(Right, other.Right);
        float bottom = MathF.Min(Bottom, other.Bottom);
        if (right <= left || bottom <= top)
        {
            return Empty;
        }

        return new Bounds(left, top, right - left, bottom - top);
    }

    /// <summary>Normalises negative dimensions.</summary>
    public readonly Bounds Normalized() => new(
        Width < 0 ? X + Width : X,
        Height < 0 ? Y + Height : Y,
        MathF.Abs(Width),
        MathF.Abs(Height));

    /// <inheritdoc/>
    public override readonly string ToString() => $"{X:0.##}, {Y:0.##}  {Width:0.##} × {Height:0.##}";
}

/// <summary>Matrix helpers built on <see cref="Matrix3x2"/> using row-vector composition.</summary>
public static class Mat
{
    /// <summary>An identity transform.</summary>
    public static Matrix3x2 Identity => Matrix3x2.Identity;

    /// <summary>Creates a translation.</summary>
    public static Matrix3x2 Translate(float x, float y) => Matrix3x2.CreateTranslation(x, y);

    /// <summary>Creates a translation.</summary>
    public static Matrix3x2 Translate(Vec2 offset) => Matrix3x2.CreateTranslation(offset.X, offset.Y);

    /// <summary>Creates a scale.</summary>
    public static Matrix3x2 Scale(float x, float y) => Matrix3x2.CreateScale(x, y);

    /// <summary>Creates a rotation in radians.</summary>
    public static Matrix3x2 Rotate(float radians) => Matrix3x2.CreateRotation(radians);

    /// <summary>Creates a scale about a fixed point.</summary>
    public static Matrix3x2 ScaleAbout(Vec2 origin, float sx, float sy) =>
        Matrix3x2.CreateTranslation(-origin.X, -origin.Y)
        * Matrix3x2.CreateScale(sx, sy)
        * Matrix3x2.CreateTranslation(origin.X, origin.Y);

    /// <summary>Creates a rotation about a fixed point.</summary>
    public static Matrix3x2 RotateAbout(Vec2 origin, float radians) =>
        Matrix3x2.CreateTranslation(-origin.X, -origin.Y)
        * Matrix3x2.CreateRotation(radians)
        * Matrix3x2.CreateTranslation(origin.X, origin.Y);

    /// <summary>Composes two transforms: <paramref name="first"/> runs, then <paramref name="second"/>.</summary>
    public static Matrix3x2 Compose(Matrix3x2 first, Matrix3x2 second) => first * second;

    /// <summary>Projects a point through a transform.</summary>
    public static Vec2 Apply(this Matrix3x2 matrix, Vec2 point) => new(
        (point.X * matrix.M11) + (point.Y * matrix.M21) + matrix.M31,
        (point.X * matrix.M12) + (point.Y * matrix.M22) + matrix.M32);

    /// <summary>Projects a direction through a transform, ignoring translation.</summary>
    public static Vec2 ApplyVector(this Matrix3x2 matrix, Vec2 vector) => new(
        (vector.X * matrix.M11) + (vector.Y * matrix.M21),
        (vector.X * matrix.M12) + (vector.Y * matrix.M22));

    /// <summary>Transforms a rectangle, covering the rotated corners.</summary>
    public static Bounds TransformBounds(this Matrix3x2 matrix, Bounds bounds)
    {
        Bounds result = Bounds.Empty;
        result.Add(matrix.Apply(new Vec2(bounds.Left, bounds.Top)));
        result.Add(matrix.Apply(new Vec2(bounds.Right, bounds.Top)));
        result.Add(matrix.Apply(new Vec2(bounds.Right, bounds.Bottom)));
        result.Add(matrix.Apply(new Vec2(bounds.Left, bounds.Bottom)));
        return result;
    }

    /// <summary>The average axis scale of the matrix, used for stroke widths and hit tolerance.</summary>
    public static float ScaleFactor(this Matrix3x2 matrix)
    {
        float sx = MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
        float sy = MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22));
        return (sx + sy) * 0.5f;
    }
}

/// <summary>Packed 0xAARRGGBB colour helpers.</summary>
public static class Colour
{
    /// <summary>Builds an opaque colour from channels.</summary>
    public static uint Rgb(int r, int g, int b) => Rgba(r, g, b, 255);

    /// <summary>Builds a colour from channels.</summary>
    public static uint Rgba(int r, int g, int b, int a) => ((uint)Math.Clamp(a, 0, 255) << 24)
        | ((uint)Math.Clamp(r, 0, 255) << 16)
        | ((uint)Math.Clamp(g, 0, 255) << 8)
        | (uint)Math.Clamp(b, 0, 255);

    /// <summary>Red channel.</summary>
    public static int R(uint color) => (int)((color >> 16) & 0xFF);

    /// <summary>Green channel.</summary>
    public static int G(uint color) => (int)((color >> 8) & 0xFF);

    /// <summary>Blue channel.</summary>
    public static int B(uint color) => (int)(color & 0xFF);

    /// <summary>Alpha channel.</summary>
    public static int A(uint color) => (int)((color >> 24) & 0xFF);

    /// <summary>Replaces the alpha channel.</summary>
    public static uint WithAlpha(uint color, int alpha) => (color & 0x00FFFFFFu) | ((uint)Math.Clamp(alpha, 0, 255) << 24);

    /// <summary>Multiplies the alpha channel by a factor.</summary>
    public static uint Fade(uint color, float factor) => WithAlpha(color, (int)MathF.Round(A(color) * Math.Clamp(factor, 0f, 1f)));

    /// <summary>Opaque version of a colour.</summary>
    public static uint Opaque(uint color) => WithAlpha(color, 255);

    /// <summary>Formats as #RRGGBB.</summary>
    public static string ToHex(uint color) => $"#{R(color):X2}{G(color):X2}{B(color):X2}";

    /// <summary>Parses #RGB, #RRGGBB or #AARRGGBB, returning false when the text is not a colour.</summary>
    public static bool TryParseHex(string? text, out uint color)
    {
        color = 0xFF000000;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().TrimStart('#');
        if (value.Length is not (3 or 6 or 8)
            || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint parsed))
        {
            return false;
        }

        color = value.Length switch
        {
            3 => 0xFF000000
                | ((uint)(((parsed >> 8) & 0xF) * 17) << 16)
                | ((uint)(((parsed >> 4) & 0xF) * 17) << 8)
                | (uint)((parsed & 0xF) * 17),
            6 => 0xFF000000 | parsed,
            _ => (parsed & 0x00FFFFFFu) | (((parsed >> 24) & 0xFF) << 24),
        };

        // A six-digit value already carries an implicit opaque alpha in the high byte
        // above, so keep it explicit for readability.
        if (value.Length == 6)
        {
            color |= 0xFF000000u;
        }

        return true;
    }

    /// <summary>Linear interpolation between two colours in straight alpha space.</summary>
    public static uint Lerp(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Rgba(
            (int)MathF.Round(R(a) + ((R(b) - R(a)) * t)),
            (int)MathF.Round(G(a) + ((G(b) - G(a)) * t)),
            (int)MathF.Round(B(a) + ((B(b) - B(a)) * t)),
            (int)MathF.Round(A(a) + ((A(b) - A(a)) * t)));
    }

    /// <summary>A readable foreground colour for a swatch.</summary>
    public static uint Contrast(uint color)
    {
        float luma = (0.299f * R(color)) + (0.587f * G(color)) + (0.114f * B(color));
        return luma > 150f ? 0xFF14161A : 0xFFF4F6F9;
    }

    /// <summary>Converts a packed colour into hue, saturation and value.</summary>
    public static void ToHsv(uint color, out float hue, out float saturation, out float value)
    {
        float r = R(color) / 255f;
        float g = G(color) / 255f;
        float b = B(color) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        value = max;
        saturation = max <= 0.0001f ? 0f : delta / max;

        if (delta <= 0.0001f)
        {
            hue = 0f;
            return;
        }

        if (max == r)
        {
            hue = 60f * (((g - b) / delta) % 6f);
        }
        else if (max == g)
        {
            hue = 60f * (((b - r) / delta) + 2f);
        }
        else
        {
            hue = 60f * (((r - g) / delta) + 4f);
        }

        if (hue < 0f)
        {
            hue += 360f;
        }
    }

    /// <summary>Builds a packed colour from hue, saturation, value and alpha.</summary>
    public static uint FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        hue = ((hue % 360f) + 360f) % 360f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);
        float c = value * saturation;
        float x = c * (1f - MathF.Abs(((hue / 60f) % 2f) - 1f));
        float m = value - c;

        (float r, float g, float b) = hue switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return Rgba(
            (int)MathF.Round((r + m) * 255f),
            (int)MathF.Round((g + m) * 255f),
            (int)MathF.Round((b + m) * 255f),
            (int)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f));
    }
}
