using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Keyboard shortcut reference sheet.</summary>
internal sealed class ShortcutsModal : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);

        return Div(new() { ClassName = "modal", OnClick = () => { } },
            Div(new() { ClassName = "modal-head" },
                Span(new() { ClassName = "modal-title" }, Text("Keyboard Shortcuts")),
                Ui.IconButton(Icons.Close, "Close", session.CloseShortcuts)),
            Div(new() { ClassName = "modal-body" },
                Div(new() { ClassName = "modal-cols" },
                    Group("Tools",
                        ("Selection", "V"),
                        ("Direct Selection", "A"),
                        ("Pen", "P"),
                        ("Pencil", "N"),
                        ("Rectangle", "M"),
                        ("Rounded Rectangle", "R"),
                        ("Ellipse", "E"),
                        ("Polygon", "G"),
                        ("Star", "S"),
                        ("Line", "L"),
                        ("Text", "T"),
                        ("Lasso", "Q"),
                        ("Zoom", "Z"),
                        ("Hand", "H")),
                    Group("Edit",
                        ("Undo", "Ctrl+Z"),
                        ("Redo", "Ctrl+Y"),
                        ("Cut", "Ctrl+X"),
                        ("Copy", "Ctrl+C"),
                        ("Paste", "Ctrl+V"),
                        ("Duplicate", "Ctrl+D"),
                        ("Group", "Ctrl+G"),
                        ("Ungroup", "Ctrl+Shift+G"),
                        ("Select All", "Ctrl+A"),
                        ("Deselect", "Esc"),
                        ("Delete", "Del"),
                        ("Nudge", "Arrows"),
                        ("Nudge ×10", "Shift+Arrows"),
                        ("Zoom In / Out", "Ctrl +/−"),
                        ("Actual Size", "Ctrl+0")))));
    }

    private static Node Group(string title, params (string Label, string Key)[] rows)
    {
        List<Node> list = [Div(new() { ClassName = "modal-group-title" }, Text(title))];
        foreach ((string label, string key) in rows)
        {
            list.Add(Div(new() { ClassName = "shortcut-row" },
                Span(new() { }, Text(label)),
                H("kbd", new() { }, Text(key))));
        }

        return Div(new() { }, list.ToArray());
    }
}
