using Donkeh.UILab;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Props for a collapsible inspector section.</summary>
internal sealed class PanelProps : PropsWithChildren
{
    public string Title { get; init; } = "";

    public string Badge { get; init; } = "";

    public bool StartOpen { get; init; } = true;
}

/// <summary>A collapsible inspector section with a caret header.</summary>
internal sealed class Panel : Component<PanelProps>
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var (open, setOpen) = State(Props.StartOpen);

        return Div(new() { ClassName = "panel" },
            Div(new()
            {
                ClassName = "panel-head",
                OnClick = () => setOpen(!open),
            },
            Span(new() { ClassName = "panel-caret" }, Ui.Icon(open ? Icons.ChevronDown : Icons.ChevronRight)),
            Span(new() { ClassName = "panel-title" }, Text(Props.Title)),
            Show(Props.Badge.Length > 0, Span(new() { ClassName = "panel-badge" }, Text(Props.Badge)))),
            Show(open, Div(new() { ClassName = "panel-body" }, F(Props.Children ?? []))));
    }
}
