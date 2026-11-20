using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab._;

namespace VectorDotNet.Tests;

public sealed class AppShellTests
{
    [Fact]
    public void ApplicationRootRendersTheWholeShell()
    {
        var page = ComponentTest.Render(C<App>());

        Assert.NotNull(page.TryFind(".app"));
        Assert.NotNull(page.TryFind(".menubar"));
        Assert.NotNull(page.TryFind(".optionsbar"));
        Assert.NotNull(page.TryFind(".rail"));
        Assert.NotNull(page.TryFind(".stage"));
        Assert.NotNull(page.TryFind(".side"));
        Assert.NotNull(page.TryFind(".statusbar"));
        Assert.Single(page.FindAll(".doc-tab"));
        Assert.NotNull(page.TryFind(".surface"));
        Assert.Null(page.TryFind(".modal"));
        Assert.Null(page.TryFind(".menu-float"));
    }

    [Fact]
    public void ToolRailAndKeyboardShortcutsUpdateTheStatusBar()
    {
        var page = ComponentTest.Render(C<App>());

        page.Click("[title='Rectangle (M)']");
        Assert.Equal("Rectangle", page.Find(".status-tool").Text);

        page.KeyDown(".app", "E");
        Assert.Equal("Ellipse", page.Find(".status-tool").Text);

        page.KeyDown(".app", "P");
        Assert.Equal("Pen", page.Find(".status-tool").Text);

        page.KeyDown(".app", "V");
        Assert.Equal("Selection", page.Find(".status-tool").Text);
    }

    [Fact]
    public void FileMenuLoadsTheSample()
    {
        var page = ComponentTest.Render(C<App>());

        page.FindAll(".menu-title").First(title => title.Text == "File").Click();
        Assert.NotNull(page.TryFind(".menu-float"));

        page.FindByText("Open Sample").Click();

        Assert.Equal(2, page.FindAll(".doc-tab").Count);
        Assert.Contains("Sample Poster", page.FindAll(".doc-tab-name").Select(name => name.Text));
    }

    [Fact]
    public void WindowMenuTogglesTheInspector()
    {
        var page = ComponentTest.Render(C<App>());
        Assert.False(page.Find(".app").HasClass("is-sidebar-hidden"));

        page.FindAll(".menu-title").First(title => title.Text == "Window").Click();
        page.FindAll(".menu-item")
            .First(row => row.Find(".menu-item-label").Text == "Inspector")
            .Click();

        Assert.True(page.Find(".app").HasClass("is-sidebar-hidden"));
    }

    [Fact]
    public void MenuClosesWhenTheRootIsClickedOrEscapeIsPressed()
    {
        var page = ComponentTest.Render(C<App>());

        page.FindAll(".menu-title").First(title => title.Text == "Edit").Click();
        Assert.NotNull(page.TryFind(".menu-float"));
        page.Click(".app");
        Assert.Null(page.TryFind(".menu-float"));

        page.FindAll(".menu-title").First(title => title.Text == "Edit").Click();
        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".menu-float"));
    }

    [Fact]
    public void ShortcutSheetOpensFromTheMenuAndEscapeClosesIt()
    {
        var page = ComponentTest.Render(C<App>());

        page.FindAll(".menu-title").First(title => title.Text == "Help").Click();
        page.FindAll(".menu-item")
            .First(row => row.Find(".menu-item-label").Text == "Keyboard Shortcuts")
            .Click();

        Assert.NotNull(page.TryFind(".modal"));
        Assert.Equal("Keyboard Shortcuts", page.Find(".modal-title").Text);

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".modal"));
    }

    [Fact]
    public void ImportBrowserOpensFromTheShortcut()
    {
        var page = ComponentTest.Render(C<App>());

        page.KeyDown(".app", "Ctrl+Shift+I");
        Assert.NotNull(page.TryFind(".file-browser"));
        Assert.Equal("Import SVG", page.Find(".modal-title").Text);

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".file-browser"));
    }

    [Fact]
    public void FillSwatchOpensTheColourPicker()
    {
        var page = ComponentTest.Render(C<App>());

        page.Click("[title='Fill colour picker']");
        Assert.NotNull(page.TryFind(".colour-picker-modal"));
        Assert.Equal("Fill Colour", page.Find(".modal-title").Text);

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".colour-picker-modal"));
    }

    [Fact]
    public void PlacingTextExposesFontAndTextControls()
    {
        var page = ComponentTest.Render(C<App>());
        page.KeyDown(".app", "T");
        Assert.Equal("Text", page.Find(".status-tool").Text);

        PointerDown(page, ".surface");

        Assert.NotNull(page.TryFind(".text-field-wide"));
        page.Find(".font-choice").Click();
        Assert.NotNull(page.TryFind(".font-picker-modal"));

        page.KeyDown(".app", "Escape");
        Assert.Null(page.TryFind(".font-picker-modal"));
    }

    [Fact]
    public void UndoAndRedoButtonsBecomeAvailableAfterAChange()
    {
        var page = ComponentTest.Render(C<App>());
        page.KeyDown(".app", "M");

        TestElement canvas = page.Find(".surface");
        Rect box = canvas.Bounds;
        page.Dispatch(canvas, new PointerDownEvent(box.X + 200, box.Y + 200), bubble: false);
        page.Dispatch(canvas, new PointerMoveEvent(box.X + 400, box.Y + 300), bubble: false);
        page.Dispatch(canvas, new PointerUpEvent(box.X + 400, box.Y + 300), bubble: false);

        Assert.True(page.FindAll(".layer-row").Count >= 2);
        page.KeyDown(".app", "Ctrl+Z");
        Assert.Single(page.FindAll(".layer-row"));
        page.KeyDown(".app", "Ctrl+Y");
        Assert.True(page.FindAll(".layer-row").Count >= 2);
    }

    private static void PointerDown(ComponentTestHarness page, string selector)
    {
        TestElement element = page.Find(selector);
        Rect bounds = element.Bounds;
        page.Dispatch(
            element,
            new PointerDownEvent(bounds.X + (bounds.Width * 0.5f), bounds.Y + (bounds.Height * 0.5f)),
            bubble: false);
        page.Dispatch(
            element,
            new PointerUpEvent(bounds.X + (bounds.Width * 0.5f), bounds.Y + (bounds.Height * 0.5f)),
            bubble: false);
    }
}
