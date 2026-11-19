using System.Globalization;
using System.Numerics;
using System.Text;

namespace VectorDotNet.Core;

/// <summary>Writes a document artboard as standards-compliant SVG.</summary>
public static class SvgExporter
{
    /// <summary>Serialises one artboard, or the active one, to an SVG document.</summary>
    public static string Export(Document document, int artboardIndex = -1)
    {
        int index = artboardIndex < 0 ? document.ActiveArtboard : artboardIndex;
        index = Math.Clamp(index, 0, Math.Max(0, document.Artboards.Count - 1));
        Artboard artboard = document.Artboards.Count == 0 ? new Artboard() : document.Artboards[index];

        StringBuilder svg = new();
        svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(artboard.Width)}\" height=\"{N(artboard.Height)}\" viewBox=\"0 0 {N(artboard.Width)} {N(artboard.Height)}\">");
        svg.AppendLine($"  <title>{Escape(document.Name)}</title>");
        svg.AppendLine($"  <rect x=\"0\" y=\"0\" width=\"{N(artboard.Width)}\" height=\"{N(artboard.Height)}\" fill=\"{Hex(artboard.Background)}\" />");
        svg.AppendLine($"  <g transform=\"translate({N(-artboard.X)} {N(-artboard.Y)})\">");

        foreach (SceneObject layer in document.Root.Children)
        {
            Emit(layer, svg, "    ");
        }

        svg.AppendLine("  </g>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static void Emit(SceneObject node, StringBuilder svg, string indent)
    {
        if (!node.Visible || node.Opacity <= 0.002f)
        {
            return;
        }

        string transform = TransformAttribute(node.Transform);
        string opacity = node.Opacity < 0.999f ? $" opacity=\"{N(node.Opacity)}\"" : string.Empty;

        switch (node)
        {
            case GroupObject group:
                svg.AppendLine($"{indent}<g{transform}{opacity}>");
                foreach (SceneObject child in group.Children)
                {
                    Emit(child, svg, indent + "  ");
                }

                svg.AppendLine($"{indent}</g>");
                break;

            case PathObject path when path.Shape is not null || path.Path.SubPaths.Count > 0:
                svg.AppendLine($"{indent}<path{transform}{opacity} d=\"{PathData(path.Path)}\"{PaintAttributes(path)}{FillRuleAttribute(path.Path.FillRule)} />");
                break;

            case TextObject text:
                EmitText(text, svg, indent, transform, opacity);
                break;
        }
    }

    private static void EmitText(TextObject text, StringBuilder svg, string indent, string transform, string opacity)
    {
        if (text.Fill is not { IsVisible: true } fill)
        {
            return;
        }

        string weight = text.Bold ? " font-weight=\"700\"" : string.Empty;
        string style = text.Italic ? " font-style=\"italic\"" : string.Empty;
        string fillOpacity = Colour.A(fill.Color) < 255
            ? $" fill-opacity=\"{N(Colour.A(fill.Color) / 255f)}\""
            : string.Empty;

        svg.AppendLine($"{indent}<text{transform}{opacity} font-family=\"{Escape(text.FontFamily)}\" font-size=\"{N(text.FontSize)}\"{weight}{style} fill=\"{Hex(fill.Color)}\"{fillOpacity}>");

        string[] lines = text.Text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0)
            {
                svg.AppendLine($"{indent}  <tspan x=\"0\" y=\"{N(text.FontSize * 0.82f)}\">{Escape(lines[i])}</tspan>");
            }
            else
            {
                svg.AppendLine($"{indent}  <tspan x=\"0\" dy=\"{N(text.FontSize * text.LineHeight)}\">{Escape(lines[i])}</tspan>");
            }
        }

        svg.AppendLine($"{indent}</text>");
    }

    private static string PaintAttributes(PathObject path)
    {
        StringBuilder attributes = new();
        if (path.Fill is { IsVisible: true } fill)
        {
            attributes.Append($" fill=\"{Hex(fill.Color)}\"");
            if (Colour.A(fill.Color) < 255)
            {
                attributes.Append($" fill-opacity=\"{N(Colour.A(fill.Color) / 255f)}\"");
            }
        }
        else
        {
            attributes.Append(" fill=\"none\"");
        }

        if (path.Stroke is { IsVisible: true } stroke)
        {
            attributes.Append($" stroke=\"{Hex(stroke.Paint.Color)}\" stroke-width=\"{N(stroke.Width)}\"");
            if (Colour.A(stroke.Paint.Color) < 255)
            {
                attributes.Append($" stroke-opacity=\"{N(Colour.A(stroke.Paint.Color) / 255f)}\"");
            }

            attributes.Append(stroke.Cap switch
            {
                LineCap.Round => " stroke-linecap=\"round\"",
                LineCap.Square => " stroke-linecap=\"square\"",
                _ => string.Empty,
            });

            attributes.Append(stroke.Join switch
            {
                LineJoin.Round => " stroke-linejoin=\"round\"",
                LineJoin.Bevel => " stroke-linejoin=\"bevel\"",
                _ => $" stroke-linejoin=\"miter\" stroke-miterlimit=\"{N(stroke.MiterLimit)}\"",
            });

            if (stroke.Dash.Length > 0)
            {
                attributes.Append($" stroke-dasharray=\"{string.Join(' ', stroke.Dash.Select(N))}\"");
                if (MathF.Abs(stroke.DashOffset) > 0.001f)
                {
                    attributes.Append($" stroke-dashoffset=\"{N(stroke.DashOffset)}\"");
                }
            }
        }

        return attributes.ToString();
    }

    private static string FillRuleAttribute(FillRule rule) =>
        rule == FillRule.EvenOdd ? " fill-rule=\"evenodd\"" : " fill-rule=\"nonzero\"";

    private static string PathData(VPath path)
    {
        StringBuilder data = new();
        foreach (VSubPath sub in path.SubPaths)
        {
            if (sub.Nodes.Count == 0)
            {
                continue;
            }

            PathNode first = sub.Nodes[0];
            data.Append($"M {N(first.Point.X)} {N(first.Point.Y)} ");

            for (int i = 0; i < sub.Nodes.Count; i++)
            {
                if (i == sub.Nodes.Count - 1 && !sub.Closed)
                {
                    break;
                }

                PathNode from = sub.Nodes[i];
                PathNode to = sub.Nodes[(i + 1) % sub.Nodes.Count];
                if (!from.HasOut && !to.HasIn)
                {
                    data.Append($"L {N(to.Point.X)} {N(to.Point.Y)} ");
                }
                else
                {
                    Vec2 control1 = from.HasOut ? from.Out : from.Point;
                    Vec2 control2 = to.HasIn ? to.In : to.Point;
                    data.Append($"C {N(control1.X)} {N(control1.Y)} {N(control2.X)} {N(control2.Y)} {N(to.Point.X)} {N(to.Point.Y)} ");
                }
            }

            if (sub.Closed)
            {
                data.Append("Z ");
            }
        }

        return data.ToString().TrimEnd();
    }

    private static string TransformAttribute(Matrix3x2 matrix)
    {
        if (matrix == Matrix3x2.Identity)
        {
            return string.Empty;
        }

        return $" transform=\"matrix({N(matrix.M11)} {N(matrix.M12)} {N(matrix.M21)} {N(matrix.M22)} {N(matrix.M31)} {N(matrix.M32)})\"";
    }

    private static string Hex(uint color) => Colour.ToHex(color);

    private static string N(float value) => MathF.Abs(value) < 0.0005f
        ? "0"
        : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
