using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>Layer tree with visibility, locking and selection.</summary>
internal sealed class LayersPanel : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);

        List<Node> rows = [];
        foreach (LayerObject layer in session.Document.Layers.Reverse())
        {
            AddRows(session, layer, 0, rows);
        }

        return C<Panel, PanelProps>(new PanelProps
        {
            Title = "Layers",
            Badge = $"{session.Document.Layers.Count()}",
            Children =
            [
                Div(new() { ClassName = "panel-actions" },
                    Ui.MiniButton(Icons.Plus, "New layer", session.AddLayer),
                    Ui.MiniButton(Icons.Trash, "Delete active layer", session.DeleteActiveLayer, enabled: session.Document.Layers.Count() > 1),
                    Ui.MiniButton(Icons.Group, "Group selection (Ctrl+G)", session.GroupSelection, enabled: session.Selection.Count > 0),
                    Ui.MiniButton(Icons.Ungroup, "Ungroup selection (Ctrl+Shift+G)", session.UngroupSelection, enabled: session.Selection.OfType<GroupObject>().Any(group => !group.IsLayer))),
                Show(rows.Count == 0, Div(new() { ClassName = "layer-empty" }, Text("No layers yet."))),
                Show(rows.Count > 0, Div(new() { ClassName = "layer-list" }, [.. rows])),
            ],
        });
    }

    private static void AddRows(EditorSession session, SceneObject item, int level, List<Node> rows)
    {
        rows.Add(Row(session, item, level));

        if (item is not GroupObject group || !group.Expanded || group.Children.Count == 0)
        {
            return;
        }

        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            AddRows(session, group.Children[i], level + 1, rows);
        }
    }

    private static Node Row(EditorSession session, SceneObject item, int level)
    {
        bool isGroup = item is GroupObject;
        bool isLayer = item is LayerObject;
        bool selected = session.Selection.Contains(item);
        bool activeLayer = item is LayerObject layer && session.Document.ActiveLayer == layer;

        List<string> classes = ["layer-row"];
        if (isLayer)
        {
            classes.Add("is-layer");
        }

        if (selected || activeLayer)
        {
            classes.Add("is-selected");
        }

        string indent = $"padding-left:{4 + (level * 12)}px;";

        List<Node> content = [];

        if (isGroup)
        {
            GroupObject group = (GroupObject)item;
            content.Add(Button(new()
            {
                ClassName = "layer-caret",
                Title = group.Expanded ? "Collapse" : "Expand",
                OnClick = () =>
                {
                    group.Expanded = !group.Expanded;
                    session.Notify();
                },
            }, Ui.Icon(group.Expanded ? Icons.ChevronDown : Icons.ChevronRight)));
        }
        else
        {
            content.Add(Span(new() { ClassName = "layer-caret" }));
        }

        content.Add(Button(new()
        {
            ClassName = item.Visible ? "layer-toggle" : "layer-toggle is-off",
            Title = item.Visible ? "Hide" : "Show",
            OnClick = () => session.ToggleVisibility(item),
        }, Ui.Icon(item.Visible ? Icons.Eye : Icons.EyeOff)));

        content.Add(Button(new()
        {
            ClassName = item.Locked ? "layer-toggle is-off" : "layer-toggle",
            Title = item.Locked ? "Unlock" : "Lock",
            OnClick = () => session.ToggleLock(item),
        }, Ui.Icon(item.Locked ? Icons.Lock : Icons.Unlock)));

        content.Add(Ui.LayerThumb(ThumbColour(item), item is TextObject, isGroup));
        content.Add(Span(new() { ClassName = "layer-name" }, Text(item.DisplayName)));

        if (isGroup)
        {
            content.Add(Span(new() { ClassName = "layer-count" }, Text(((GroupObject)item).Children.Count.ToString())));
        }

        return Div(new()
        {
            ClassName = string.Join(' ', classes),
            Title = item.DisplayName,
            Attributes = new() { ["style"] = indent },
            OnClick = () =>
            {
                if (item is LayerObject target)
                {
                    session.SetActiveLayer(target);
                }
                else
                {
                    session.Select(item);
                }
            },
        }, content.ToArray());
    }

    private static uint ThumbColour(SceneObject item) => item switch
    {
        PathObject { Fill: { } fill } => Colour.Opaque(fill.Color),
        PathObject { Stroke: { } stroke } => Colour.Opaque(stroke.Paint.Color),
        TextObject { Fill: { } fill } => Colour.Opaque(fill.Color),
        TextObject => 0xFF9AA3AF,
        _ => 0xFF39404A,
    };
}
