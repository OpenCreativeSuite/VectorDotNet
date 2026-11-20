using System.Numerics;
using Donkeh.Testing;
using Donkeh.UILab;
using VectorDotNet.Components;
using VectorDotNet.Core;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace VectorDotNet.Tests;

/// <summary>Shared fixtures for the vector-engine and component tests.</summary>
internal static class V
{
    /// <summary>Creates a session with a stable viewport for component tests.</summary>
    public static EditorSession Session()
    {
        EditorSession session = new();
        session.SetViewSize(1280f, 800f);
        return session;
    }

    /// <summary>Creates a session with rulers and snapping off for pointer tests.</summary>
    public static EditorSession InputSession()
    {
        EditorSession session = new()
        {
            ShowRulers = false,
            SnapToObjects = false,
            SnapToGrid = false,
            SmartGuides = false,
        };
        session.SetViewSize(1280f, 800f);
        session.ActualPixels();
        return session;
    }

    /// <summary>Adds a live rectangle to the active layer.</summary>
    public static PathObject AddRect(
        EditorSession session,
        float x,
        float y,
        float width,
        float height,
        uint? fill = 0xFF4C8DFF,
        uint? stroke = null)
    {
        PathObject path = new()
        {
            Name = "Rectangle",
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = width, Height = height },
            Fill = fill.HasValue ? Paint.Solid(fill.Value) : null,
            Stroke = stroke.HasValue
                ? new StrokeStyle { Paint = Paint.Solid(stroke.Value), Width = 2f }
                : null,
            Transform = Mat.Translate(x + (width * 0.5f), y + (height * 0.5f)),
        };
        path.Rebuild();
        (session.Document.ActiveLayer ?? session.ActiveLayer).Add(path);
        return path;
    }

    /// <summary>Adds a live rectangle to a specific document.</summary>
    public static PathObject AddRectToDocument(
        Document document,
        float x,
        float y,
        float width,
        float height,
        uint fill)
    {
        PathObject path = new()
        {
            Shape = new ShapeSpec { Kind = ShapeKind.Rectangle, Width = width, Height = height },
            Fill = Paint.Solid(fill),
            Transform = Mat.Translate(x + (width * 0.5f), y + (height * 0.5f)),
        };
        path.Rebuild();
        document.ActiveLayer!.Add(path);
        return path;
    }

    /// <summary>Adds a live ellipse to the active layer.</summary>
    public static PathObject AddEllipse(
        EditorSession session,
        float x,
        float y,
        float width,
        float height,
        uint? fill = 0xFF4C8DFF)
    {
        PathObject path = new()
        {
            Name = "Ellipse",
            Shape = new ShapeSpec { Kind = ShapeKind.Ellipse, Width = width, Height = height },
            Fill = fill.HasValue ? Paint.Solid(fill.Value) : null,
            Transform = Mat.Translate(x + (width * 0.5f), y + (height * 0.5f)),
        };
        path.Rebuild();
        (session.Document.ActiveLayer ?? session.ActiveLayer).Add(path);
        return path;
    }

    /// <summary>Adds a text object to the active layer.</summary>
    public static TextObject AddText(
        EditorSession session,
        float x,
        float y,
        string text = "Text",
        float size = 48f)
    {
        TextObject item = new()
        {
            Text = text,
            FontSize = size,
            Fill = Paint.Solid(0xFF17191D),
            Transform = Mat.Translate(x, y),
        };
        (session.Document.ActiveLayer ?? session.ActiveLayer).Add(item);
        return item;
    }

    /// <summary>Drags a creation tool across document-space corners.</summary>
    public static void Drag(EditorSession session, Vec2 from, Vec2 to, bool shift = false, bool alt = false)
    {
        Vec2 start = session.DocumentToScreen(from);
        Vec2 end = session.DocumentToScreen(to);
        session.PointerDown(start, shift, alt, middle: false);
        session.PointerMove(end);
        session.PointerUp(end);
    }

    /// <summary>Runs the tool at a document point without dragging.</summary>
    public static void Click(EditorSession session, Vec2 point, bool shift = false, bool alt = false)
    {
        Vec2 screen = session.DocumentToScreen(point);
        session.PointerDown(screen, shift, alt, middle: false);
        session.PointerUp(screen);
    }

    /// <summary>Wraps a component in the inspector's provider.</summary>
    public static Node WithSession(EditorSession session, Node child) =>
        Provider(SessionContext.Value, session, child);

    /// <summary>Mounts a component against a supplied session.</summary>
    public static ComponentTestHarness Render(EditorSession session, Node child) =>
        ComponentTest.Render(WithSession(session, child));

    /// <summary>Re-renders a mounted component after a session mutation.</summary>
    public static void Reload<T>(ComponentTestHarness page, EditorSession session)
        where T : Component, new() =>
        page.Update(WithSession(session, C<T>()));

    /// <summary>Approximate bounds comparison.</summary>
    public static void Nearly(float expected, float actual, float tolerance = 0.05f) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
}

/// <summary>Writes the host's modifier snapshot for gesture tests.</summary>
internal static class InputControl
{
    /// <summary>Sets a boolean modifier such as Shift, Alt, Middle or Control.</summary>
    public static void Set(string name, bool value) =>
        typeof(Donkeh.Windowing.InputState)
            .GetProperty(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(null, [value]);

    /// <summary>Records the window-space pointer position.</summary>
    public static void Pointer(float x, float y) =>
        typeof(Donkeh.Windowing.InputState)
            .GetMethod("SetPointer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [x, y]);

    /// <summary>Sets the press counter used to discard stale pan gestures.</summary>
    public static void PressSequence(int value) =>
        typeof(Donkeh.Windowing.InputState)
            .GetProperty("PressSequence", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(null, [value]);

    /// <summary>Clears every modifier after a test.</summary>
    public static void Reset()
    {
        Set("Shift", false);
        Set("Alt", false);
        Set("Control", false);
        Set("Middle", false);
    }
}

/// <summary>A temporary folder that is removed after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "VectorDotNetTests",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Cleanup must not hide the assertion result.
        }
    }
}
