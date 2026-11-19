using Donkeh.Hosting;
using Donkeh.UILab;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet;

/// <summary>
/// Application root. Owns the editor session and publishes it to the chrome,
/// the canvas and the inspector; the tick counter rebuilds the tree when the
/// model reports a structural change.
/// </summary>
[AppRoot(
    Width = 1360,
    Height = 840,
    Title = "VectorDotNet",
    ClearColor = 0xFF101114,
    MinWidth = 960,
    MinHeight = 620)]
public sealed class App : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        Ref<EditorSession> reference = UseRef<EditorSession>();
        EditorSession session = reference.Value ??= new EditorSession();
        session.RegisterFontFile ??= FontLibrary.Register;
        var (_, _, updateTick) = StateWithUpdate(0);
        session.Refresh = () => updateTick(value => value + 1);

        Node root = Div(new()
        {
            ClassName = session.ShowSidebar ? "app" : "app is-sidebar-hidden",
            OnClick = () =>
            {
                if (session.OpenMenu is not null)
                {
                    session.CloseMenu();
                }
            },
            OnKeyDown = key =>
            {
                if (session.ShowShortcuts)
                {
                    if (key == "Escape")
                    {
                        session.CloseShortcuts();
                    }

                    return;
                }

                if (session.ShowColourPicker)
                {
                    if (key == "Escape")
                    {
                        session.CancelColourPicker();
                    }

                    return;
                }

                if (session.ShowFontPicker)
                {
                    if (key == "Escape")
                    {
                        session.CloseFontPicker();
                    }

                    return;
                }

                if (session.ShowFileBrowser)
                {
                    if (key == "Escape")
                    {
                        session.CloseFileBrowser();
                    }
                    else if (key is "Return" or "Enter")
                    {
                        session.BrowserConfirm();
                    }

                    return;
                }

                session.HandleKey(key);
            },
        },
        C<MenuBar>(),
        C<OptionsBar>(),
        C<ToolRail>(),
        C<Viewport>(),
        C<SidePanel>(),
        C<StatusBar>(),
        Show(session.OpenMenu is not null, C<MenuOverlay>()),
        Show(session.ShowShortcuts, Div(new()
        {
            ClassName = "modal-layer",
            OnClick = session.CloseShortcuts,
        }, C<ShortcutsModal>())),
        Show(session.ShowColourPicker, Div(new()
        {
            ClassName = "modal-layer",
            OnClick = session.CancelColourPicker,
        }, C<ColourPicker>())),
        Show(session.ShowFileBrowser, Div(new()
        {
            ClassName = "modal-layer",
            OnClick = session.CloseFileBrowser,
        }, C<FileBrowser>())),
        Show(session.ShowFontPicker, Div(new()
        {
            ClassName = "modal-layer",
            OnClick = session.CloseFontPicker,
        }, C<FontPicker>())));

        return Provider(SessionContext.Value, session, root);
    }
}
