using System.Numerics;
using VectorDotNet.Core;

namespace VectorDotNet.Tests;

public sealed class VecTests
{
    [Fact]
    public void ArithmeticAndLengthWork()
    {
        Vec2 a = new(3, 4);
        Vec2 b = new(1, 2);

        Assert.Equal(5f, a.Length, 4);
        Assert.Equal(new Vec2(4, 6), a + b);
        Assert.Equal(new Vec2(2, 2), a - b);
        Assert.Equal(new Vec2(-3, -4), -a);
        Assert.Equal(new Vec2(6, 8), a * 2f);
        Assert.Equal(new Vec2(6, 8), 2f * a);
        Assert.Equal(new Vec2(1.5f, 2f), a / 2f);
        Assert.Equal(new Vec2(-4, 3), a.Perpendicular);

        Assert.Equal(2.828427f, Vec2.Distance(a, b), 3);
        Assert.Equal(new Vec2(2, 3), Vec2.Lerp(a, b, 0.5f));
        Assert.Equal(11f, Vec2.Dot(a, b));
        Assert.Equal(2f, Vec2.Cross(a, b));
    }

    [Fact]
    public void NormalisedZeroStaysZeroAndRotationIsClockwiseInScreenSpace()
    {
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized);
        Assert.Equal(1f, new Vec2(10, 0).Normalized.Length, 4);

        Vec2 rotated = new Vec2(1, 0).Rotate(MathF.PI / 2f);
        V.Nearly(0f, rotated.X);
        V.Nearly(1f, rotated.Y);
    }

    [Fact]
    public void EqualityUsesExactComponents()
    {
        Assert.True(new Vec2(1, 2) == new Vec2(1, 2));
        Assert.True(new Vec2(1, 2) != new Vec2(1, 3));
        Assert.Equal(new Vec2(1, 2).GetHashCode(), new Vec2(1, 2).GetHashCode());
        Assert.Equal("1.5, 2", new Vec2(1.5f, 2f).ToString());
    }
}

public sealed class BoundsTests
{
    [Fact]
    public void EdgesCentreAndValidityAreDerivedFromTheRectangle()
    {
        Bounds bounds = new(10, 20, 30, 40);

        Assert.Equal(10f, bounds.Left);
        Assert.Equal(20f, bounds.Top);
        Assert.Equal(40f, bounds.Right);
        Assert.Equal(60f, bounds.Bottom);
        Assert.Equal(new Vec2(25, 40), bounds.Center);
        Assert.True(bounds.IsValid);
        Assert.False(bounds.IsEmpty);
        Assert.True(Bounds.Empty.IsEmpty);
        Assert.True(new Bounds(0, 0, 0, 5).IsEmpty);
    }

    [Fact]
    public void EmptyAbsorbsPointsAndOtherBounds()
    {
        Bounds bounds = Bounds.Empty;
        bounds.Add(new Vec2(10, 20));
        bounds.Add(new Vec2(4, 30));
        bounds.Add(new Bounds(20, 10, 5, 5));

        Assert.Equal(4f, bounds.Left);
        Assert.Equal(10f, bounds.Top);
        Assert.Equal(25f, bounds.Right);
        Assert.Equal(30f, bounds.Bottom);
    }

    [Fact]
    public void ContainsIntersectsInflateAndIntersect()
    {
        Bounds bounds = new(0, 0, 10, 10);

        Assert.True(bounds.Contains(new Vec2(5, 5)));
        Assert.False(bounds.Contains(new Vec2(11, 5)));
        Assert.True(bounds.Intersects(new Bounds(9, 9, 5, 5)));
        Assert.False(bounds.Intersects(new Bounds(11, 0, 5, 5)));

        Bounds inflated = bounds.Inflate(2);
        Assert.Equal(-2f, inflated.Left);
        Assert.Equal(14f, inflated.Width);

        Bounds overlap = bounds.Intersect(new Bounds(5, 5, 10, 10));
        Assert.Equal(new Bounds(5, 5, 5, 5), overlap);
        Assert.True(bounds.Intersect(new Bounds(20, 20, 2, 2)).IsEmpty);
    }

    [Fact]
    public void FromPointsAndNormalizedHandleNegativeSizes()
    {
        Bounds bounds = Bounds.FromPoints(new Vec2(10, 20), new Vec2(4, 8));
        Assert.Equal(new Bounds(4, 8, 6, 12), bounds);

        Bounds normalized = new Bounds(10, 10, -4, -6).Normalized();
        Assert.Equal(new Bounds(6, 4, 4, 6), normalized);
    }
}

public sealed class MatrixTests
{
    [Fact]
    public void ApplyProjectsPointsAndVectors()
    {
        Matrix3x2 matrix = Mat.Translate(10, 20);

        Assert.Equal(new Vec2(15, 26), matrix.Apply(new Vec2(5, 6)));
        Assert.Equal(new Vec2(5, 6), matrix.ApplyVector(new Vec2(5, 6)));
    }

    [Fact]
    public void TransformBoundsCoversRotatedCorners()
    {
        Bounds bounds = new Bounds(-1, -1, 2, 2).Inflate(0);
        Bounds rotated = Mat.Rotate(MathF.PI / 4f).TransformBounds(new Bounds(0, 0, 10, 10));

        Assert.True(rotated.Width > 10f);
        Assert.True(rotated.Height > 10f);
        V.Nearly(14.142f, rotated.Width, 0.01f);
    }

    [Fact]
    public void ScaleAboutAndRotateAboutPinTheirOrigin()
    {
        Vec2 origin = new(10, 10);
        Matrix3x2 scale = Mat.ScaleAbout(origin, 2f, 2f);
        Assert.Equal(origin, scale.Apply(origin));
        Assert.Equal(new Vec2(30, 10), scale.Apply(new Vec2(20, 10)));

        Matrix3x2 rotate = Mat.RotateAbout(origin, MathF.PI / 2f);
        Vec2 rotated = rotate.Apply(new Vec2(20, 10));
        V.Nearly(10f, rotated.X);
        V.Nearly(20f, rotated.Y);
    }

    [Fact]
    public void ScaleFactorAveragesAxes()
    {
        Assert.Equal(2f, Mat.Scale(2, 2).ScaleFactor(), 4);
        Assert.Equal(1.5f, Mat.Scale(2, 1).ScaleFactor(), 4);
        Assert.Equal(1f, Mat.Identity.ScaleFactor(), 4);
    }
}

public sealed class ColourTests
{
    [Fact]
    public void ChannelsPackInArgbOrder()
    {
        uint colour = Colour.Rgba(0x12, 0x34, 0x56, 0x78);

        Assert.Equal(0x12, Colour.R(colour));
        Assert.Equal(0x34, Colour.G(colour));
        Assert.Equal(0x56, Colour.B(colour));
        Assert.Equal(0x78, Colour.A(colour));
        Assert.Equal(0xFF123456u, Colour.Rgb(0x12, 0x34, 0x56));
    }

    [Fact]
    public void AlphaHelpersClampAndPreserveColour()
    {
        Assert.Equal(0x80112233u, Colour.WithAlpha(0xFF112233, 0x80));
        Assert.Equal(0xFF112233u, Colour.Opaque(0x00112233));
        Assert.Equal(0x40112233u, Colour.Fade(0x80112233, 0.5f));
        Assert.Equal(0x00112233u, Colour.Fade(0xFF112233, 0f));
    }

    [Fact]
    public void HexFormattingAndParsingSupportThreeSixAndEightDigits()
    {
        Assert.Equal("#102030", Colour.ToHex(0xFF102030));

        Assert.True(Colour.TryParseHex("#ABC", out uint shortForm));
        Assert.Equal(0xFFAABBCCu, shortForm);

        Assert.True(Colour.TryParseHex("#102030", out uint six));
        Assert.Equal(0xFF102030u, six);

        Assert.True(Colour.TryParseHex("#80102030", out uint eight));
        Assert.Equal(0x80102030u, eight);

        Assert.False(Colour.TryParseHex("nope", out _));
        Assert.False(Colour.TryParseHex("", out _));
        Assert.False(Colour.TryParseHex("#12", out _));
    }

    [Fact]
    public void LerpAndContrastInterpolate()
    {
        uint black = 0xFF000000;
        uint white = 0xFFFFFFFF;

        Assert.Equal(0xFF808080u, Colour.Lerp(black, white, 0.5f));
        Assert.Equal(0xFF000000u, Colour.Lerp(black, white, -1f));
        Assert.Equal(0xFFFFFFFFu, Colour.Lerp(black, white, 2f));
        Assert.Equal(0xFF14161Au, Colour.Contrast(white));
        Assert.Equal(0xFFF4F6F9u, Colour.Contrast(black));
    }

    [Fact]
    public void HsvRoundTripsAndBuildsPrimaries()
    {
        Colour.ToHsv(0xFFFF0000u, out float hue, out float saturation, out float value);
        V.Nearly(0f, hue, 0.01f);
        Assert.Equal(1f, saturation, 3);
        Assert.Equal(1f, value, 3);
        Assert.Equal(0xFFFF0000u, Colour.FromHsv(hue, saturation, value));

        Colour.ToHsv(Colour.FromHsv(210f, 0.5f, 0.75f), out float h2, out float s2, out float v2);
        V.Nearly(210f, h2, 0.5f);
        Assert.Equal(0.5f, s2, 2);
        Assert.Equal(0.75f, v2, 2);
    }
}
