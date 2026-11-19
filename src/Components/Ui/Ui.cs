using System.Globalization;
using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Markup helpers shared by the chrome, the canvas and the inspector.</summary>
internal static class Ui
{
    /// <summary>Formats a length without trailing noise.</summary>
    public static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Formats a whole number readout.</summary>
    public static string Format(float value) => MathF.Round(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a percentage.</summary>
    public static string Percent(float value) => (value * 100f).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Parses a float, falling back to a default.</summary>
    public static float Parse(string? text, float fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        string clean = text.Trim().TrimEnd('%');
        return float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;
    }

    /// <summary>Builds canvas width/height attributes.</summary>
    public static Dictionary<string, string> Size(int width, int height) => new()
    {
        ["width"] = width.ToString(CultureInfo.InvariantCulture),
        ["height"] = height.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Formats a packed colour as a CSS hex string.</summary>
    public static string Hex(uint color) => Colour.ToHex(color);

    /// <summary>Focus handler that suppresses global editing shortcuts.</summary>
    public static Action Focus(EditorSession session) => () => session.IsTyping = true;

    /// <summary>Blur handler that restores global editing shortcuts.</summary>
    public static Action Blur(EditorSession session) => () => session.IsTyping = false;

    /// <summary>A Font Awesome glyph.</summary>
    public static Node Icon(string name, string extra = "", bool regular = false) =>
        I(new() { ClassName = $"fa {(regular ? "fa-regular" : "fa-solid")} {name} {extra}".TrimEnd() });

    /// <summary>A square icon button.</summary>
    public static Node IconButton(
        string icon,
        string title,
        Action onClick,
        bool enabled = true,
        bool active = false,
        string className = "icon-btn")
    {
        List<string> classes = [className];
        if (active)
        {
            classes.Add("is-on");
        }

        return Button(new()
        {
            ClassName = string.Join(' ', classes),
            Title = title,
            Disabled = !enabled,
            OnClick = onClick,
        },
        Icon(icon));
    }

    /// <summary>A labelled grouped control inside the options bar.</summary>
    public static Node OptionsGroup(params Node[] children) =>
        Div(new() { ClassName = "options-group" }, children);

    /// <summary>A vertical divider in the options bar.</summary>
    public static Node OptionsSeparator() => Div(new() { ClassName = "options-sep" });

    /// <summary>A small numeric context field for the options bar.</summary>
    public static Node ContextField(EditorSession session, string label, float value, Action<string> commit) =>
        Div(new() { ClassName = "options-context" },
            Span(new() { ClassName = "options-context-label" }, Text(label)),
            Input(new()
            {
                ClassName = "options-context-input",
                Type = "text",
                Value = Number(value),
                OnChange = commit,
                OnFocus = Focus(session),
                OnBlur = Blur(session),
            }));

    /// <summary>A numeric field with a one-character key.</summary>
    public static Node NumField(EditorSession session, string key, float value, Action<string> commit) =>
        Div(new() { ClassName = "field-row" },
            Span(new() { ClassName = "field-key" }, Text(key)),
            Input(new()
            {
                ClassName = "num-field",
                Type = "text",
                Value = Number(MathF.Round(value * 100f) / 100f),
                OnChange = commit,
                OnFocus = Focus(session),
                OnBlur = Blur(session),
            }));

    /// <summary>A labelled slider.</summary>
    public static Node Slider(
        EditorSession session,
        string label,
        float value,
        float min,
        float max,
        string suffix,
        Action<string> onInput,
        Action<string>? onChange = null)
    {
        string display = suffix == "%" ? Percent(value) + "%" : Number(value) + suffix;
        return Div(new() { ClassName = "slider-row" },
            Div(new() { ClassName = "slider-head" },
                Span(new() { ClassName = "slider-label" }, Text(label)),
                Span(new() { ClassName = "slider-value" }, Text(display))),
            Input(new()
            {
                ClassName = "slider-input",
                Type = "range",
                Value = Number(value),
                Attributes = new()
                {
                    ["min"] = Number(min),
                    ["max"] = Number(max),
                    ["step"] = Number((max - min) / 100f),
                },
                OnInput = onInput,
                OnChange = onChange ?? onInput,
                OnFocus = Focus(session),
                OnBlur = Blur(session),
            }));
    }

    /// <summary>A colour chip painted from a packed colour.</summary>
    public static Node Swatch(uint color, string title, Action onClick, string className = "swatch") =>
        Div(new()
        {
            ClassName = className,
            Title = title,
            OnClick = onClick,
            Attributes = new() { ["style"] = $"background-color:{Hex(color)}" },
        });

    /// <summary>A face swatch with optional "none" styling.</summary>
    public static Node FaceSwatch(uint color, bool none, string title, Action onClick) =>
        Div(new()
        {
            ClassName = none ? "face-swatch is-none" : "face-swatch",
            Title = title,
            OnClick = onClick,
            Attributes = new()
            {
                ["style"] = none ? "background-color:#191d22" : $"background-color:{Hex(color)}",
            },
        });

    /// <summary>The small square that previews an object in the layers list.</summary>
    public static Node LayerThumb(uint fill, bool isText = false, bool isGroup = false) =>
        Div(new()
        {
            ClassName = "layer-thumb",
            Attributes = new()
            {
                ["style"] = isGroup
                    ? "background-color:#262b32"
                    : $"background-color:{Hex(fill)}",
            },
        });

    /// <summary>A tiny action button.</summary>
    public static Node MiniButton(string icon, string title, Action onClick, bool active = false, bool enabled = true) =>
        Button(new()
        {
            ClassName = active ? "mini-btn is-on" : "mini-btn",
            Title = title,
            Disabled = !enabled,
            OnClick = onClick,
        },
        Icon(icon));
}
