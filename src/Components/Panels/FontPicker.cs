using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>
/// Searchable font family picker. Rows show the family name and a live preview
/// set in that face, following the same flow as the PixelDotNet font popup.
/// </summary>
internal sealed class FontPicker : Component
{
    private const int VisibleLimit = 160;

    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        IReadOnlyList<string> matches = session.FilteredFontFamilies;
        string current = session.CurrentFontFamily;
        List<Node> rows = [];
        int visible = Math.Min(matches.Count, VisibleLimit);

        for (int i = 0; i < visible; i++)
        {
            rows.Add(Row(session, matches[i], current));
        }

        return Div(new() { ClassName = "modal font-picker-modal", OnClick = () => { } },
            Div(new() { ClassName = "modal-head" },
                Span(new() { ClassName = "modal-title" }, Text("Fonts")),
                Ui.IconButton(Icons.Close, "Close", session.CloseFontPicker)),
            Div(new() { ClassName = "font-search-row" },
                Ui.Icon(Icons.Zoom, "font-search-icon"),
                Input(new()
                {
                    ClassName = "font-search",
                    Type = "text",
                    Value = session.FontSearch,
                    Placeholder = "Search fonts…",
                    OnInput = session.SetFontSearch,
                    OnChange = session.SetFontSearch,
                    OnFocus = Ui.Focus(session),
                    OnBlur = Ui.Blur(session),
                }),
                Span(new()
                {
                    ClassName = "font-count",
                }, Text(CountLabel(matches.Count)))),
            Show(matches.Count > 0,
                Div(new() { ClassName = "font-list" }, rows.ToArray())),
            Show(matches.Count == 0,
                Div(new() { ClassName = "font-empty" }, Text("No fonts match that search."))),
            Div(new() { ClassName = "font-footer" },
                Button(new()
                {
                    ClassName = "btn",
                    Title = "Load a TTF, OTF, WOFF or WOFF2 font file",
                    OnClick = session.LoadFontFromPicker,
                }, Text("Load font file…")),
                Span(new() { ClassName = "panel-hint" }, Text("Type to search, click a family to apply"))));
    }

    private static Node Row(EditorSession session, string family, string current)
    {
        bool selected = string.Equals(family, current, StringComparison.OrdinalIgnoreCase);
        string safe = family.Replace("\"", "\\\"");
        return Div(new()
        {
            ClassName = selected ? "font-row is-selected" : "font-row",
            Title = family,
            OnClick = () =>
            {
                FontLibrary.EnsureFamily(family);
                session.ApplyFontFamily(family);
            },
        },
        Span(new() { ClassName = "font-row-name" }, Text(family)),
        Span(new()
        {
            ClassName = "font-row-preview",
            Attributes = new() { ["style"] = $"font-family:\"{safe}\";" },
        }, Text("Aa The quick brown fox")));
    }

    private static string CountLabel(int count) => count switch
    {
        0 => "no matches",
        1 => "1 font",
        _ => count + " fonts",
    };
}
