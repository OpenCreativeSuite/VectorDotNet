using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Right column: artboards, transform, appearance, colour and layers.</summary>
internal sealed class SidePanel : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        string subject = session.Selection.Count switch
        {
            0 => session.ActiveLayer.DisplayName,
            1 => session.Selection[0].DisplayName,
            _ => $"{session.Selection.Count} objects",
        };

        return Div(new() { ClassName = "side" },
            Div(new() { ClassName = "side-head" },
                Span(new() { ClassName = "side-head-title" }, Text("Inspector")),
                Span(new() { ClassName = "side-head-name" }, Text(subject))),
            C<PropertiesPanel>(),
            C<LayersPanel>(),
            C<ColourPanel>(),
            C<ArtboardsPanel>());
    }
}
