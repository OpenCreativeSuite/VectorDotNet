using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Artboard list with add, duplicate, delete and selection.</summary>
internal sealed class ArtboardsPanel : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        List<Node> rows = [];

        for (int i = 0; i < session.Document.Artboards.Count; i++)
        {
            int index = i;
            Artboard artboard = session.Document.Artboards[i];
            bool active = index == session.Document.ActiveArtboard;
            rows.Add(Div(new()
            {
                ClassName = active ? "artboard-row is-selected" : "artboard-row",
                OnClick = () => session.SelectArtboard(index),
            },
            Ui.Icon(Icons.RoundedRectangle, "artboard-icon"),
            Span(new() { ClassName = "artboard-name" }, Text(artboard.Name)),
            Span(new() { ClassName = "artboard-size" }, Text($"{Ui.Format(artboard.Width)} × {Ui.Format(artboard.Height)}")),
            Ui.MiniButton(Icons.Duplicate, "Duplicate artboard", () => session.DuplicateArtboard(index)),
            Ui.MiniButton(Icons.Trash, "Delete artboard", () => session.DeleteArtboard(index), enabled: session.Document.Artboards.Count > 1)));
        }

        return C<Panel, PanelProps>(new PanelProps
        {
            Title = "Artboards",
            Badge = $"{session.Document.Artboards.Count}",
            StartOpen = false,
            Children =
            [
                Div(new() { ClassName = "panel-actions" },
                    Ui.MiniButton(Icons.Plus, "New artboard", session.AddArtboard)),
                Div(new() { ClassName = "artboard-list" }, [.. rows]),
            ],
        });
    }
}
