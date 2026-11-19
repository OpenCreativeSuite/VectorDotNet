using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>One row in a drop-down menu.</summary>
internal sealed record MenuAction(
    string Label,
    string Shortcut,
    Action<EditorSession> Run,
    Func<EditorSession, bool>? Enabled = null,
    Func<EditorSession, bool>? Checked = null,
    bool StartsGroup = false);

/// <summary>A titled drop-down.</summary>
internal sealed record MenuDefinition(string Title, MenuAction[] Items);

/// <summary>Top bar with the application menus and the document readout.</summary>
internal sealed class MenuBar : Component
{
    internal static readonly MenuDefinition[] Menus = BuildMenus();

    /// <summary>Finds a menu definition by title.</summary>
    internal static MenuDefinition? Find(string? title)
    {
        if (title is null)
        {
            return null;
        }

        foreach (MenuDefinition menu in Menus)
        {
            if (menu.Title == title)
            {
                return menu;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);

        return Div(new() { ClassName = "menubar" },
            Div(new() { ClassName = "brand" },
                Div(new() { ClassName = "brand-mark" },
                    Span(new() { ClassName = "brand-dot" })),
                Span(new() { ClassName = "brand-name" },
                    Strong(new() { }, Text("Vector")), Text("DotNet"))),
            Each(Menus, menu => (Node)Menu(session, menu)),
            Div(new() { ClassName = "menu-spacer" }),
            Div(new() { ClassName = "menu-document" },
                Ui.Icon(Icons.Document),
                Span(new() { ClassName = "menu-doc-name" },
                    Text(session.Document.Name),
                    Text("  ·  "),
                    Strong(new() { }, Text($"{Ui.Format(session.Document.Active.Width)} × {Ui.Format(session.Document.Active.Height)} px")))));
    }

    private static Node Menu(EditorSession session, MenuDefinition definition)
    {
        bool open = session.OpenMenu == definition.Title;
        Ref<ElementNode> reference = new();
        ElementNode title = (ElementNode)Button(new()
        {
            ClassName = open ? "menu-title is-open" : "menu-title",
            OnClick = () =>
            {
                ElementNode? node = reference.Value;
                session.ToggleMenuAt(
                    definition.Title,
                    node?.PositionX ?? 0f,
                    (node?.PositionY ?? 0f) + (node?.Height ?? 30f) - 2f);
            },
        }, Text(definition.Title));
        title.BindRef(reference);

        // The simple OnMouseOver property is not consistently dispatched by
        // every host, so register the event directly on the element. Sweeping
        // the bar then swaps the open drop-down instead of closing it.
        title.On("mouseover", (Action)(() =>
        {
            ElementNode? node = reference.Value;
            session.HoverMenuAt(
                definition.Title,
                node?.PositionX ?? 0f,
                (node?.PositionY ?? 0f) + (node?.Height ?? 30f) - 2f);
        }));

        return Div(new() { ClassName = "menu" }, title);
    }

    internal static Node[] Rows(EditorSession session, MenuDefinition definition)
    {
        List<Node> rows = new(definition.Items.Length * 2);
        foreach (MenuAction item in definition.Items)
        {
            if (item.StartsGroup && rows.Count > 0)
            {
                rows.Add(Div(new() { ClassName = "menu-sep" }));
            }

            bool enabled = item.Enabled is null || item.Enabled(session);
            bool on = item.Checked is not null && item.Checked(session);
            rows.Add(Button(new()
            {
                ClassName = "menu-item",
                Disabled = !enabled,
                OnClick = () =>
                {
                    session.CloseMenu();
                    item.Run(session);
                },
            },
            Span(new() { ClassName = "menu-item-check" }, Show(on, Ui.Icon(Icons.Check))),
            Span(new() { ClassName = "menu-item-label" }, Text(item.Label)),
            Span(new() { ClassName = "menu-item-key" }, Text(item.Shortcut))));
        }

        return rows.ToArray();
    }

    private static MenuDefinition[] BuildMenus() =>
    [
        new("File",
        [
            new("New Document", "Ctrl+N", session => session.NewDocument(1200f, 800f)),
            new("New Square Document", "", session => session.NewDocument(1080f, 1080f)),
            new("New A4 Document", "", session => session.NewDocument(1240f, 1754f)),
            new("Open Sample", "", session => session.LoadSample(), StartsGroup: true),
            new("Import SVG…", "Ctrl+Shift+I", session => session.OpenImportDialog()),
            new("Export SVG…", "Ctrl+Shift+E", session => session.OpenExportDialog(), StartsGroup: true),
            new("Load Font…", "", session => session.OpenFontBrowser(), StartsGroup: true),
        ]),
        new("Edit",
        [
            new("Undo", "Ctrl+Z", session => session.Undo(), session => session.CanUndo),
            new("Redo", "Ctrl+Y", session => session.Redo(), session => session.CanRedo),
            new("Cut", "Ctrl+X", session => session.Cut(), session => session.Selection.Count > 0, StartsGroup: true),
            new("Copy", "Ctrl+C", session => session.Copy(), session => session.Selection.Count > 0),
            new("Paste", "Ctrl+V", session => session.Paste()),
            new("Duplicate", "Ctrl+D", session => session.DuplicateSelection(), session => session.Selection.Count > 0),
            new("Delete", "Del", session => session.DeleteSelection(), session => session.Selection.Count > 0),
            new("Select All", "Ctrl+A", session => session.SelectAll(), StartsGroup: true),
            new("Deselect", "Esc", session => session.DeselectAll(), session => session.Selection.Count > 0),
        ]),
        new("Object",
        [
            new("Group", "Ctrl+G", session => session.GroupSelection(), session => session.Selection.Count > 0),
            new("Ungroup", "Ctrl+Shift+G", session => session.UngroupSelection(), session => session.Selection.OfType<GroupObject>().Any(group => !group.IsLayer)),
            new("Bring to Front", "]", session => session.Arrange(2), session => session.Selection.Count > 0, StartsGroup: true),
            new("Bring Forward", "", session => session.Arrange(1), session => session.Selection.Count > 0),
            new("Send Backward", "", session => session.Arrange(-1), session => session.Selection.Count > 0),
            new("Send to Back", "[", session => session.Arrange(-2), session => session.Selection.Count > 0),
            new("Align Left", "", session => session.Align(EditorSession.AlignMode.Left), session => session.Selection.Count > 0, StartsGroup: true),
            new("Align Centre", "", session => session.Align(EditorSession.AlignMode.CenterHorizontal), session => session.Selection.Count > 0),
            new("Align Right", "", session => session.Align(EditorSession.AlignMode.Right), session => session.Selection.Count > 0),
            new("Align Top", "", session => session.Align(EditorSession.AlignMode.Top), session => session.Selection.Count > 0),
            new("Align Middle", "", session => session.Align(EditorSession.AlignMode.CenterVertical), session => session.Selection.Count > 0),
            new("Align Bottom", "", session => session.Align(EditorSession.AlignMode.Bottom), session => session.Selection.Count > 0),
            new("Distribute Horizontally", "", session => session.Align(EditorSession.AlignMode.DistributeHorizontal), session => session.Selection.Count > 2, StartsGroup: true),
            new("Distribute Vertically", "", session => session.Align(EditorSession.AlignMode.DistributeVertical), session => session.Selection.Count > 2),
        ]),
        new("Path",
        [
            new("Convert Shape to Path", "", session => session.ConvertShapesToPaths(), session => session.Selection.OfType<PathObject>().Any(path => path.IsShape)),
            new("Reverse Direction", "", session => session.ReverseSelectedPaths(), session => session.Selection.OfType<PathObject>().Any()),
            new("Simplify", "", session => session.SimplifySelectedPaths(), session => session.Selection.OfType<PathObject>().Any(), StartsGroup: true),
        ]),
        new("Text",
        [
            new("Bold", "", session => session.ToggleTextBold(), session => session.Selection.OfType<TextObject>().Any()),
            new("Increase Size", "", session => session.NudgeTextSize(4f), session => session.Selection.OfType<TextObject>().Any(), StartsGroup: true),
            new("Decrease Size", "", session => session.NudgeTextSize(-4f), session => session.Selection.OfType<TextObject>().Any()),
        ]),
        new("Layer",
        [
            new("New Layer", "", session => session.AddLayer()),
            new("Delete Layer", "", session => session.DeleteActiveLayer(), session => session.Document.Layers.Count() > 1),
        ]),
        new("View",
        [
            new("Zoom In", "Ctrl++", session => session.ZoomIn()),
            new("Zoom Out", "Ctrl+-", session => session.ZoomOut()),
            new("Actual Size", "Ctrl+0", session => session.ActualPixels()),
            new("Fit Artboard", "", session => session.FitArtboard()),
            new("Zoom to Selection", "", session => session.ZoomToSelection(), session => session.Selection.Count > 0),
            new("Show Grid", "", session => session.ToggleGrid(), StartsGroup: true, Checked: session => session.ShowGrid),
            new("Show Rulers", "", session => session.ToggleRulers(), Checked: session => session.ShowRulers),
            new("Show Guides", "", session => session.ToggleGuides(), Checked: session => session.ShowGuides),
            new("Snap to Grid", "", session => session.ToggleGridSnapping(), Checked: session => session.SnapToGrid),
            new("Snap to Objects", "", session => session.ToggleObjectSnapping(), Checked: session => session.SnapToObjects),
            new("Smart Guides", "", session => session.ToggleSmartGuides(), Checked: session => session.SmartGuides),
            new("Clear Guides", "", session => session.ClearGuides(), session => session.Document.Guides.Count > 0, StartsGroup: true),
        ]),
        new("Window",
        [
            new("Inspector", "", session => session.ToggleSidebar(), Checked: session => session.ShowSidebar),
            new("Keyboard Shortcuts", "", session => session.OpenShortcuts(), StartsGroup: true),
        ]),
        new("Help",
        [
            new("Keyboard Shortcuts", "", session => session.OpenShortcuts()),
            new("Open Sample", "", session => session.LoadSample()),
        ]),
    ];
}
