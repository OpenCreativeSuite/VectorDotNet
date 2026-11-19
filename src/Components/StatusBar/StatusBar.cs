using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Bottom bar: tool state, cursor readout, selection and zoom.</summary>
internal sealed class StatusBar : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        Bounds bounds = session.SelectionBounds;
        int objects = session.Document.Root.SelfAndDescendants().OfType<PathObject>().Count()
            + session.Document.Root.SelfAndDescendants().OfType<TextObject>().Count();

        string position = session.CursorOnCanvas
            ? $"{session.CursorDocument.X:0}, {session.CursorDocument.Y:0}"
            : "—";

        string selection = session.Selection.Count switch
        {
            0 => "No selection",
            1 => $"{session.Selection[0].DisplayName}  {Ui.Format(bounds.Width)} × {Ui.Format(bounds.Height)}",
            _ => $"{session.Selection.Count} objects selected",
        };

        return Div(new() { ClassName = "statusbar" },
            Span(new() { ClassName = "status-item status-tool" }, Text(session.ToolName)),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-item" }, Text(selection)),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-item is-muted" }, Text(position)),
            Div(new() { ClassName = "status-spacer" }),
            Span(new() { ClassName = "status-item is-muted" }, Text($"{objects} objects")),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-item is-muted" }, Text($"{session.Document.Active.Name}")),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-message" }, Text(session.Status)));
    }
}
