using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Left rail: the drawing, selection and navigation tools.</summary>
internal sealed class ToolRail : Component
{
    private sealed record ToolEntry(ToolKind Tool, string Icon, string Shortcut, bool StartsGroup = false);

    private static readonly ToolEntry[] Tools =
    [
        new(ToolKind.Select, Icons.Select, "V"),
        new(ToolKind.Direct, Icons.Direct, "A"),
        new(ToolKind.Pen, Icons.Pen, "P", StartsGroup: true),
        new(ToolKind.Pencil, Icons.Pencil, "N"),
        new(ToolKind.Lasso, Icons.Lasso, "Q"),
        new(ToolKind.Rectangle, Icons.Rectangle, "M", StartsGroup: true),
        new(ToolKind.RoundedRectangle, Icons.RoundedRectangle, "R"),
        new(ToolKind.Ellipse, Icons.Ellipse, "E"),
        new(ToolKind.Polygon, Icons.Polygon, "G"),
        new(ToolKind.Star, Icons.Star, "S"),
        new(ToolKind.Line, Icons.Line, "L"),
        new(ToolKind.Spiral, Icons.Spiral, ""),
        new(ToolKind.Text, Icons.Text, "T", StartsGroup: true),
        new(ToolKind.Zoom, Icons.Zoom, "Z", StartsGroup: true),
        new(ToolKind.Hand, Icons.Hand, "H"),
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);

        return Div(new() { ClassName = "rail" },
            Each(Tools, entry => (Node)ToolButton(session, entry)),
            Div(new() { ClassName = "rail-colours" },
                Div(new()
                {
                    ClassName = "rail-swatch is-fill",
                    Title = "Fill colour picker",
                    OnClick = () => session.OpenColourPicker(ColourTarget.Fill),
                    Attributes = new() { ["style"] = $"background-color:{Ui.Hex(session.FillColor)}" },
                }),
                Div(new()
                {
                    ClassName = "rail-swatch is-stroke",
                    Title = "Stroke colour picker",
                    OnClick = () => session.OpenColourPicker(ColourTarget.Stroke),
                    Attributes = new() { ["style"] = $"background-color:{Ui.Hex(session.StrokeColor)}" },
                }),
                Ui.IconButton(Icons.Swap, "Swap fill and stroke", () =>
                {
                    (session.FillColor, session.StrokeColor) = (session.StrokeColor, session.FillColor);
                    session.Notify();
                }, className: "mini-btn rail-swap")));
    }

    private static Node ToolButton(EditorSession session, ToolEntry entry)
    {
        string title = entry.Shortcut.Length == 0
            ? entry.Tool.ToString()
            : $"{SpaceName(entry.Tool)} ({entry.Shortcut})";

        return F(
            Show(entry.StartsGroup, Div(new() { ClassName = "rail-sep" })),
            Button(new()
            {
                ClassName = session.Tool == entry.Tool ? "tool-btn is-active" : "tool-btn",
                Title = title,
                OnClick = () => session.SetTool(entry.Tool),
            },
            entry.Tool == ToolKind.Direct
                ? Span(new() { ClassName = "tool-glyph is-direct" }, Ui.Icon(Icons.Select))
                : Ui.Icon(entry.Icon)));
    }

    private static string SpaceName(ToolKind tool) => tool switch
    {
        ToolKind.Direct => "Direct Selection",
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
        ToolKind.Hand => "Hand",
        ToolKind.Pen => "Pen",
        ToolKind.Pencil => "Pencil",
        _ => "Selection",
    };
}
