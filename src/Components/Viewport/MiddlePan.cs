using Donkeh.LayoutLab;
using Donkeh.Windowing;
using VectorDotNet.Core;

namespace VectorDotNet.Components;

/// <summary>Middle-button panning for the document canvas.</summary>
/// <remarks>
/// Middle presses do not always produce a pointer-down on the canvas, so the
/// gesture is driven from pointer moves, exactly as the sibling PixelDotNet
/// editor does. Deltas use window coordinates; canvas-local deltas would feed
/// the pan offset back into themselves.
/// </remarks>
internal static class MiddlePan
{
    private static bool _panning;
    private static int _pressSequence;
    private static float _lastX;
    private static float _lastY;

    /// <summary>Handles a pointer move; true while the middle button owns it.</summary>
    public static bool Handle(EditorSession session)
    {
        int pressSequence = InputState.PressSequence;
        if (pressSequence != _pressSequence)
        {
            _pressSequence = pressSequence;
            _panning = false;
        }

        if (!InputState.Middle)
        {
            if (_panning)
            {
                _panning = false;
                InputState.CursorOverride = null;
            }

            return false;
        }

        float x = InputState.PointerX;
        float y = InputState.PointerY;
        InputState.CursorOverride = Cursor.Grabbing;

        if (!_panning)
        {
            _panning = true;
            _lastX = x;
            _lastY = y;
            return true;
        }

        float dx = x - _lastX;
        float dy = y - _lastY;
        _lastX = x;
        _lastY = y;

        if (dx != 0f || dy != 0f)
        {
            session.PanBy(dx, dy);
        }

        return true;
    }

    /// <summary>Ends a middle-pan gesture.</summary>
    public static void Reset()
    {
        _panning = false;
        InputState.CursorOverride = null;
    }
}
