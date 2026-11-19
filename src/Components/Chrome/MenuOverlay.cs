using Donkeh.UILab;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Components;

/// <summary>
/// The open drop-down, rendered as a top-level overlay rather than a child of
/// the menu bar. The menu bar is only 30px tall, and a dropdown clipped to that
/// box could be painted but never receive pointer input; owning its own layer
/// keeps it above the canvas and above every panel.
/// </summary>
internal sealed class MenuOverlay : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        EditorSession session = UseContext(SessionContext.Value);
        MenuDefinition? menu = MenuBar.Find(session.OpenMenu);
        if (menu is null)
        {
            return F();
        }

        return Div(new()
        {
            ClassName = "menu-float",
            Attributes = new()
            {
                ["style"] = $"left:{Ui.Number(session.MenuX)}px;top:{Ui.Number(session.MenuY)}px;",
            },
        },
        MenuBar.Rows(session, menu));
    }
}
