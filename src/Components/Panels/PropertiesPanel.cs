using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Transform and appearance for the current selection.</summary>
internal sealed class PropertiesPanel : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        return F(
            TransformSection(session),
            AppearanceSection(session));
    }

    private static Node TransformSection(EditorSession session)
    {
        bool hasSelection = session.Selection.Count > 0;
        Bounds bounds = session.SelectionBounds;

        Node content = hasSelection
            ? F(
                Div(new() { ClassName = "field-grid" },
                    Ui.NumField(session, "X", bounds.Left, text => session.SetSelectionPosition(Ui.Parse(text, bounds.Left), bounds.Top)),
                    Ui.NumField(session, "Y", bounds.Top, text => session.SetSelectionPosition(bounds.Left, Ui.Parse(text, bounds.Top))),
                    Ui.NumField(session, "W", bounds.Width, text => session.SetSelectionSize(Ui.Parse(text, bounds.Width), bounds.Height)),
                    Ui.NumField(session, "H", bounds.Height, text => session.SetSelectionSize(bounds.Width, Ui.Parse(text, bounds.Height)))),
                Div(new() { ClassName = "panel-hint" },
                    Text($"{session.Selection.Count} object{(session.Selection.Count == 1 ? "" : "s")} · {session.Selection[0].DisplayName}")))
            : F(
                Div(new() { ClassName = "field-grid" },
                    Ui.NumField(session, "W", session.Document.Active.Width, text => session.SetArtboardSize(Ui.Parse(text, session.Document.Active.Width), session.Document.Active.Height)),
                    Ui.NumField(session, "H", session.Document.Active.Height, text => session.SetArtboardSize(session.Document.Active.Width, Ui.Parse(text, session.Document.Active.Height)))),
                Div(new() { ClassName = "panel-hint" }, Text("Artboard size. Select an object to transform it.")));

        return C<Panel, PanelProps>(new PanelProps
        {
            Title = "Transform",
            Badge = hasSelection ? "Selection" : "Artboard",
            Children = [content],
        });
    }

    private static Node AppearanceSection(EditorSession session)
    {
        PathObject? path = session.Selection.OfType<PathObject>().FirstOrDefault();
        TextObject? text = session.Selection.OfType<TextObject>().FirstOrDefault();
        bool hasSelection = session.Selection.Count > 0;

        uint fillColor = path?.Fill?.Color ?? text?.Fill?.Color ?? session.FillColor;
        bool fillNone = hasSelection && path is not null && path.Fill is null;
        uint strokeColor = path?.Stroke?.Paint.Color ?? session.StrokeColor;
        float strokeWidth = path?.Stroke?.Width ?? session.StrokeWidth;
        bool strokeNone = hasSelection && path is not null && path.Stroke is null;
        float opacity = session.Selection.Count > 0 ? session.Selection[0].Opacity : 1f;

        List<Node> rows =
        [
            Div(new() { ClassName = "face-row" },
                Span(new() { ClassName = "face-label" }, Text("Fill")),
                Ui.FaceSwatch(fillColor, fillNone, "Fill colour", () => SelectDefaultFill(session, fillColor)),
                Input(new()
                {
                    ClassName = "face-hex",
                    Type = "text",
                    Value = fillNone ? "None" : Ui.Hex(fillColor),
                    OnChange = value => CommitFill(session, value),
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                }),
                Ui.MiniButton(Icons.Close, "No fill", () => ApplyNoneFill(session), enabled: hasSelection)),
            Div(new() { ClassName = "face-row" },
                Span(new() { ClassName = "face-label" }, Text("Stroke")),
                Ui.FaceSwatch(strokeColor, strokeNone, "Stroke colour", () => SelectDefaultStroke(session, strokeColor)),
                Input(new()
                {
                    ClassName = "face-hex",
                    Type = "text",
                    Value = strokeNone ? "None" : Ui.Hex(strokeColor),
                    OnChange = value => CommitStroke(session, value),
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                }),
                Ui.MiniButton(Icons.Close, "No stroke", () => ApplyNoneStroke(session), enabled: hasSelection)),
            Div(new() { ClassName = "field-grid" },
                Div(new() { ClassName = "field-row" },
                    Span(new() { ClassName = "field-key" }, Text("SW")),
                    Input(new()
                    {
                        ClassName = "num-field",
                        Type = "text",
                        Value = Ui.Number(strokeWidth),
                        OnChange = value => CommitStrokeWidth(session, Ui.Parse(value, strokeWidth)),
                        OnFocus = Ui.Focus(session),
                        OnBlur = Ui.Blur(session),
                    }))),
            Ui.Slider(session, "Opacity", opacity, 0f, 1f, "%", value =>
            {
                if (session.Selection.Count > 0)
                {
                    session.PreviewSelectionOpacity(Math.Clamp(Ui.Parse(value, opacity), 0f, 1f));
                }
            }, value =>
            {
                if (session.Selection.Count > 0)
                {
                    session.CommitSelectionOpacity(Math.Clamp(Ui.Parse(value, opacity), 0f, 1f));
                }
            }),
        ];

        if (path?.Shape?.Kind == ShapeKind.RoundedRectangle)
        {
            rows.Add(Div(new() { ClassName = "field-row" },
                Span(new() { ClassName = "field-key" }, Text("R")),
                Input(new()
                {
                    ClassName = "num-field",
                    Type = "text",
                    Value = Ui.Number(path.Shape.CornerRadius),
                    OnChange = value => session.SetSelectionCornerRadius(Ui.Parse(value, path.Shape.CornerRadius)),
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                })));
        }

        if (text is not null)
        {
            rows.Add(Div(new() { ClassName = "text-options" },
                Span(new() { ClassName = "text-option-label" }, Text("Font family")),
                Div(new()
                {
                    ClassName = "font-choice",
                    Title = "Search and choose a font family",
                    OnClick = session.OpenFontPicker,
                }, Text(text.FontFamily)),
                Div(new() { ClassName = "text-size-row" },
                    Span(new() { ClassName = "field-key" }, Text("Aa")),
                    Input(new()
                    {
                        ClassName = "num-field",
                        Type = "text",
                        Value = Ui.Number(text.FontSize),
                        OnChange = value => session.SetTextSize(Ui.Parse(value, text.FontSize)),
                        OnFocus = Ui.Focus(session),
                        OnBlur = Ui.Blur(session),
                    }),
                    Div(new()
                    {
                        ClassName = text.Bold ? "mini-btn is-on" : "mini-btn",
                        Title = "Bold",
                        OnClick = session.ToggleTextBold,
                    }, Span(new() { ClassName = "style-bold" }, Text("B"))),
                    Div(new()
                    {
                        ClassName = text.Italic ? "mini-btn is-on" : "mini-btn",
                        Title = "Italic",
                        OnClick = session.ToggleTextItalic,
                    }, Span(new() { ClassName = "style-italic" }, Text("I")))),
                Input(new()
                {
                    ClassName = "text-field-wide",
                    Type = "text",
                    Value = text.Text,
                    Placeholder = "Text content",
                    OnChange = session.SetTextContent,
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                })));
        }

        if (!hasSelection)
        {
            rows.Add(Div(new() { ClassName = "panel-hint" }, Text("Select an object to change its fill, stroke and opacity.")));
            rows.Add(Div(new() { ClassName = "face-row" },
                Span(new() { ClassName = "face-label" }, Text("Page")),
                Ui.FaceSwatch(session.Document.Active.Background, false, "Artboard background", () => session.OpenColourPicker(ColourTarget.Artboard)),
                Input(new()
                {
                    ClassName = "face-hex",
                    Type = "text",
                    Value = Ui.Hex(session.Document.Active.Background),
                    OnChange = value =>
                    {
                        if (Colour.TryParseHex(value, out uint color))
                        {
                            session.SetArtboardBackground(color);
                        }
                    },
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                })));
        }

        return C<Panel, PanelProps>(new PanelProps
        {
            Title = "Appearance",
            Badge = hasSelection ? "" : "Document",
            Children = [.. rows],
        });
    }

    private static void SelectDefaultFill(EditorSession session, uint color)
    {
        session.OpenColourPicker(ColourTarget.Fill);
    }

    private static void SelectDefaultStroke(EditorSession session, uint color)
    {
        session.OpenColourPicker(ColourTarget.Stroke);
    }

    private static void CommitFill(EditorSession session, string value)
    {
        if (!Colour.TryParseHex(value, out uint color))
        {
            return;
        }

        if (session.Selection.Count == 0)
        {
            session.FillColor = color;
            session.Notify();
            return;
        }

        session.SetSelectionFill(color);
    }

    private static void CommitStroke(EditorSession session, string value)
    {
        if (!Colour.TryParseHex(value, out uint color))
        {
            return;
        }

        if (session.Selection.Count == 0)
        {
            session.StrokeColor = color;
            session.Notify();
            return;
        }

        session.SetSelectionStroke(color);
    }

    private static void CommitStrokeWidth(EditorSession session, float width)
    {
        if (session.Selection.Count == 0)
        {
            session.StrokeWidth = MathF.Max(0f, width);
            session.Notify();
            return;
        }

        session.SetSelectionStrokeWidth(width);
    }

    private static void ApplyNoneFill(EditorSession session)
    {
        if (session.Selection.Count == 0)
        {
            session.FillColor = 0x00000000;
            session.Notify();
            return;
        }

        session.ClearSelectionFill();
    }

    private static void ApplyNoneStroke(EditorSession session)
    {
        if (session.Selection.Count == 0)
        {
            session.StrokeColor = 0x00000000;
            session.Notify();
            return;
        }

        session.ClearSelectionStroke();
    }
}
