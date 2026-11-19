using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VectorDotNet.Core;

/// <summary>Imports SVG documents into the editable vector model.</summary>
public static class SvgImporter
{
    private static readonly Regex NumberPattern = new(
        @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PathTokenPattern = new(
        @"[MmZzLlHhVvCcSsQqTtAa]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TransformPattern = new(
        @"([a-zA-Z]+)\s*\(([^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, uint> NamedColours = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0xFF000000, ["white"] = 0xFFFFFFFF, ["red"] = 0xFFE53935,
        ["green"] = 0xFF2E7D32, ["blue"] = 0xFF1E88E5, ["yellow"] = 0xFFFDD835,
        ["cyan"] = 0xFF00ACC1, ["aqua"] = 0xFF00ACC1, ["magenta"] = 0xFFD81B60,
        ["fuchsia"] = 0xFFD81B60, ["gray"] = 0xFF808080, ["grey"] = 0xFF808080,
        ["silver"] = 0xFFBDBDBD, ["maroon"] = 0xFF800000, ["olive"] = 0xFF808000,
        ["lime"] = 0xFF00E676, ["teal"] = 0xFF008080, ["navy"] = 0xFF1A237E,
        ["orange"] = 0xFFFB8C00, ["purple"] = 0xFF8E24AA, ["pink"] = 0xFFF06292,
        ["brown"] = 0xFF795548, ["transparent"] = 0x00000000,
    };

    /// <summary>Reads an SVG file into a new document.</summary>
    public static Document Load(string path)
    {
        XDocument xml;
        try
        {
            if (path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream file = File.OpenRead(path);
                using GZipStream gzip = new(file, CompressionMode.Decompress);
                xml = XDocument.Load(gzip);
            }
            else
            {
                xml = XDocument.Load(path);
            }
        }
        catch (XmlException error)
        {
            throw new InvalidDataException("The file is not valid SVG: " + error.Message, error);
        }

        XElement root = xml.Root ?? throw new InvalidDataException("The SVG file is empty.");
        if (!string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The file is not an SVG document.");
        }

        float viewX = 0f;
        float viewY = 0f;
        float viewWidth = ParseLength(root.Attribute("width")?.Value);
        float viewHeight = ParseLength(root.Attribute("height")?.Value);
        float[] viewBox = ParseNumbers(root.Attribute("viewBox")?.Value);
        if (viewBox.Length >= 4 && viewBox[2] > 0.1f && viewBox[3] > 0.1f)
        {
            viewX = viewBox[0];
            viewY = viewBox[1];
            viewWidth = viewBox[2];
            viewHeight = viewBox[3];
        }

        if (viewWidth <= 1f || viewHeight <= 1f)
        {
            viewWidth = 1200f;
            viewHeight = 800f;
        }

        float width = Math.Clamp(MathF.Round(viewWidth), 16f, 12000f);
        float height = Math.Clamp(MathF.Round(viewHeight), 16f, 12000f);

        Document document = new(width, height)
        {
            Name = Path.GetFileNameWithoutExtension(path),
        };

        Artboard artboard = document.Active;
        artboard.Width = width;
        artboard.Height = height;
        artboard.Name = document.Name;

        LayerObject layer = document.Layers.FirstOrDefault() ?? new LayerObject { Name = "Imported" };
        layer.Name = "Imported";
        document.ActiveLayer = layer;

        Matrix3x2 map = Matrix3x2.CreateScale(width / MathF.Max(1f, viewWidth), height / MathF.Max(1f, viewHeight))
            * Matrix3x2.CreateTranslation(-viewX * (width / MathF.Max(1f, viewWidth)), -viewY * (height / MathF.Max(1f, viewHeight)));
        layer.Transform = map;

        Dictionary<string, uint> gradients = CollectGradients(root);
        Dictionary<string, XElement> ids = new(StringComparer.Ordinal);
        foreach (XElement element in root.DescendantsAndSelf())
        {
            string? id = element.Attribute("id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids[id] = element;
            }
        }

        SvgStyle rootStyle = new();
        ApplyElementStyle(root, rootStyle, gradients);
        Walk(root, rootStyle, layer, gradients, ids, 0);

        return document;
    }

    private sealed class SvgStyle
    {
        public uint FillColour = 0xFF000000;
        public bool FillNone;
        public float FillOpacity = 1f;
        public uint StrokeColour = 0x00000000;
        public bool StrokeNone = true;
        public float StrokeOpacity = 1f;
        public float StrokeWidth = 1f;
        public FillRule Rule = FillRule.NonZero;
        public LineCap Cap = LineCap.Butt;
        public LineJoin Join = LineJoin.Miter;
        public float MiterLimit = 4f;
        public float[] Dash = [];
        public float Opacity = 1f;
        public bool Hidden;
        public string FontFamily = "Segoe UI";
        public float FontSize = 24f;
        public int FontWeight = 400;
        public bool Italic;
        public string TextAnchor = "start";

        public SvgStyle Copy() => (SvgStyle)MemberwiseClone();
    }

    private static void Walk(
        XElement element,
        SvgStyle parentStyle,
        GroupObject parent,
        Dictionary<string, uint> gradients,
        Dictionary<string, XElement> ids,
        int depth)
    {
        if (depth > 32)
        {
            return;
        }

        string name = element.Name.LocalName;
        if (name is "defs" or "style" or "title" or "desc" or "metadata" or "clipPath" or "mask")
        {
            return;
        }

        SvgStyle style = parentStyle.Copy();
        ApplyElementStyle(element, style, gradients);
        if (style.Hidden)
        {
            return;
        }

        Matrix3x2 local = ParseTransform(element.Attribute("transform")?.Value);
        GroupObject container = parent;

        if (name == "use")
        {
            string? href = element.Attribute("href")?.Value
                ?? element.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"))?.Value;
            if (href is not null && href.StartsWith('#') && ids.TryGetValue(href[1..], out XElement? target))
            {
                float x = ParseLength(element.Attribute("x")?.Value);
                float y = ParseLength(element.Attribute("y")?.Value);
                GroupObject useGroup = new()
                {
                    Name = "Use",
                    Transform = Mat.Translate(x, y) * local,
                    Opacity = style.Opacity,
                };
                parent.Add(useGroup);
                SvgStyle useStyle = style.Copy();
                useStyle.Opacity = 1f;
                Walk(target, useStyle, useGroup, gradients, ids, depth + 1);
            }

            return;
        }

        if (name is "g" or "svg" or "a" or "switch" or "symbol")
        {
            GroupObject group = new()
            {
                Name = element.Attribute("id")?.Value ?? "Group",
                Transform = local,
                Opacity = style.Opacity,
            };
            parent.Add(group);
            container = group;
            style.Opacity = 1f;
        }

        switch (name)
        {
            case "rect":
                AddRect(element, style, local, container);
                break;
            case "circle":
            case "ellipse":
                AddEllipse(element, name == "circle", style, local, container);
                break;
            case "line":
                AddLine(element, style, local, container);
                break;
            case "polyline":
            case "polygon":
                AddPolygon(element, name == "polygon", style, local, container);
                break;
            case "path":
                AddPath(element, style, local, container);
                break;
            case "text":
                AddText(element, style, local, container);
                break;
        }

        if (name is "g" or "svg" or "a" or "switch" or "symbol")
        {
            foreach (XElement child in element.Elements())
            {
                Walk(child, style, container, gradients, ids, depth + 1);
            }
        }
    }

    private static void ApplyElementStyle(XElement element, SvgStyle style, Dictionary<string, uint> gradients)
    {
        style.Opacity = 1f;
        if (element.Attribute("display")?.Value is "none" ||
            element.Attribute("visibility")?.Value is "hidden")
        {
            style.Hidden = true;
        }

        foreach (XAttribute attribute in element.Attributes())
        {
            ApplyDeclaration(style, attribute.Name.LocalName, attribute.Value, gradients);
        }

        string? inline = element.Attribute("style")?.Value;
        if (string.IsNullOrWhiteSpace(inline))
        {
            return;
        }

        foreach (string entry in inline.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            ApplyDeclaration(style, entry[..colon].Trim(), entry[(colon + 1)..].Trim(), gradients);
        }
    }

    private static void ApplyDeclaration(SvgStyle style, string name, string value, Dictionary<string, uint> gradients)
    {
        switch (name.ToLowerInvariant())
        {
            case "fill":
                if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                {
                    style.FillNone = true;
                }
                else if (string.Equals(value, "currentColor", StringComparison.OrdinalIgnoreCase))
                {
                    style.FillNone = false;
                    style.FillColour = 0xFF000000;
                }
                else if (TryParsePaint(value, gradients, out uint fill))
                {
                    style.FillNone = false;
                    style.FillColour = fill;
                }

                break;

            case "fill-opacity":
                style.FillOpacity = ParseOpacity(value, style.FillOpacity);
                break;

            case "stroke":
                if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                {
                    style.StrokeNone = true;
                }
                else if (TryParsePaint(value, gradients, out uint stroke))
                {
                    style.StrokeNone = false;
                    style.StrokeColour = stroke;
                }

                break;

            case "stroke-opacity":
                style.StrokeOpacity = ParseOpacity(value, style.StrokeOpacity);
                break;

            case "stroke-width":
                style.StrokeWidth = MathF.Max(0f, ParseLength(value));
                break;

            case "opacity":
                style.Opacity = ParseOpacity(value, style.Opacity);
                break;

            case "fill-rule":
                style.Rule = value.Contains("evenodd", StringComparison.OrdinalIgnoreCase) ? FillRule.EvenOdd : FillRule.NonZero;
                break;

            case "stroke-linecap":
                style.Cap = value switch
                {
                    "round" => LineCap.Round,
                    "square" => LineCap.Square,
                    _ => LineCap.Butt,
                };
                break;

            case "stroke-linejoin":
                style.Join = value switch
                {
                    "round" => LineJoin.Round,
                    "bevel" => LineJoin.Bevel,
                    _ => LineJoin.Miter,
                };
                break;

            case "stroke-miterlimit":
                style.MiterLimit = MathF.Max(1f, ParseLength(value));
                break;

            case "stroke-dasharray":
                style.Dash = ParseNumbers(value);
                break;

            case "font-family":
                style.FontFamily = value.Trim('\'', '"');
                break;

            case "font-size":
                style.FontSize = ParseFontSize(value, style.FontSize);
                break;

            case "font-weight":
                if (value.Equals("bold", StringComparison.OrdinalIgnoreCase))
                {
                    style.FontWeight = 700;
                }
                else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                {
                    style.FontWeight = Math.Clamp(weight, 100, 900);
                }

                break;

            case "font-style":
                style.Italic = value.Contains("italic", StringComparison.OrdinalIgnoreCase);
                break;

            case "text-anchor":
                style.TextAnchor = value;
                break;
        }
    }

    private static float ParseFontSize(string value, float parent)
    {
        string clean = value.Trim();
        if (clean.EndsWith('%') && float.TryParse(clean[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
        {
            return parent * percent / 100f;
        }

        if (clean.EndsWith("em", StringComparison.OrdinalIgnoreCase) && float.TryParse(clean[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out float em))
        {
            return parent * em;
        }

        float size = ParseLength(clean);
        return size > 0.1f ? size : parent;
    }

    private static void AddRect(XElement element, SvgStyle style, Matrix3x2 transform, GroupObject parent)
    {
        float x = ParseLength(element.Attribute("x")?.Value);
        float y = ParseLength(element.Attribute("y")?.Value);
        float width = ParseLength(element.Attribute("width")?.Value);
        float height = ParseLength(element.Attribute("height")?.Value);
        if (width <= 0.01f || height <= 0.01f)
        {
            return;
        }

        float rx = ParseLength(element.Attribute("rx")?.Value);
        float ry = ParseLength(element.Attribute("ry")?.Value);
        float radius = MathF.Max(rx, ry);
        VPath path = radius > 0.01f
            ? VPath.RoundedRectangle(new Bounds(x, y, width, height), radius)
            : VPath.Rectangle(new Bounds(x, y, width, height));
        AddObject(path, style, transform, parent, "Rectangle");
    }

    private static void AddEllipse(XElement element, bool circle, SvgStyle style, Matrix3x2 transform, GroupObject parent)
    {
        float cx = ParseLength(element.Attribute("cx")?.Value);
        float cy = ParseLength(element.Attribute("cy")?.Value);
        float rx = ParseLength(element.Attribute(circle ? "r" : "rx")?.Value);
        float ry = circle ? rx : ParseLength(element.Attribute("ry")?.Value);
        if (rx <= 0.01f || ry <= 0.01f)
        {
            return;
        }

        AddObject(VPath.Ellipse(new Bounds(cx - rx, cy - ry, rx * 2f, ry * 2f)), style, transform, parent, circle ? "Circle" : "Ellipse");
    }

    private static void AddLine(XElement element, SvgStyle style, Matrix3x2 transform, GroupObject parent)
    {
        Vec2 from = new(ParseLength(element.Attribute("x1")?.Value), ParseLength(element.Attribute("y1")?.Value));
        Vec2 to = new(ParseLength(element.Attribute("x2")?.Value), ParseLength(element.Attribute("y2")?.Value));
        if (style.StrokeNone)
        {
            return;
        }

        AddObject(VPath.Line(from, to), style, transform, parent, "Line");
    }

    private static void AddPolygon(XElement element, bool closed, SvgStyle style, Matrix3x2 transform, GroupObject parent)
    {
        float[] points = ParseNumbers(element.Attribute("points")?.Value);
        if (points.Length < 4)
        {
            return;
        }

        VPath path = new();
        VSubPath sub = path.Add(new VSubPath(closed));
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            sub.Add(PathNode.Corner(new Vec2(points[i], points[i + 1])));
        }

        AddObject(path, style, transform, parent, closed ? "Polygon" : "Polyline");
    }

    private static void AddPath(XElement element, SvgStyle style, Matrix3x2 transform, GroupObject parent)
    {
        string data = element.Attribute("d")?.Value ?? string.Empty;
        VPath path = ParsePathData(data);
        if (path.SubPaths.Count == 0)
        {
            return;
        }

        path.FillRule = style.Rule;
        AddObject(path, style, transform, parent, element.Attribute("id")?.Value ?? "Path");
    }

    private static void AddText(XElement element, SvgStyle style, Matrix3x2 transform, GroupObject parent)
    {
        string text = CollectText(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        float x = ParseLength(element.Attribute("x")?.Value);
        float y = ParseLength(element.Attribute("y")?.Value);
        float estimatedWidth = text.Split('\n').Max(line => line.Length) * style.FontSize * 0.56f;
        x -= style.TextAnchor switch
        {
            "middle" => estimatedWidth * 0.5f,
            "end" => estimatedWidth,
            _ => 0f,
        };

        TextObject created = new()
        {
            Text = text,
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            Bold = style.FontWeight >= 600,
            Italic = style.Italic,
            Fill = style.FillNone ? null : Paint.Solid(Colour.Fade(style.FillColour, style.FillOpacity)),
            Opacity = style.Opacity,
            Transform = transform * Mat.Translate(x, y - (style.FontSize * 0.82f)),
        };
        parent.Add(created);
    }

    private static string CollectText(XElement element)
    {
        List<string> lines = [];
        foreach (XElement span in element.Elements().Where(child => child.Name.LocalName == "tspan"))
        {
            string text = string.Concat(span.DescendantNodes().OfType<XText>().Select(node => node.Value));
            if (text.Length > 0)
            {
                lines.Add(System.Net.WebUtility.HtmlDecode(text));
            }
        }

        if (lines.Count == 0)
        {
            string text = string.Concat(element.DescendantNodes().OfType<XText>().Select(node => node.Value));
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }

        return string.Join('\n', lines).Trim();
    }

    private static void AddObject(VPath path, SvgStyle style, Matrix3x2 transform, GroupObject parent, string name)
    {
        PathObject created = new()
        {
            Name = name,
            Path = path,
            Transform = transform,
            Opacity = style.Opacity,
            Fill = style.FillNone ? null : Paint.Solid(Colour.Fade(style.FillColour, style.FillOpacity)),
            Stroke = style.StrokeNone || style.StrokeWidth <= 0f
                ? null
                : new StrokeStyle
                {
                    Paint = Paint.Solid(Colour.Fade(style.StrokeColour, style.StrokeOpacity)),
                    Width = style.StrokeWidth,
                    Cap = style.Cap,
                    Join = style.Join,
                    MiterLimit = style.MiterLimit,
                    Dash = style.Dash,
                },
        };
        parent.Add(created);
    }

    private static Dictionary<string, uint> CollectGradients(XElement root)
    {
        Dictionary<string, uint> gradients = new(StringComparer.Ordinal);
        foreach (XElement element in root.Descendants())
        {
            if (element.Name.LocalName is not ("linearGradient" or "radialGradient"))
            {
                continue;
            }

            string? id = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            List<(float Offset, uint Colour)> stops = [];
            foreach (XElement stop in element.Descendants().Where(child => child.Name.LocalName == "stop"))
            {
                float offset = ParseOffset(stop.Attribute("offset")?.Value);
                uint colour = ParseColour(stop.Attribute("stop-color")?.Value) ?? 0xFF000000;
                float opacity = ParseOpacity(stop.Attribute("stop-opacity")?.Value, 1f);
                stops.Add((offset, Colour.Fade(colour, opacity)));
            }

            if (stops.Count == 0)
            {
                continue;
            }

            stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            gradients[id] = stops[Math.Min(stops.Count - 1, stops.Count / 2)].Colour;
        }

        return gradients;
    }

    private static float ParseOffset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0f;
        }

        string clean = text.Trim();
        return clean.EndsWith('%')
            ? ParseLength(clean[..^1]) / 100f
            : ParseLength(clean);
    }

    private static bool TryParsePaint(string value, Dictionary<string, uint> gradients, out uint colour)
    {
        colour = 0xFF000000;
        string clean = value.Trim();
        if (clean.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            int hash = clean.IndexOf('#');
            int close = clean.IndexOf(')', hash + 1);
            if (hash >= 0 && close > hash)
            {
                string id = clean[(hash + 1)..close].Trim('\'', '"');
                if (gradients.TryGetValue(id, out colour))
                {
                    return true;
                }
            }

            return false;
        }

        uint? parsed = ParseColour(clean);
        if (parsed is null)
        {
            return false;
        }

        colour = parsed.Value;
        return true;
    }

    private static uint? ParseColour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string clean = value.Trim();
        if (clean.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            float[] parts = ParseNumbers(clean);
            if (parts.Length >= 3)
            {
                int alpha = parts.Length >= 4 ? (int)MathF.Round(parts[3] * 255f) : 255;
                return Colour.Rgba((int)parts[0], (int)parts[1], (int)parts[2], alpha);
            }

            return null;
        }

        if (clean.StartsWith('#') && Colour.TryParseHex(clean, out uint hex))
        {
            return hex;
        }

        return NamedColours.TryGetValue(clean, out uint named) ? named : null;
    }

    private static float ParseOpacity(string? value, float fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string clean = value.Trim();
        if (clean.EndsWith('%') && float.TryParse(clean[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent))
        {
            return Math.Clamp(percent / 100f, 0f, 1f);
        }

        return float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? Math.Clamp(parsed, 0f, 1f)
            : fallback;
    }

    private static float ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0f;
        }

        Match match = NumberPattern.Match(value);
        return match.Success && float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0f;
    }

    private static float[] ParseNumbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        MatchCollection matches = NumberPattern.Matches(value);
        float[] numbers = new float[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            _ = float.TryParse(matches[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]);
        }

        return numbers;
    }

    private static Matrix3x2 ParseTransform(string? value)
    {
        Matrix3x2 result = Matrix3x2.Identity;
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (Match match in TransformPattern.Matches(value))
        {
            float[] args = ParseNumbers(match.Groups[2].Value);
            string name = match.Groups[1].Value.ToLowerInvariant();
            Matrix3x2 matrix = name switch
            {
                "matrix" when args.Length >= 6 => new Matrix3x2(args[0], args[1], args[2], args[3], args[4], args[5]),
                "translate" when args.Length >= 1 => Matrix3x2.CreateTranslation(args[0], args.Length >= 2 ? args[1] : 0f),
                "scale" when args.Length >= 1 => Matrix3x2.CreateScale(args[0], args.Length >= 2 ? args[1] : args[0]),
                "rotate" when args.Length >= 1 => args.Length >= 3
                    ? Mat.Translate(args[1], args[2]) * Mat.Rotate(args[0] * MathF.PI / 180f) * Mat.Translate(-args[1], -args[2])
                    : Mat.Rotate(args[0] * MathF.PI / 180f),
                "skewx" when args.Length >= 1 => new Matrix3x2(1f, 0f, MathF.Tan(args[0] * MathF.PI / 180f), 1f, 0f, 0f),
                "skewy" when args.Length >= 1 => new Matrix3x2(1f, MathF.Tan(args[0] * MathF.PI / 180f), 0f, 1f, 0f, 0f),
                _ => Matrix3x2.Identity,
            };
            result = matrix * result;
        }

        return result;
    }

    private static VPath ParsePathData(string data)
    {
        VPath path = new();
        if (string.IsNullOrWhiteSpace(data))
        {
            return path;
        }

        MatchCollection tokens = PathTokenPattern.Matches(data);
        int index = 0;
        Vec2 current = Vec2.Zero;
        Vec2 start = Vec2.Zero;
        Vec2 lastCubic = Vec2.Zero;
        Vec2 lastQuad = Vec2.Zero;
        char command = 'M';
        char previousCommand = 'M';
        VSubPath? sub = null;

        Vec2 Point()
        {
            float x = Number(tokens, ref index);
            float y = Number(tokens, ref index);
            return new Vec2(x, y);
        }

        void EnsureSubpath(bool close)
        {
            sub ??= path.Add(new VSubPath(close));
        }

        void AddLineTo(Vec2 point)
        {
            EnsureSubpath(false);
            sub!.Add(PathNode.Corner(point));
            current = point;
        }

        void AddCubicTo(Vec2 control1, Vec2 control2, Vec2 point)
        {
            EnsureSubpath(false);
            if (sub!.Nodes.Count == 0)
            {
                sub.Add(PathNode.Corner(current));
            }

            PathNode from = sub.Nodes[^1];
            from.Out = control1;
            from.HasOut = true;
            sub.Nodes[^1] = from;
            sub.Add(PathNode.Smooth(point, control2, control2));
            PathNode added = sub.Nodes[^1];
            added.HasOut = false;
            sub.Nodes[^1] = added;
            current = point;
        }

        while (index < tokens.Count)
        {
            string token = tokens[index].Value;
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                command = token[0];
                index++;
            }

            bool relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    Vec2 point = Point();
                    if (relative)
                    {
                        point += current;
                    }

                    current = point;
                    start = point;
                    sub = path.Add(new VSubPath());
                    sub.Add(PathNode.Corner(point));
                    command = relative ? 'l' : 'L';
                    break;
                }

                case 'L':
                {
                    Vec2 point = Point();
                    AddLineTo(relative ? point + current : point);
                    break;
                }

                case 'H':
                {
                    float x = Number(tokens, ref index);
                    AddLineTo(new Vec2(relative ? current.X + x : x, current.Y));
                    break;
                }

                case 'V':
                {
                    float y = Number(tokens, ref index);
                    AddLineTo(new Vec2(current.X, relative ? current.Y + y : y));
                    break;
                }

                case 'C':
                {
                    Vec2 c1 = Point();
                    Vec2 c2 = Point();
                    Vec2 point = Point();
                    if (relative)
                    {
                        c1 += current;
                        c2 += current;
                        point += current;
                    }

                    AddCubicTo(c1, c2, point);
                    lastCubic = c2;
                    break;
                }

                case 'S':
                {
                    Vec2 c2 = Point();
                    Vec2 point = Point();
                    if (relative)
                    {
                        c2 += current;
                        point += current;
                    }

                    Vec2 c1 = char.ToUpperInvariant(previousCommand) is 'C' or 'S'
                        ? current + (current - lastCubic)
                        : current;
                    AddCubicTo(c1, c2, point);
                    lastCubic = c2;
                    break;
                }

                case 'Q':
                {
                    Vec2 control = Point();
                    Vec2 point = Point();
                    if (relative)
                    {
                        control += current;
                        point += current;
                    }

                    AddCubicTo(
                        current + ((control - current) * (2f / 3f)),
                        point + ((control - point) * (2f / 3f)),
                        point);
                    lastQuad = control;
                    break;
                }

                case 'T':
                {
                    Vec2 point = Point();
                    if (relative)
                    {
                        point += current;
                    }

                    Vec2 control = char.ToUpperInvariant(previousCommand) is 'Q' or 'T'
                        ? current + (current - lastQuad)
                        : current;
                    AddCubicTo(
                        current + ((control - current) * (2f / 3f)),
                        point + ((control - point) * (2f / 3f)),
                        point);
                    lastQuad = control;
                    break;
                }

                case 'A':
                {
                    float rx = Number(tokens, ref index);
                    float ry = Number(tokens, ref index);
                    float rotation = Number(tokens, ref index);
                    bool largeArc = Number(tokens, ref index) != 0f;
                    bool sweep = Number(tokens, ref index) != 0f;
                    Vec2 point = Point();
                    if (relative)
                    {
                        point += current;
                    }

                    EnsureSubpath(false);
                    if (sub!.Nodes.Count == 0)
                    {
                        sub.Add(PathNode.Corner(current));
                    }

                    AddArc(sub!, current, rx, ry, rotation, largeArc, sweep, point);
                    current = point;
                    break;
                }

                case 'Z':
                {
                    if (sub is not null)
                    {
                        sub.Closed = true;
                        if (sub.Nodes.Count > 1 && Vec2.Distance(sub.Nodes[^1].Point, sub.Nodes[0].Point) < 0.01f)
                        {
                            // The final curve arrives back at the first anchor.
                            // Move its incoming handle onto that anchor before
                            // dropping the duplicate, otherwise the closing
                            // segment degrades to a straight line.
                            PathNode first = sub.Nodes[0];
                            PathNode last = sub.Nodes[^1];
                            if (last.HasIn)
                            {
                                first.In = last.In;
                                first.HasIn = true;
                                sub.Nodes[0] = first;
                            }

                            sub.Nodes.RemoveAt(sub.Nodes.Count - 1);
                        }
                    }

                    current = start;
                    sub = null;
                    break;
                }

                default:
                    index++;
                    break;
            }

            previousCommand = command;
        }

        return path;
    }

    private static float Number(MatchCollection tokens, ref int index)
    {
        if (index >= tokens.Count)
        {
            return 0f;
        }

        return float.TryParse(tokens[index++].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;
    }

    private static void AddArc(
        VSubPath sub,
        Vec2 from,
        float rx,
        float ry,
        float rotationDegrees,
        bool largeArc,
        bool sweep,
        Vec2 to)
    {
        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);
        if (rx < 0.01f || ry < 0.01f || Vec2.Distance(from, to) < 0.001f)
        {
            sub.Add(PathNode.Corner(to));
            return;
        }

        float phi = rotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(phi);
        float sin = MathF.Sin(phi);

        Vec2 delta = (from - to) * 0.5f;
        Vec2 local = new(
            (cos * delta.X) + (sin * delta.Y),
            (-sin * delta.X) + (cos * delta.Y));

        float lambda = ((local.X * local.X) / (rx * rx)) + ((local.Y * local.Y) / (ry * ry));
        if (lambda > 1f)
        {
            float scale = MathF.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        float numerator = MathF.Max(0f, (rx * rx * ry * ry) - (rx * rx * local.Y * local.Y) - (ry * ry * local.X * local.X));
        float denominator = (rx * rx * local.Y * local.Y) + (ry * ry * local.X * local.X);
        float factor = denominator <= 0.0001f ? 0f : MathF.Sqrt(numerator / denominator);
        if (largeArc == sweep)
        {
            factor = -factor;
        }

        Vec2 centreLocal = new(
            factor * rx * local.Y / ry,
            -factor * ry * local.X / rx);

        Vec2 centre = new(
            (cos * centreLocal.X) - (sin * centreLocal.Y) + ((from.X + to.X) * 0.5f),
            (sin * centreLocal.X) + (cos * centreLocal.Y) + ((from.Y + to.Y) * 0.5f));

        Vec2 Angle(Vec2 point)
        {
            Vec2 v = new(
                (cos * (point.X - centre.X)) + (sin * (point.Y - centre.Y)),
                (-sin * (point.X - centre.X)) + (cos * (point.Y - centre.Y)));
            return new Vec2(v.X / rx, v.Y / ry);
        }

        Vec2 startVector = Angle(from);
        Vec2 endVector = Angle(to);
        float startAngle = MathF.Atan2(startVector.Y, startVector.X);
        float endAngle = MathF.Atan2(endVector.Y, endVector.X);
        float sweepAngle = endAngle - startAngle;

        if (!sweep && sweepAngle > 0f)
        {
            sweepAngle -= MathF.PI * 2f;
        }
        else if (sweep && sweepAngle < 0f)
        {
            sweepAngle += MathF.PI * 2f;
        }

        int segments = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweepAngle) / (MathF.PI * 0.5f)));
        float step = sweepAngle / segments;

        Vec2 PointAt(float angle) => new(
            centre.X + (rx * MathF.Cos(angle) * cos) - (ry * MathF.Sin(angle) * sin),
            centre.Y + (rx * MathF.Cos(angle) * sin) + (ry * MathF.Sin(angle) * cos));

        Vec2 Derivative(float angle) => new(
            (-rx * MathF.Sin(angle) * cos) - (ry * MathF.Cos(angle) * sin),
            (-rx * MathF.Sin(angle) * sin) + (ry * MathF.Cos(angle) * cos));

        float alpha = (4f / 3f) * MathF.Tan(step / 4f);
        Vec2 startPoint = from;

        for (int i = 0; i < segments; i++)
        {
            float a1 = startAngle + (step * i);
            float a2 = a1 + step;
            Vec2 p2 = i == segments - 1 ? to : PointAt(a2);
            Vec2 c1 = startPoint + (Derivative(a1) * alpha);
            Vec2 c2 = p2 - (Derivative(a2) * alpha);

            PathNode node = sub.Nodes[^1];
            node.Out = c1;
            node.HasOut = true;
            sub.Nodes[^1] = node;
            sub.Add(PathNode.Smooth(p2, c2, c2));
            PathNode added = sub.Nodes[^1];
            added.HasOut = false;
            sub.Nodes[^1] = added;

            startPoint = p2;
        }
    }
}
