using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Second bar: document actions, snapping, alignment and tool options.</summary>
internal sealed class OptionsBar : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);

        return Div(new() { ClassName = "optionsbar" },
            Ui.OptionsGroup(
                Ui.IconButton(Icons.Undo, "Undo (Ctrl+Z)", session.Undo, session.CanUndo),
                Ui.IconButton(Icons.Redo, "Redo (Ctrl+Y)", session.Redo, session.CanRedo)),
            Ui.OptionsSeparator(),
            ZoomGroup(session),
            Ui.OptionsSeparator(),
            Ui.OptionsGroup(
                Ui.IconButton(Icons.Grid, "Show grid", session.ToggleGrid, active: session.ShowGrid),
                Ui.IconButton(Icons.Snap, "Snap to objects", session.ToggleObjectSnapping, active: session.SnapToObjects),
                Ui.IconButton(Icons.Ruler, "Show rulers", session.ToggleRulers, active: session.ShowRulers)),
            Ui.OptionsSeparator(),
            AlignGroup(session),
            Ui.OptionsSeparator(),
            Ui.OptionsGroup(
                Ui.IconButton(Icons.Group, "Group selection (Ctrl+G)", session.GroupSelection, session.Selection.Count > 0),
                Ui.IconButton(Icons.Ungroup, "Ungroup selection (Ctrl+Shift+G)", session.UngroupSelection, session.Selection.OfType<GroupObject>().Any(group => !group.IsLayer))),
            Ui.OptionsSeparator(),
            Ui.OptionsGroup(
                Ui.IconButton(Icons.Folder, "Import SVG (Ctrl+Shift+I)", session.OpenImportDialog),
                Ui.IconButton(Icons.Export, "Export SVG (Ctrl+Shift+E)", session.OpenExportDialog)),
            ContextOptions(session),
            Div(new() { ClassName = "options-spacer" }),
            SelectionReadout(session));
    }

    private static Node ZoomGroup(EditorSession session) =>
        Div(new() { ClassName = "options-zoom" },
            Ui.IconButton(Icons.Minus, "Zoom out (Ctrl+-)", session.ZoomOut),
            Button(new()
            {
                ClassName = "zoom-value",
                Title = "Actual size (Ctrl+0)",
                OnClick = session.ActualPixels,
            }, Text($"{session.Zoom * 100f:0.#}%")),
            Ui.IconButton(Icons.Plus, "Zoom in (Ctrl++)", session.ZoomIn));

    private static Node AlignGroup(EditorSession session)
    {
        bool many = session.Selection.Count > 0;
        return Ui.OptionsGroup(
            AlignButton("left", "Align left", () => session.Align(EditorSession.AlignMode.Left), many),
            AlignButton("center", "Align horizontal centres", () => session.Align(EditorSession.AlignMode.CenterHorizontal), many),
            AlignButton("right", "Align right", () => session.Align(EditorSession.AlignMode.Right), many),
            AlignButton("left", "Align top", () => session.Align(EditorSession.AlignMode.Top), many, vertical: true),
            AlignButton("center", "Align vertical centres", () => session.Align(EditorSession.AlignMode.CenterVertical), many, vertical: true),
            AlignButton("right", "Align bottom", () => session.Align(EditorSession.AlignMode.Bottom), many, vertical: true));
    }

    private static Node AlignButton(string align, string title, Action onClick, bool enabled, bool vertical = false) =>
        Button(new()
        {
            ClassName = "icon-btn",
            Title = title,
            Disabled = !enabled,
            OnClick = onClick,
        },
        Span(new()
        {
            ClassName = vertical ? $"align-glyph is-{align} is-vertical" : $"align-glyph is-{align}",
        },
        Span(new() { }),
        Span(new() { }),
        Span(new() { })));

    private static Node? ContextOptions(EditorSession session)
    {
        PathObject? shape = session.Selection.OfType<PathObject>().FirstOrDefault(path => path.Shape is not null);

        if (session.Tool == ToolKind.RoundedRectangle || shape?.Shape?.Kind == ShapeKind.RoundedRectangle)
        {
            float radius = shape?.Shape?.CornerRadius ?? session.DefaultCornerRadius;
            return F(
                Div(new() { ClassName = "options-sep" }),
                Ui.ContextField(session, "Radius", radius, text =>
                {
                    session.DefaultCornerRadius = MathF.Max(0f, Ui.Parse(text, 14f));
                    session.SetSelectionCornerRadius(session.DefaultCornerRadius);
                }));
        }

        if (session.Tool is ToolKind.Polygon or ToolKind.Star || shape?.Shape?.Kind is ShapeKind.Polygon or ShapeKind.Star)
        {
            ShapeKind kind = shape?.Shape?.Kind ?? (session.Tool == ToolKind.Star ? ShapeKind.Star : ShapeKind.Polygon);
            int points = shape?.Shape?.Points ?? session.DefaultPoints;
            if (kind == ShapeKind.Star)
            {
                float inner = shape?.Shape?.InnerRatio ?? session.DefaultInnerRatio;
                return F(
                    Div(new() { ClassName = "options-sep" }),
                    Ui.ContextField(session, "Points", points, text => session.SetSelectionPoints((int)Ui.Parse(text, points))),
                    Ui.ContextField(session, "Inner", MathF.Round(inner * 100f), text =>
                    {
                        session.DefaultInnerRatio = Math.Clamp(Ui.Parse(text, inner * 100f) / 100f, 0.05f, 0.95f);
                        session.SetSelectionInnerRatio(session.DefaultInnerRatio);
                    }));
            }

            return F(
                Div(new() { ClassName = "options-sep" }),
                Ui.ContextField(session, "Sides", points, text => session.SetSelectionPoints((int)Ui.Parse(text, points))));
        }

        return null;
    }

    private static Node SelectionReadout(EditorSession session)
    {
        Bounds bounds = session.SelectionBounds;
        string selection = session.Selection.Count switch
        {
            0 => "No selection",
            1 => session.Selection[0].DisplayName,
            _ => $"{session.Selection.Count} objects",
        };

        string detail = session.Selection.Count > 0
            ? $"{Ui.Format(bounds.Width)} × {Ui.Format(bounds.Height)}"
            : $"{Ui.Format(session.Document.Active.Width)} × {Ui.Format(session.Document.Active.Height)} px";

        return Div(new() { ClassName = "options-readout" },
            Strong(new() { }, Text(selection)),
            Span(new() { ClassName = "options-detail" }, Text(detail)));
    }
}
