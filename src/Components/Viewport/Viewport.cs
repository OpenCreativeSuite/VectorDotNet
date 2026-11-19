using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>The workspace column: document tabs above the canvas.</summary>
internal sealed class Viewport : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        IReadOnlyList<Document> documents = session.OpenDocuments;
        List<Node> tabs = new(documents.Count);

        for (int i = 0; i < documents.Count; i++)
        {
            int index = i;
            Document document = documents[i];
            bool active = index == session.ActiveDocumentIndex;
            tabs.Add(Div(new()
            {
                ClassName = active ? "doc-tab is-active" : "doc-tab",
                Title = document.Name,
                OnClick = () => session.SwitchDocument(index),
            },
            Ui.Icon(Icons.Document, "doc-tab-icon"),
            Span(new() { ClassName = "doc-tab-name" }, Text(document.Name)),
            Button(new()
            {
                ClassName = "doc-tab-close",
                Title = "Close document",
                OnClick = () => session.CloseDocument(index),
            }, Ui.Icon(Icons.Close))));
        }

        tabs.Add(Button(new()
        {
            ClassName = "doc-tab-add",
            Title = "New document tab (Ctrl+N)",
            OnClick = () => session.NewDocument(1200f, 800f),
        }, Ui.Icon(Icons.Plus)));

        return Div(new() { ClassName = "stage" },
            Div(new() { ClassName = "doc-tabs" }, tabs.ToArray()),
            Div(new() { ClassName = "viewport" },
                C<CanvasSurface>()));
    }
}
