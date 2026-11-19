using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>
/// A full colour picker with a saturation/value field, hue and alpha strips,
/// hexadecimal and RGB entry, and colour history. Opening it previews changes
/// live on the canvas; Apply records one undo step and Cancel restores.
/// </summary>
internal sealed class ColourPicker : Component
{
    private const int FieldWidth = 348;
    private const int FieldHeight = 170;
    private const int StripHeight = 14;

    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        Colour.ToHsv(session.ColourPickerInitial, out float initialHue, out float initialSaturation, out float initialValue);
        float initialAlpha = Colour.A(session.ColourPickerInitial) / 255f;

        var (hue, setHue) = State(initialHue);
        var (saturation, setSaturation) = State(initialSaturation);
        var (value, setValue) = State(initialValue);
        var (alpha, setAlpha) = State(initialAlpha);
        Ref<bool> draggingField = UseRef(false);
        Ref<bool> draggingHue = UseRef(false);
        Ref<bool> draggingAlpha = UseRef(false);

        uint current = Colour.FromHsv(hue, saturation, value, alpha);

        void ApplyHsv(float h, float s, float v, float a)
        {
            setHue(h);
            setSaturation(s);
            setValue(v);
            setAlpha(a);
            session.PreviewColour(Colour.FromHsv(h, s, v, a));
        }

        void SetSv(float x, float y)
        {
            float s = Math.Clamp(x / FieldWidth, 0f, 1f);
            float v = 1f - Math.Clamp(y / FieldHeight, 0f, 1f);
            ApplyHsv(hue, s, v, alpha);
        }

        void SetHue(float x)
        {
            ApplyHsv(Math.Clamp(x / FieldWidth, 0f, 1f) * 360f, saturation, value, alpha);
        }

        void SetAlpha(float x)
        {
            ApplyHsv(hue, saturation, value, Math.Clamp(x / FieldWidth, 0f, 1f));
        }

        void ApplyColour(uint colour)
        {
            Colour.ToHsv(colour, out float h, out float s, out float v);
            ApplyHsv(h, s, v, Colour.A(colour) / 255f);
        }

        Node ChannelField(string label, int channelValue, Action<string> commit) =>
            Div(new() { ClassName = "picker-channel" },
                Span(new() { ClassName = "picker-channel-label" }, Text(label)),
                Input(new()
                {
                    ClassName = "picker-channel-input",
                    Type = "text",
                    Value = channelValue.ToString(),
                    OnInput = commit,
                    OnChange = commit,
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                }));

        return Div(new() { ClassName = "modal colour-picker-modal", OnClick = () => { } },
            Div(new() { ClassName = "modal-head" },
                Span(new() { ClassName = "modal-title" }, Text(session.ColourPickerTitle)),
                Ui.IconButton(Icons.Close, "Cancel", session.CancelColourPicker)),
            Div(new() { ClassName = "picker-body" },
                Div(new() { ClassName = "picker-preview" },
                    Div(new() { ClassName = "picker-preview-half" },
                        Div(new()
                        {
                            ClassName = "picker-preview-swatch",
                            Attributes = new() { ["style"] = $"background-color:{Ui.Hex(session.ColourPickerInitial)}" },
                        }),
                        Span(new() { ClassName = "picker-preview-label" }, Text("Original"))),
                    Div(new() { ClassName = "picker-preview-half" },
                        Div(new()
                        {
                            ClassName = "picker-preview-swatch",
                            Attributes = new() { ["style"] = $"background-color:{Ui.Hex(current)}" },
                        }),
                        Span(new() { ClassName = "picker-preview-label" }, Text("New")))),

                Canvas(new CanvasProps
                {
                    ClassName = "picker-field",
                    Attributes = Ui.Size(FieldWidth, FieldHeight),
                    OnDraw = context => DrawField(context, hue, saturation, value),
                    OnPointerDown = (x, y) =>
                    {
                        draggingField.Value = true;
                        SetSv(x, y);
                    },
                    OnPointerMove = (x, y) =>
                    {
                        if (draggingField.Value)
                        {
                            SetSv(x, y);
                        }
                    },
                    OnPointerUp = (x, y) =>
                    {
                        draggingField.Value = false;
                        SetSv(x, y);
                    },
                }),

                Canvas(new CanvasProps
                {
                    ClassName = "picker-strip",
                    Attributes = Ui.Size(FieldWidth, StripHeight),
                    OnDraw = context => DrawHueStrip(context, hue),
                    OnPointerDown = (x, y) =>
                    {
                        draggingHue.Value = true;
                        SetHue(x);
                    },
                    OnPointerMove = (x, y) =>
                    {
                        if (draggingHue.Value)
                        {
                            SetHue(x);
                        }
                    },
                    OnPointerUp = (x, y) =>
                    {
                        draggingHue.Value = false;
                        SetHue(x);
                    },
                }),

                Canvas(new CanvasProps
                {
                    ClassName = "picker-strip",
                    Attributes = Ui.Size(FieldWidth, StripHeight),
                    OnDraw = context => DrawAlphaStrip(context, hue, saturation, value, alpha),
                    OnPointerDown = (x, y) =>
                    {
                        draggingAlpha.Value = true;
                        SetAlpha(x);
                    },
                    OnPointerMove = (x, y) =>
                    {
                        if (draggingAlpha.Value)
                        {
                            SetAlpha(x);
                        }
                    },
                    OnPointerUp = (x, y) =>
                    {
                        draggingAlpha.Value = false;
                        SetAlpha(x);
                    },
                }),

                Div(new() { ClassName = "picker-fields" },
                    Input(new()
                    {
                        ClassName = "picker-hex",
                        Type = "text",
                        Value = Ui.Hex(current),
                        OnInput = text =>
                        {
                            if (Colour.TryParseHex(text, out uint parsed))
                            {
                                ApplyColour(Colour.WithAlpha(parsed, (int)MathF.Round(alpha * 255f)));
                            }
                        },
                        OnChange = text =>
                        {
                            if (Colour.TryParseHex(text, out uint parsed))
                            {
                                ApplyColour(Colour.WithAlpha(parsed, (int)MathF.Round(alpha * 255f)));
                            }
                        },
                        OnFocus = Ui.Focus(session),
                        OnBlur = Ui.Blur(session),
                    }),
                    ChannelField("R", Colour.R(current), text =>
                    {
                        if (int.TryParse(text, out int channel))
                        {
                            ApplyColour(Colour.Rgba(Math.Clamp(channel, 0, 255), Colour.G(current), Colour.B(current), (int)MathF.Round(alpha * 255f)));
                        }
                    }),
                    ChannelField("G", Colour.G(current), text =>
                    {
                        if (int.TryParse(text, out int channel))
                        {
                            ApplyColour(Colour.Rgba(Colour.R(current), Math.Clamp(channel, 0, 255), Colour.B(current), (int)MathF.Round(alpha * 255f)));
                        }
                    }),
                    ChannelField("B", Colour.B(current), text =>
                    {
                        if (int.TryParse(text, out int channel))
                        {
                            ApplyColour(Colour.Rgba(Colour.R(current), Colour.G(current), Math.Clamp(channel, 0, 255), (int)MathF.Round(alpha * 255f)));
                        }
                    }),
                    ChannelField("A", (int)MathF.Round(alpha * 100f), text =>
                    {
                        if (float.TryParse(text, out float percent))
                        {
                            ApplyHsv(hue, saturation, value, Math.Clamp(percent, 0f, 100f) / 100f);
                        }
                    })),

                Show(session.RecentColours.Count > 0, F(
                    Div(new() { ClassName = "picker-section-title" }, Text("Recent")),
                    Div(new() { ClassName = "picker-swatch-list" }, SwatchRows(session.RecentColours, ApplyColour)))),

                Show(session.DocumentColours.Count > 0, F(
                    Div(new() { ClassName = "picker-section-title" }, Text("In document")),
                    Div(new() { ClassName = "picker-swatch-list" }, SwatchRows(session.DocumentColours, ApplyColour)))),

                Div(new() { ClassName = "modal-actions" },
                    Button(new() { ClassName = "btn", OnClick = session.CancelColourPicker }, Text("Cancel")),
                    Button(new() { ClassName = "btn is-primary", OnClick = () => session.CommitColour(current) }, Text("Apply")))));
    }

    private static Node Swatch(uint colour, Action select) =>
        Div(new()
        {
            ClassName = "picker-swatch",
            Title = Ui.Hex(colour),
            OnClick = select,
            Attributes = new() { ["style"] = $"background-color:{Ui.Hex(colour)}" },
        });

    private static Node[] SwatchRows(IReadOnlyList<uint> colours, Action<uint> select)
    {
        List<Node> rows = [];
        for (int start = 0; start < colours.Count; start += 12)
        {
            int end = Math.Min(start + 12, colours.Count);
            List<Node> cells = [];
            for (int i = start; i < end; i++)
            {
                uint colour = colours[i];
                cells.Add(Swatch(colour, () => select(colour)));
            }

            rows.Add(Div(new() { ClassName = "picker-swatch-row" }, cells.ToArray()));
        }

        return rows.ToArray();
    }

    private static void DrawField(CanvasContext context, float hue, float saturation, float value)
    {
        context.Clear(0xFF000000);

        for (int x = 0; x < FieldWidth; x += 2)
        {
            uint band = Colour.FromHsv(hue, x / (float)FieldWidth, 1f);
            context.FillRect(x, 0, 2f, FieldHeight, band);
        }

        for (int y = 0; y < FieldHeight; y += 2)
        {
            int shade = (int)(y / (float)FieldHeight * 255f);
            context.FillRect(0, y, FieldWidth, 2f, (uint)shade << 24);
        }

        float markerX = saturation * FieldWidth;
        float markerY = (1f - value) * FieldHeight;
        context.FillEllipse(new Rect(markerX - 7f, markerY - 7f, 14f, 14f), 0xFF101215);
        context.FillEllipse(new Rect(markerX - 5.5f, markerY - 5.5f, 11f, 11f), 0xFFFFFFFF);
        context.FillEllipse(new Rect(markerX - 3.5f, markerY - 3.5f, 7f, 7f), Colour.FromHsv(hue, saturation, value));
    }

    private static void DrawHueStrip(CanvasContext context, float hue)
    {
        for (int x = 0; x < FieldWidth; x += 2)
        {
            context.FillRect(x, 0, 2f, StripHeight, Colour.FromHsv(x / (float)FieldWidth * 360f, 1f, 1f));
        }

        float markerX = Math.Clamp(hue / 360f, 0f, 1f) * FieldWidth;
        context.FillRect(markerX - 2f, 0f, 4f, StripHeight, 0xFF101215);
        context.FillRect(markerX - 1f, 1f, 2f, StripHeight - 2f, 0xFFFFFFFF);
    }

    private static void DrawAlphaStrip(CanvasContext context, float hue, float saturation, float value, float alpha)
    {
        const int cell = 8;
        for (int y = 0; y < StripHeight; y += cell)
        {
            for (int x = 0; x < FieldWidth; x += cell)
            {
                bool light = ((x / cell) + (y / cell)) % 2 == 0;
                context.FillRect(x, y, cell, cell, light ? 0xFFB9BFC8u : 0xFF7A828Du);
            }
        }

        for (int x = 0; x < FieldWidth; x += 2)
        {
            uint colour = Colour.FromHsv(hue, saturation, value, x / (float)FieldWidth);
            context.FillRect(x, 0, 2f, StripHeight, colour);
        }

        float markerX = Math.Clamp(alpha, 0f, 1f) * FieldWidth;
        context.FillRect(markerX - 2f, 0f, 4f, StripHeight, 0xFF101215);
        context.FillRect(markerX - 1f, 1f, 2f, StripHeight - 2f, 0xFFFFFFFF);
    }
}
