using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Fill and stroke picker with palette and recent colours.</summary>
internal sealed class ColourPanel : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        var (target, setTarget) = State(0);

        PathObject? path = session.Selection.OfType<PathObject>().FirstOrDefault();
        TextObject? text = session.Selection.OfType<TextObject>().FirstOrDefault();
        uint fill = path?.Fill?.Color ?? text?.Fill?.Color ?? session.FillColor;
        uint stroke = path?.Stroke?.Paint.Color ?? session.StrokeColor;
        uint current = target == 0 ? fill : stroke;

        return C<Panel, PanelProps>(new PanelProps
        {
            Title = "Colour",
            Badge = target == 0 ? "Fill" : "Stroke",
            Children =
            [
                Div(new() { ClassName = "colour-targets" },
                    Button(new()
                    {
                        ClassName = target == 0 ? "colour-target is-active" : "colour-target",
                        Title = "Open the fill colour picker",
                        OnClick = () =>
                        {
                            setTarget(0);
                            session.OpenColourPicker(ColourTarget.Fill);
                        },
                    },
                    Ui.FaceSwatch(fill, path is not null && path.Fill is null, "Fill", () => session.OpenColourPicker(ColourTarget.Fill)),
                    Span(new() { ClassName = "colour-target-label" }, Text("Fill"))),
                    Button(new()
                    {
                        ClassName = target == 1 ? "colour-target is-active" : "colour-target",
                        Title = "Open the stroke colour picker",
                        OnClick = () =>
                        {
                            setTarget(1);
                            session.OpenColourPicker(ColourTarget.Stroke);
                        },
                    },
                    Ui.FaceSwatch(stroke, path is not null && path.Stroke is null, "Stroke", () => session.OpenColourPicker(ColourTarget.Stroke)),
                    Span(new() { ClassName = "colour-target-label" }, Text("Stroke")))),
                Div(new() { ClassName = "colour-preview" },
                    Div(new()
                    {
                        ClassName = "colour-preview-bar",
                        Title = "Open the colour picker",
                        OnClick = () => session.OpenColourPicker(target == 0 ? ColourTarget.Fill : ColourTarget.Stroke),
                        Attributes = new() { ["style"] = $"background-color:{Ui.Hex(current)}" },
                    }),
                    Input(new()
                    {
                        ClassName = "face-hex",
                        Type = "text",
                        Value = Ui.Hex(current),
                        OnChange = value =>
                        {
                            if (Colour.TryParseHex(value, out uint parsed))
                            {
                                Apply(session, target, parsed);
                            }
                        },
                        OnFocus = Ui.Focus(session),
                        OnBlur = Ui.Blur(session),
                    })),
                Show(session.RecentColours.Count > 0, F(
                    Div(new() { ClassName = "panel-hint" }, Text("Recent")),
                    Div(new() { ClassName = "swatch-grid" }, SwatchRows(session, session.RecentColours, 8, true, target)))),
                Show(session.DocumentColours.Count > 0, F(
                    Div(new() { ClassName = "panel-hint" }, Text("In document")),
                    Div(new() { ClassName = "swatch-grid" }, SwatchRows(session, session.DocumentColours, 8, true, target)))),
            ],
        });
    }

    private static Node[] SwatchRows(EditorSession session, IReadOnlyList<uint> colors, int perRow, bool recent, int target)
    {
        List<Node> rows = [];
        for (int start = 0; start < colors.Count; start += perRow)
        {
            int end = Math.Min(start + perRow, colors.Count);
            List<Node> cells = [];
            for (int i = start; i < end; i++)
            {
                uint color = colors[i];
                cells.Add(Ui.Swatch(color, Ui.Hex(color), () => Apply(session, target, color)));
            }

            rows.Add(Div(new()
            {
                ClassName = recent ? "swatch-row is-recent" : "swatch-row",
            }, cells.ToArray()));
        }

        return rows.ToArray();
    }

    private static void Apply(EditorSession session, int target, uint color)
    {
        if (session.Selection.Count == 0)
        {
            if (target == 0)
            {
                session.FillColor = color;
            }
            else
            {
                session.StrokeColor = color;
            }

            session.RecentColours.Remove(color);
            session.RecentColours.Insert(0, color);
            while (session.RecentColours.Count > 12)
            {
                session.RecentColours.RemoveAt(session.RecentColours.Count - 1);
            }

            session.Notify();
            return;
        }

        if (target == 0)
        {
            session.SetSelectionFill(color);
        }
        else
        {
            session.SetSelectionStroke(color);
        }
    }
}
